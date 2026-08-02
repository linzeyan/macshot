using Microsoft.Windows.AI;
using Microsoft.Windows.AI.Imaging;

using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;

using static Macshot.Windows.Services.Localization;

namespace Macshot.Windows.Services;

/// <summary>
/// Cuts the subject of a capture out of what is behind it, the Windows counterpart of
/// macshot's <c>VNGenerateForegroundInstanceMaskRequest</c> pass.
/// </summary>
/// <remarks>
/// <para>
/// <c>ImageObjectExtractor</c> is the nearest thing Windows has. It is not the same
/// bargain: macOS runs subject lifting on every Mac from Sonoma on, while this model is
/// part of Windows AI Foundry and runs only where there is an NPU to run it — a Copilot+
/// PC. So the button is drawn everywhere and answers with a reason where it cannot work,
/// rather than being quietly absent on most machines.
/// </para>
/// <para>
/// The model wants a hint saying where to look, where macOS's request takes none. The
/// whole frame is given as the one include-rect: the user has already said what the
/// subject is by dragging the region around it, and asking them to click it again would
/// be asking for something they have just told us.
/// </para>
/// <para>
/// <b>Package identity is required.</b> Windows AI Foundry is unreachable from a process
/// with no MSIX identity, and macshot currently builds unpackaged
/// (<c>WindowsPackageType=None</c>), so on today's builds this reports unavailable even
/// on hardware that could run it. The check below is written against the capability
/// rather than the packaging so it starts working the moment macshot ships with identity,
/// and <see cref="Unavailable"/> names both reasons.
/// </para>
/// </remarks>
internal static class BackgroundRemover
{
    /// <summary>
    /// Whether this machine can run the model at all, asked before anything is drawn or
    /// any model is fetched.
    /// </summary>
    /// <remarks>
    /// A property rather than a cached field: a Copilot+ PC does not stop being one, but
    /// the answer is also cheap, and a cache would hold a false from an early call made
    /// before the AI runtime finished coming up.
    /// </remarks>
    public static bool IsSupported
    {
        get
        {
            try
            {
                return AICapabilities.HasAICapability(AICapabilityCategory.CopilotPlusPC);
            }
            catch (Exception exception)
            {
                // No AI runtime to ask — which is the answer, not a fault to raise from a
                // property. An unpackaged process lands here.
                DiagnosticLog.Write($"Could not ask about Copilot+ support: {exception.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// The frame with everything but its subject made transparent.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The machine cannot run the model, the model could not be made ready, or it found
    /// no subject to lift. All three are things to tell the user rather than log: they
    /// pressed a button and are owed an answer.
    /// </exception>
    public static async Task<CapturedFrame> CutOutAsync(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (!IsSupported)
        {
            throw new InvalidOperationException(Unavailable);
        }

        await EnsureModelAsync();

        using var bitmap = frame.ToSoftwareBitmap();
        var extractor = await ImageObjectExtractor.CreateWithSoftwareBitmapAsync(bitmap);

        // The region itself is the hint. One rect and nothing else, which is what the
        // guidance asks for: several rects make the mask worse, and exclude points with
        // nothing to exclude from are an error.
        var hint = new ImageObjectExtractorHint(
            includeRects: new List<RectInt32> { new(0, 0, frame.Width, frame.Height) },
            includePoints: null,
            excludePoints: null);

        using var mask = extractor.GetSoftwareBitmapObjectMask(hint);
        return Cut(frame, mask);
    }

    /// <summary>What to say when the model cannot run here.</summary>
    /// <remarks>
    /// Both reasons in one sentence. "Needs a Copilot+ PC" alone would be a lie on the
    /// machine that has one and is running an unpackaged build, and the user would go
    /// looking at their hardware for a fault that is in ours.
    /// </remarks>
    private static string Unavailable =>
        L("Remove Background needs a Copilot+ PC and an installed build of macshot.");

    /// <summary>
    /// Waits for the model to be on the machine, fetching it the first time.
    /// </summary>
    /// <remarks>
    /// The download is the reason this is awaited rather than checked: the model is not
    /// shipped with Windows, and the first press on a fresh machine is the one that
    /// brings it down.
    /// </remarks>
    private static async Task EnsureModelAsync()
    {
        if (ImageObjectExtractor.GetReadyState() != AIFeatureReadyState.NotReady)
        {
            return;
        }

        var result = await ImageObjectExtractor.EnsureReadyAsync();
        if (result.Status != AIFeatureReadyResultState.Success)
        {
            throw new InvalidOperationException(
                L("The background removal model could not be prepared."),
                result.ExtendedError);
        }
    }

    /// <summary>
    /// Applies the model's mask to the frame's pixels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mask is greyscale, 255 on the subject and 0 everywhere else, so it is the
    /// alpha channel directly. Written straight rather than premultiplied, because that
    /// is what the PNG encoder is told the pixels are — see
    /// <see cref="CapturedFrame.HasAlpha"/>.
    /// </para>
    /// <para>
    /// Sampled by position rather than copied byte for byte. Nothing promises the mask
    /// comes back at the size it went in, and a mask read at the wrong stride is a
    /// cut-out sheared diagonally across the picture — a failure that looks like a bad
    /// model rather than a bad loop.
    /// </para>
    /// </remarks>
    private static CapturedFrame Cut(CapturedFrame frame, SoftwareBitmap mask)
    {
        var (maskWidth, maskHeight, coverage) = Read(mask);
        if (maskWidth <= 0 || maskHeight <= 0)
        {
            throw new InvalidOperationException(NoSubject);
        }

        var pixels = new byte[frame.BgraPixels.Length];
        frame.BgraPixels.CopyTo(pixels, 0);

        var lifted = 0;
        for (var y = 0; y < frame.Height; y++)
        {
            var maskRow = y * maskHeight / frame.Height * maskWidth;
            var row = y * frame.Width * 4;

            for (var x = 0; x < frame.Width; x++)
            {
                var alpha = coverage[maskRow + (x * maskWidth / frame.Width)];
                pixels[row + (x * 4) + 3] = alpha;

                if (alpha > 0)
                {
                    lifted++;
                }
            }
        }

        // macshot says the same thing rather than handing back a blank rectangle, and
        // the empty result is the common failure here: the model was given a region with
        // no subject in it.
        if (lifted == 0)
        {
            throw new InvalidOperationException(NoSubject);
        }

        return new CapturedFrame(
            frame.VirtualX,
            frame.VirtualY,
            frame.Width,
            frame.Height,
            pixels,
            hasAlpha: true);
    }

    /// <summary>macshot's own wording for a region the model found nothing in.</summary>
    private static string NoSubject => L("Background removal failed — no clear subject found.");

    /// <summary>
    /// The mask's size and one coverage byte per pixel.
    /// </summary>
    /// <remarks>
    /// Converted to BGRA before it is copied out. A greyscale bitmap's rows are padded to
    /// a stride this has no way to ask for, whereas four bytes a pixel is already aligned
    /// and comes back packed; the grey is then in every colour byte, so any one of them
    /// is the coverage.
    /// </remarks>
    private static (int Width, int Height, byte[] Coverage) Read(SoftwareBitmap mask)
    {
        using var wide = SoftwareBitmap.Convert(mask, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);

        // Fully qualified from the root: inside namespace Macshot.Windows.Services the
        // name "Windows" binds to Macshot.Windows, and Buffer alone would be System.Buffer.
        var buffer = new global::Windows.Storage.Streams.Buffer(
            (uint)checked(wide.PixelWidth * wide.PixelHeight * 4));
        wide.CopyToBuffer(buffer);
        CryptographicBuffer.CopyToByteArray(buffer, out var bytes);

        var pixels = wide.PixelWidth * wide.PixelHeight;
        if (bytes.Length < pixels * 4)
        {
            throw new InvalidOperationException(NoSubject);
        }

        var coverage = new byte[pixels];
        for (var pixel = 0; pixel < pixels; pixel++)
        {
            coverage[pixel] = bytes[pixel * 4];
        }

        return (wide.PixelWidth, wide.PixelHeight, coverage);
    }
}
