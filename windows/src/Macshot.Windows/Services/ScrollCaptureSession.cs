using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Services;

/// <summary>One finished scroll capture, and why it ended.</summary>
public sealed record ScrollCaptureResult(CapturedFrame Frame, ScrollCaptureStop Stop, int Frames);

/// <summary>How far a scroll capture has got.</summary>
public sealed record ScrollCaptureProgress(int Frames, int Rows);

/// <summary>
/// Captures a window taller than the screen, by taking frames of it while scrolling
/// it and stitching what each one reveals.
/// </summary>
/// <remarks>
/// <para>
/// This is the Windows half of scroll capture. The matching lives in
/// <see cref="ScrollStitcher"/> and the stopping in <see cref="ScrollCapturePolicy"/>,
/// both in Core and both tested; what is left here is the part that needs a real
/// desktop — asking the compositor for frames of one window, and turning the wheel
/// between them.
/// </para>
/// <para>
/// Frames come from the window's own capture item rather than from the screen, so
/// the overlay macshot has over everything, and anything else in front of the
/// window, is not in the result.
/// </para>
/// </remarks>
public sealed class ScrollCaptureSession
{
    /// <summary>
    /// Rows past which the capture stops. Far more than any page anyone means to
    /// capture, and a bound on the buffer for the feeds that never end.
    /// </summary>
    public const int MaximumHeight = 40_000;

    /// <summary>
    /// How long the view is given to finish scrolling and repaint before the next
    /// frame is taken. Too short and frames arrive mid-animation, which the stitcher
    /// rejects; too long and a page of any length becomes tedious. Smooth scrolling
    /// is what sets the floor here, not the capture.
    /// </summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(140);

    private readonly Func<long, Task<CapturedFrame?>> _captureWindow;
    private readonly ScrollDriver _driver;

    public ScrollCaptureSession(Func<long, Task<CapturedFrame?>> captureWindow, ScrollDriver? driver = null)
    {
        _captureWindow = captureWindow ?? throw new ArgumentNullException(nameof(captureWindow));
        _driver = driver ?? new ScrollDriver();
    }

    /// <summary>
    /// Raised after each frame is taken in, so something on screen can say the
    /// desktop is being driven on purpose. Raised on whichever thread the capture is
    /// running on, which for the only caller is the UI thread.
    /// </summary>
    public event EventHandler<ScrollCaptureProgress>? Progressed;

    /// <summary>
    /// Scrolls <paramref name="window"/> to its end, or until
    /// <paramref name="cancellation"/> asks it to stop, and returns everything seen
    /// on the way as one tall image.
    /// </summary>
    public async Task<ScrollCaptureResult> RunAsync(CaptureWindow window, CancellationToken cancellation = default)
    {
        if (!_driver.TryTakeOver(window, out var cursor))
        {
            throw new InvalidOperationException("macshot could not bring that window forward to scroll it.");
        }

        try
        {
            return await CaptureAsync(window, cancellation);
        }
        finally
        {
            // Always: the pointer was moved out from under the user, and leaving it
            // parked in the middle of someone else's window is the kind of thing a
            // failure must not also do.
            _driver.Restore(cursor);
        }
    }

    private async Task<ScrollCaptureResult> CaptureAsync(CaptureWindow window, CancellationToken cancellation)
    {
        var first = await _captureWindow(window.Id)
            ?? throw new InvalidOperationException("Windows would not capture that window.");

        if (first.Height < ScrollStitcher.BandHeight)
        {
            throw new InvalidOperationException("That window is too short to scroll-capture.");
        }

        var stitcher = new ScrollStitcher(first.Width, first.Height);
        var policy = new ScrollCapturePolicy(MaximumHeight);

        CapturedFrame? frame = first;
        var frames = 0;

        while (true)
        {
            frames++;

            // A frame of the wrong size is a window that resized under the capture.
            // Fed to the stitcher it would throw; treated as content that does not
            // match, it costs one frame and ends the run only if it keeps happening.
            var outcome = frame is not null
                && frame.Width == stitcher.Width
                && frame.Height == stitcher.FrameHeight
                    ? stitcher.Add(frame.BgraPixels)
                    : ScrollStitchOutcome.Rejected;

            Progressed?.Invoke(this, new ScrollCaptureProgress(frames, stitcher.Height));

            var stop = policy.Observe(outcome, stitcher.Height);
            if (stop != ScrollCaptureStop.None)
            {
                return Finish(first, stitcher, stop, frames);
            }

            if (cancellation.IsCancellationRequested)
            {
                // Stopping early is a complete capture of what was scrolled through,
                // not a failure: the user asked for it to end here.
                return Finish(first, stitcher, ScrollCaptureStop.Complete, frames);
            }

            _driver.StepDown();

            try
            {
                await Task.Delay(SettleDelay, cancellation);
            }
            catch (TaskCanceledException)
            {
                return Finish(first, stitcher, ScrollCaptureStop.Complete, frames);
            }

            frame = await _captureWindow(window.Id);
        }
    }

    /// <remarks>
    /// Positioned where the first frame was. Every later frame is the same window
    /// after scrolling, so the stitched image starts where the window started and
    /// grows downwards from there.
    /// </remarks>
    private static ScrollCaptureResult Finish(
        CapturedFrame first,
        ScrollStitcher stitcher,
        ScrollCaptureStop stop,
        int frames)
    {
        var stitched = new CapturedFrame(
            first.VirtualX,
            first.VirtualY,
            stitcher.Width,
            stitcher.Height,
            stitcher.ToImage());

        return new ScrollCaptureResult(stitched, stop, frames);
    }
}
