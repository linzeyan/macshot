using Macshot.Windows.Core.Capture;

using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.FaceAnalysis;

namespace Macshot.Windows.Services;

/// <summary>
/// Where the faces are in a captured frame, for the redaction that covers them.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>VNDetectFaceRectanglesRequest</c> pass (<c>AutoRedactor.swift:126-169</c>),
/// answered here by <see cref="FaceDetector"/>. Unlike the subject model behind Remove
/// Background this one is part of Windows itself rather than of Windows AI Foundry: it
/// wants no NPU, no package identity and no model download, so the button works on every
/// machine macshot runs on.
/// </para>
/// <para>
/// It is a frontal detector and macshot's is too, so the two products miss the same
/// pictures — a face in profile, a face at an angle. That is worth saying out loud because
/// of what the button is for: it is a redaction, and a redaction that quietly missed one
/// is a leak. The caller reports the count for exactly this reason, so "3 covered" on a
/// photograph of four people is visibly wrong to the person who pressed it.
/// </para>
/// </remarks>
internal static class FaceFinder
{
    /// <summary>
    /// How much of the face's own size is added round each box.
    /// </summary>
    /// <remarks>
    /// macshot pads by a flat 4 points (<c>AutoRedactor.swift:142</c>). A fraction rather
    /// than a constant here because these boxes are in capture pixels, where 4 is a quarter
    /// of what it is in macshot's points on a Retina display — and because a detector's box
    /// stops at the chin and the hairline, leaving the parts of a head that identify
    /// someone as readily as the face does.
    /// </remarks>
    private const double Padding = 0.12;

    /// <summary>
    /// The faces in <paramref name="frame"/>, in the frame's own coordinates.
    /// </summary>
    /// <remarks>
    /// Empty rather than throwing when the platform has no detector: the caller has a
    /// "nothing found" answer to give already, and a machine without face analysis is not a
    /// fault to report as one.
    /// </remarks>
    public static async Task<IReadOnlyList<CaptureRegion>> FindAsync(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        FaceDetector detector;
        try
        {
            if (!FaceDetector.IsSupported)
            {
                return [];
            }

            detector = await FaceDetector.CreateAsync();
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write($"No face detector on this machine: {exception.Message}");
            return [];
        }

        // Gray8, because that is what the detector takes and BGRA8 is not among the
        // formats it accepts — converting here rather than letting it fail is the whole
        // of the interop. Luminance from the usual weights: a face found on a converted
        // frame is a face on the original, so the exact coefficients matter less than
        // that dark and light stay dark and light.
        var luminance = new byte[frame.Width * frame.Height];
        var pixels = frame.BgraPixels;
        for (var index = 0; index < luminance.Length; index++)
        {
            var offset = index * 4;
            luminance[index] = (byte)Math.Clamp(
                (0.114 * pixels[offset]) + (0.587 * pixels[offset + 1]) + (0.299 * pixels[offset + 2]),
                0,
                255);
        }

        using var gray = SoftwareBitmap.CreateCopyFromBuffer(
            Windows.Security.Cryptography.CryptographicBuffer.CreateFromByteArray(luminance),
            BitmapPixelFormat.Gray8,
            frame.Width,
            frame.Height);

        IList<DetectedFace> found;
        try
        {
            found = await detector.DetectFacesAsync(gray);
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write($"Looking for faces failed: {exception.Message}");
            return [];
        }

        var boxes = new List<CaptureRegion>(found.Count);
        foreach (var face in found)
        {
            var box = face.FaceBox;
            var padX = box.Width * Padding;
            var padY = box.Height * Padding;

            // Held inside the frame, so a face at the edge does not produce a redaction
            // reaching off it — the rasterizer would clip it anyway, but a box that says
            // it covers pixels that are not there is a box no test can check.
            var left = Math.Max(0, box.X - padX);
            var top = Math.Max(0, box.Y - padY);
            var right = Math.Min(frame.Width, box.X + box.Width + padX);
            var bottom = Math.Min(frame.Height, box.Y + box.Height + padY);

            if (right > left && bottom > top)
            {
                boxes.Add(new CaptureRegion(left, top, right - left, bottom - top));
            }
        }

        return boxes;
    }
}
