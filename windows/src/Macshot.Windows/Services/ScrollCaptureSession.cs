using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Services;

/// <summary>One finished scroll capture, and why it ended.</summary>
public sealed record ScrollCaptureResult(CapturedFrame Frame, ScrollCaptureStop Stop, int Frames);

/// <summary>How far a scroll capture has got.</summary>
public sealed record ScrollCaptureProgress(int Frames, int Rows);

/// <summary>
/// The capture so far, shrunk to panel width. Top-down BGRA.
/// </summary>
/// <remarks>
/// Its own event rather than a field on <see cref="ScrollCaptureProgress"/>, because it
/// is produced every few frames rather than every one: composing it walks every row that
/// has been stitched, and a page eight thousand rows tall would be walked ten times a
/// second for a panel nobody can see change that fast.
/// </remarks>
public sealed record ScrollCapturePreview(byte[] Pixels, int Width, int Height);

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

    /// <summary>Where this capture stops, which is the user's limit or the buffer's.</summary>
    private readonly int _maximumHeight;

    /// <summary>Whether macshot turns the wheel, or the user does.</summary>
    private readonly bool _driven;

    /// <param name="maximumHeight">
    /// Rows past which to stop on purpose, or 0 for as far as the page goes. Clamped to
    /// <see cref="MaximumHeight"/> either way: that one is not a preference but the bound
    /// on a buffer that grows while the capture runs, and a feed that never ends would
    /// find it whatever the user asked for.
    /// </param>
    /// <param name="driven">
    /// Whether macshot turns the wheel. False leaves the scrolling to the user and the
    /// stopping to the Stop button: nothing a frame can show means "finished" when
    /// nobody is driving — a view that has not moved is someone thinking, and a view
    /// that will not match is someone who jumped a page.
    /// </param>
    public ScrollCaptureSession(
        Func<long, Task<CapturedFrame?>> captureWindow,
        ScrollDriver? driver = null,
        int maximumHeight = 0,
        bool driven = true)
    {
        _captureWindow = captureWindow ?? throw new ArgumentNullException(nameof(captureWindow));
        _driver = driver ?? new ScrollDriver();
        _maximumHeight = maximumHeight is > 0 and < MaximumHeight ? maximumHeight : MaximumHeight;
        _driven = driven;
    }

    /// <summary>
    /// Raised after each frame is taken in, so something on screen can say the
    /// desktop is being driven on purpose. Raised on whichever thread the capture is
    /// running on, which for the only caller is the UI thread.
    /// </summary>
    public event EventHandler<ScrollCaptureProgress>? Progressed;

    /// <summary>
    /// Raised with a small picture of the capture as it lengthens, for the panel beside
    /// the region. Every <see cref="PreviewEveryFrames"/> frames rather than every one.
    /// </summary>
    public event EventHandler<ScrollCapturePreview>? Previewed;

    /// <summary>
    /// How often the preview is rebuilt. Three is about twice a second at the settle
    /// delay a scroll capture runs at — often enough to watch, rare enough that the walk
    /// over every stitched row is not what the capture is spending its time on.
    /// </summary>
    private const int PreviewEveryFrames = 3;

    /// <summary>How wide the panel draws it — macshot's 200, <c>ScrollCapturePreviewPanel.swift:11</c>.</summary>
    public const int PreviewWidth = 200;

    /// <summary>
    /// Scrolls <paramref name="window"/> to its end, or until
    /// <paramref name="cancellation"/> asks it to stop, and returns everything seen
    /// on the way as one tall image.
    /// </summary>
    /// <param name="region">
    /// The part of the desktop to keep, in virtual space, or null for the whole window.
    /// A region is what the toolbar's button asks for: the window is still what gets
    /// scrolled, because a rectangle has no wheel, but only the pixels inside the
    /// rectangle are stitched — which is what lets a page be captured without the
    /// browser's chrome repeated down the side of it.
    /// </param>
    public async Task<ScrollCaptureResult> RunAsync(
        CaptureWindow window,
        CaptureRegion? region = null,
        CancellationToken cancellation = default)
    {
        if (!_driven)
        {
            // Forward, but the pointer stays where the user left it: they are the one
            // about to scroll, and parking it in the middle of the window would take
            // their hand off the wheel they were asked to turn. Nothing to restore
            // afterwards for the same reason.
            if (!ScrollDriver.TryBringForward(window))
            {
                throw new ExpectedFailureException(
                    Localization.L("macshot could not bring that window forward."));
            }

            return await CaptureAsync(window, region, cancellation);
        }

        var over = region is { } aimed
            ? new CapturePoint(aimed.X + (aimed.Width / 2), aimed.Y + (aimed.Height / 2))
            : (CapturePoint?)null;

        if (!_driver.TryTakeOver(window, out var cursor, over))
        {
            throw new ExpectedFailureException(
                Localization.L("macshot could not bring that window forward to scroll it."));
        }

        try
        {
            return await CaptureAsync(window, region, cancellation);
        }
        finally
        {
            // Always: the pointer was moved out from under the user, and leaving it
            // parked in the middle of someone else's window is the kind of thing a
            // failure must not also do.
            _driver.Restore(cursor);
        }
    }

    private async Task<ScrollCaptureResult> CaptureAsync(
        CaptureWindow window,
        CaptureRegion? region,
        CancellationToken cancellation)
    {
        var first = await _captureWindow(window.Id)
            ?? throw new ExpectedFailureException(
                Localization.L("Windows would not capture that window."));

        // Resolved once, against the first frame, and then held. Every later frame is
        // the same window after scrolling, so a crop that followed each frame's own
        // reported position would wander with a window the user nudged mid-capture and
        // silently stitch two different parts of it together.
        var crop = Resolve(first, region);
        var band = Cut(first, crop);

        if (band.Height < ScrollStitcher.BandHeight)
        {
            throw new ExpectedFailureException(Localization.L(region is null
                ? "That window is too short to scroll-capture."
                : "That region is too short to scroll-capture."));
        }

        var stitcher = new ScrollStitcher(band.Width, band.Height);
        var policy = new ScrollCapturePolicy(_maximumHeight, _driven);

        byte[]? pixels = band.Pixels;
        var frames = 0;

        while (true)
        {
            frames++;

            var outcome = pixels is not null
                ? stitcher.Add(pixels)
                : ScrollStitchOutcome.Rejected;

            Progressed?.Invoke(this, new ScrollCaptureProgress(frames, stitcher.Height));

            // Only when something was added: a frame that matched what is already there
            // would redraw the same picture, and the run is mostly those.
            if (Previewed is { } watcher
                && outcome is ScrollStitchOutcome.Seeded or ScrollStitchOutcome.Advanced
                && (frames == 1 || frames % PreviewEveryFrames == 0))
            {
                var (preview, width, height) = stitcher.ToPreview(PreviewWidth);
                if (height > 0)
                {
                    watcher(this, new ScrollCapturePreview(preview, width, height));
                }
            }

            var stop = policy.Observe(outcome, stitcher.Height);
            if (stop != ScrollCaptureStop.None)
            {
                return Finish(first, crop, stitcher, stop, frames);
            }

            if (cancellation.IsCancellationRequested)
            {
                // Stopping early is a complete capture of what was scrolled through,
                // not a failure: the user asked for it to end here.
                return Finish(first, crop, stitcher, ScrollCaptureStop.Complete, frames);
            }

            if (_driven)
            {
                _driver.StepDown();
            }

            try
            {
                await Task.Delay(SettleDelay, cancellation);
            }
            catch (TaskCanceledException)
            {
                return Finish(first, crop, stitcher, ScrollCaptureStop.Complete, frames);
            }

            // A frame of the wrong size is a window that resized under the capture.
            // Fed to the stitcher it would throw; treated as content that does not
            // match, it costs one frame and ends the run only if it keeps happening.
            var next = await _captureWindow(window.Id);
            pixels = next is not null && next.Width == first.Width && next.Height == first.Height
                ? Cut(next, crop).Pixels
                : null;
        }
    }

    /// <summary>
    /// Where <paramref name="region"/> falls inside a captured window frame, or the
    /// whole frame when nothing was aimed at.
    /// </summary>
    /// <remarks>
    /// A region that misses the window entirely falls back to the whole frame rather
    /// than throwing. It cannot happen from the toolbar — the window was chosen by
    /// looking under the region — and a scroll capture of the wrong size is a better
    /// answer than none at all.
    /// </remarks>
    private static CaptureRegion Resolve(CapturedFrame first, CaptureRegion? region)
    {
        var whole = new CaptureRegion(0, 0, first.Width, first.Height);
        if (region is not { } aimed)
        {
            return whole;
        }

        var local = new CaptureRegion(
            aimed.X - first.VirtualX,
            aimed.Y - first.VirtualY,
            aimed.Width,
            aimed.Height).Intersect(whole);

        return local.IsEmpty ? whole : local;
    }

    /// <summary>
    /// One frame's pixels as the stitcher wants them: the whole buffer when the crop is
    /// the whole frame, so an uncropped capture still costs nothing per frame.
    /// </summary>
    private static (int Width, int Height, byte[] Pixels) Cut(CapturedFrame frame, CaptureRegion crop)
    {
        if (crop.X == 0 && crop.Y == 0 && crop.Width == frame.Width && crop.Height == frame.Height)
        {
            return (frame.Width, frame.Height, frame.BgraPixels);
        }

        return FrameTransforms.Crop(frame.Width, frame.Height, frame.BgraPixels, crop);
    }

    /// <remarks>
    /// Positioned where the first frame's crop was. Every later frame is the same
    /// window after scrolling, so the stitched image starts where the capture started
    /// and grows downwards from there.
    /// </remarks>
    private static ScrollCaptureResult Finish(
        CapturedFrame first,
        CaptureRegion crop,
        ScrollStitcher stitcher,
        ScrollCaptureStop stop,
        int frames)
    {
        var stitched = new CapturedFrame(
            first.VirtualX + (int)crop.X,
            first.VirtualY + (int)crop.Y,
            stitcher.Width,
            stitcher.Height,
            stitcher.ToImage());

        return new ScrollCaptureResult(stitched, stop, frames);
    }
}
