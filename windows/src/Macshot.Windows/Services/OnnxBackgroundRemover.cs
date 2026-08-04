#if !OFFLINE
using Macshot.Windows.Core.Imaging;

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

using static Macshot.Windows.Services.Localization;

namespace Macshot.Windows.Services;

/// <summary>
/// Cuts the subject out of a capture with a segmentation model macshot runs itself, on any
/// machine, rather than with the one Windows provides only on a Copilot+ PC.
/// </summary>
/// <remarks>
/// <para>
/// This exists because <see cref="BackgroundRemover"/>'s original backend answers
/// "unavailable" on almost every machine: Windows AI Foundry needs an NPU and a packaged
/// build, and macshot ships unpackaged. macOS runs subject lifting on every Mac from
/// Sonoma on, so a button that is drawn everywhere and works almost nowhere is not the
/// same product.
/// </para>
/// <para>
/// Compiled out of the offline variant along with the model store — the runtime itself is
/// a conditional package reference, so the offline build does not carry the 15 MB native
/// library for a feature it cannot reach.
/// </para>
/// <para>
/// CPU inference. The model is 4 MB and takes about a tenth of a second on a laptop for a
/// button the user pressed and is waiting on; a GPU execution provider would be another
/// hundred megabytes of native code to make a fast operation faster.
/// </para>
/// </remarks>
internal static class OnnxBackgroundRemover
{
    /// <summary>
    /// One session for the life of the process. Loading a model is the expensive half of
    /// running one, and a user who removes one background usually removes several.
    /// </summary>
    private static InferenceSession? _session;

    /// <summary>Serializes inference: a session is not documented as safe to run concurrently.</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>
    /// Whether this backend can run without going to the network — which is what decides
    /// whether pressing the button downloads something.
    /// </summary>
    internal static bool IsModelPresent => SubjectModelStore.IsReady;

    /// <summary>The frame with everything but its subject made transparent.</summary>
    /// <exception cref="InvalidOperationException">
    /// The model could not be fetched, or it found no subject to lift.
    /// </exception>
    internal static async Task<CapturedFrame> CutOutAsync(CapturedFrame frame, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var path = await SubjectModelStore.EnsureAsync(cancellation);

        await Gate.WaitAsync(cancellation);
        try
        {
            var session = _session ??= new InferenceSession(path);

            // Off the UI thread: a tenth of a second is long enough to drop frames in the
            // editor's own animation, and the caller is an event handler.
            return await Task.Run(() => Run(session, frame), cancellation);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static CapturedFrame Run(InferenceSession session, CapturedFrame frame)
    {
        var side = SubjectCutout.ModelSide;
        var tensor = new DenseTensor<float>(
            SubjectCutout.Prepare(frame.BgraPixels, frame.Width, frame.Height),
            [1, 3, side, side]);

        // By the model's own name for its input rather than a literal. U²-Net's is
        // "input.1", which is an export artefact rather than anything guaranteed.
        var input = session.InputMetadata.Keys.First();
        using var results = session.Run([NamedOnnxValue.CreateFromTensor(input, tensor)]);

        // The first output is the fused prediction; U²-Net emits six more, one per decoder
        // stage, which exist for training and are progressively coarser.
        var prediction = results.First().AsEnumerable<float>().ToArray();
        var coverage = SubjectCutout.ToCoverage(prediction);

        var pixels = SubjectCutout.Cut(
            frame.BgraPixels,
            frame.Width,
            frame.Height,
            coverage,
            side,
            side,
            out var lifted);

        // macshot says this rather than handing back a blank rectangle, and it is the
        // common failure: a region with no subject in it.
        if (lifted == 0)
        {
            throw new InvalidOperationException(L("Background removal failed — no clear subject found."));
        }

        return new CapturedFrame(
            frame.VirtualX,
            frame.VirtualY,
            frame.Width,
            frame.Height,
            pixels,
            hasAlpha: true);
    }
}
#endif
