using Macshot.Windows.Core.Imaging;
using Macshot.Windows.Core.Output;

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
/// <b>Package identity is required</b> for that backend: Windows AI Foundry is unreachable
/// from a process with no MSIX identity, and macshot builds unpackaged
/// (<c>WindowsPackageType=None</c>), so today it is unavailable even on hardware that could
/// run it. The check below asks about the capability rather than the packaging, so it
/// starts working the moment macshot ships with identity.
/// </para>
/// <para>
/// Which is why it is no longer the only backend. Anything Foundry will not answer for
/// falls through to <see cref="OnnxBackgroundRemover"/>, which runs a model macshot fetches
/// and executes itself and needs neither an NPU nor a package. Foundry is still preferred
/// where it exists — it is on the machine already, so it costs no download, and its model
/// is the larger of the two. The offline variant has no fallback, since fetching the model
/// is a network call: there this is the whole feature, as it was before.
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
    public static async Task<CapturedFrame> CutOutAsync(CapturedFrame frame, BackgroundRemovalBackend backend)
    {
        ArgumentNullException.ThrowIfNull(frame);

#if !OFFLINE
        // Asked for by name rather than resolved: someone who has chosen this has a reason,
        // and falling back would hide the machine that could not honour it.
        if (backend == BackgroundRemovalBackend.LocalModel)
        {
            return await OnnxBackgroundRemover.CutOutAsync(frame);
        }
#endif

        if (!IsSupported)
        {
#if OFFLINE
            // Nothing else to fall back to: the local model is fetched over the network, so
            // the variant that makes no network calls has only this backend.
            throw new InvalidOperationException(Unavailable);
#else
            // Named explicitly, so say why it cannot run rather than quietly running the
            // other one — the whole value of naming a backend is finding out when it is
            // absent.
            if (backend == BackgroundRemovalBackend.WindowsAi)
            {
                throw new InvalidOperationException(Unavailable);
            }

            return await OnnxBackgroundRemover.CutOutAsync(frame);
#endif
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

    /// <summary>What to say when Windows AI Foundry cannot run here.</summary>
    /// <remarks>
    /// Both reasons in one sentence. "Needs a Copilot+ PC" alone would be a lie on the
    /// machine that has one and is running an unpackaged build, and the user would go
    /// looking at their hardware for a fault that is in ours. Said in the offline build,
    /// which has no other backend, and in the ordinary one only when this backend was
    /// asked for by name.
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
    /// The pixels themselves go through <see cref="SubjectCutout"/>, which is also what the
    /// local model's backend uses. Two backends that disagreed about what to do with a mask
    /// would hand back visibly different edges for the same capture depending on which
    /// machine cut it, and only one of the two can be tested off Windows.
    /// </para>
    /// </remarks>
    private static CapturedFrame Cut(CapturedFrame frame, SoftwareBitmap mask)
    {
        var (maskWidth, maskHeight, coverage) = Read(mask);
        if (maskWidth <= 0 || maskHeight <= 0)
        {
            throw new InvalidOperationException(NoSubject);
        }

        var pixels = SubjectCutout.Cut(
            frame.BgraPixels,
            frame.Width,
            frame.Height,
            coverage,
            maskWidth,
            maskHeight,
            out var lifted);

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
