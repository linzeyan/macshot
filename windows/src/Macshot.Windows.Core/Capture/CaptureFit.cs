namespace Macshot.Windows.Core.Capture;

/// <summary>
/// How big a capture is shown when it opens in a window that can scroll it.
/// </summary>
/// <remarks>
/// <para>
/// The capture overlay never asks this. It covers a display and the display is the
/// capture, which is why <see cref="Viewport.MinScale"/> is 1 and why a capture bigger
/// than the screen has nowhere to be shown there. The editor window is the surface that
/// does hold one — a stitched page, a photo reopened from the history — and what it
/// opens at is the difference between a capture that can be worked on and a strip of
/// pixels.
/// </para>
/// </remarks>
public static class CaptureFit
{
    /// <summary>
    /// The magnification a capture <paramref name="captureWidth"/> across opens at inside
    /// a viewport <paramref name="viewportWidth"/> across, held between
    /// <paramref name="minimum"/> and <paramref name="maximum"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The width is fitted and the height is left to scroll, which is why the height of
    /// neither is asked for. macshot zooms out on open so a capture larger than the window
    /// opens whole rather than showing its top-left corner
    /// (<c>DetachedEditorWindowController.swift:209-212</c>); a capture whose full width is
    /// on show is not showing a corner. Fitting the height as well is what turns a page ten
    /// screens tall into a tenth-size strip with no legible text and nowhere to aim a mark,
    /// which is the same "nowhere to be marked up" one level down.
    /// </para>
    /// <para>
    /// One rule rather than a case for tall captures. Fitting the width and fitting both
    /// axes give the same number whenever the width is the binding constraint — every
    /// capture at least as wide, in proportion, as the viewport — and they part company
    /// only where fitting both would shrink the capture on account of its height. That is
    /// exactly the capture this exists for, so the tall one needs no branch of its own.
    /// </para>
    /// <para>
    /// Never magnified past 1:1. A small capture blown up to fill the window would be shown
    /// softer than it is, and the marks drawn on it would be at a size that means nothing.
    /// </para>
    /// </remarks>
    public static double OpeningZoom(
        double captureWidth,
        double viewportWidth,
        double minimum,
        double maximum)
    {
        // Asked before either has been arranged, which happens: a scroll viewer reports no
        // viewport until it has been laid out. 1:1 rather than the minimum, because it is
        // the answer that changes nothing when the question is asked again with real
        // numbers.
        if (captureWidth <= 0 || viewportWidth <= 0)
        {
            return Math.Clamp(1, minimum, maximum);
        }

        return Math.Clamp(Math.Min(1, viewportWidth / captureWidth), minimum, maximum);
    }
}
