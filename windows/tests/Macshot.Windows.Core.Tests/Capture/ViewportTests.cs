using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

/// <summary>
/// Zooming and panning the capture under the overlay.
/// </summary>
[TestClass]
public sealed class ViewportTests
{
    private static readonly CaptureRegion View = new(0, 0, 1920, 1080);

    [TestMethod]
    public void ZoomingHoldsWhateverIsUnderThePointer()
    {
        // Zooming is aimed at something. One that pulled that something off to one side
        // would have to be followed by a pan every single time.
        var anchor = new CapturePoint(1200, 700);
        var held = Viewport.Identity.ToContent(anchor);

        var zoomed = Viewport.Identity.ZoomedAt(2, anchor, View);

        Assert.AreEqual(2, zoomed.Scale);
        Assert.AreEqual(anchor.X, zoomed.ToView(held).X, 0.001);
        Assert.AreEqual(anchor.Y, zoomed.ToView(held).Y, 0.001);
    }

    [TestMethod]
    public void TheCaptureAlwaysCoversTheOverlay()
    {
        // An offset that let the edge in would show a strip of empty window where the
        // screen ought to be.
        var zoomed = Viewport.Identity
            .ZoomedAt(2, new CapturePoint(0, 0), View)
            .PannedBy(500, 500, View);

        Assert.AreEqual(0, zoomed.OffsetX, "cannot slide past the left edge");
        Assert.AreEqual(0, zoomed.OffsetY);

        var farOff = zoomed.PannedBy(-99999, -99999, View);

        Assert.AreEqual(-1920, farOff.OffsetX, "cannot slide past the right edge");
        Assert.AreEqual(-1080, farOff.OffsetY);
    }

    [TestMethod]
    public void ZoomingBackOutLandsExactlyWhereItStarted()
    {
        var anchor = new CapturePoint(700, 300);

        var round = Viewport.Identity
            .ZoomedAt(4, anchor, View)
            .ZoomedAt(0.25, anchor, View);

        Assert.IsTrue(round.IsIdentity, $"came back to {round}");
    }

    [TestMethod]
    public void ItNeverShowsLessThanTheDisplay()
    {
        // The display is the capture. There is nothing to see further out than all of it.
        var out1 = Viewport.Identity.ZoomedAt(0.5, new CapturePoint(0, 0), View);

        Assert.AreEqual(Viewport.Identity, out1);
    }

    [TestMethod]
    public void ItStopsAtTheDeepestMagnification()
    {
        var deep = Viewport.Identity;
        for (var step = 0; step < 20; step++)
        {
            deep = deep.ZoomedAt(2, new CapturePoint(960, 540), View);
        }

        Assert.AreEqual(Viewport.MaxScale, deep.Scale);
    }

    [TestMethod]
    public void PointsGoBothWays()
    {
        var viewport = Viewport.Identity.ZoomedAt(3, new CapturePoint(400, 800), View);
        var point = new CapturePoint(123, 456);

        var round = viewport.ToContent(viewport.ToView(point));

        Assert.AreEqual(point.X, round.X, 0.001);
        Assert.AreEqual(point.Y, round.Y, 0.001);
    }
}
