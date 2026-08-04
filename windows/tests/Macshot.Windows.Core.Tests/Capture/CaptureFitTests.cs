using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

/// <summary>
/// What a capture opens at in the window that has to scroll it.
/// </summary>
[TestClass]
public sealed class CaptureFitTests
{
    /// <summary>As the editor's scroll viewer is bounded.</summary>
    private const double Minimum = 0.1;

    private const double Maximum = 8;

    [TestMethod]
    public void APageTallerThanTheWindowOpensBigEnoughToReadAndDrawOn()
    {
        // The reason this exists. A scroll capture ten screens tall fitted on both axes
        // opens at a tenth of its size, where no text is legible and no mark can be aimed —
        // which is the same "nowhere to be marked up" the overlay had, one window along.
        // Its width fits, so it opens at its own size and the rest is scrolled to.
        var opening = CaptureFit.OpeningZoom(800, 1150, Minimum, Maximum);

        Assert.AreEqual(1, opening, 0.0001);
    }

    [TestMethod]
    public void ACaptureWiderThanTheWindowStillOpensWhole()
    {
        // macshot's own reason for zooming out on open: a capture larger than the window
        // must not open showing its top-left corner. Fitting the width is what answers
        // that, and it has to keep answering it.
        var opening = CaptureFit.OpeningZoom(2300, 1150, Minimum, Maximum);

        Assert.AreEqual(0.5, opening, 0.0001);
    }

    [TestMethod]
    public void ACaptureSmallerThanTheWindowIsNeverBlownUp()
    {
        // A capture magnified to fill the window is shown softer than it is, and the marks
        // drawn on it end up at a size that means nothing once it is delivered at 1:1.
        var opening = CaptureFit.OpeningZoom(400, 1150, Minimum, Maximum);

        Assert.AreEqual(1, opening, 0.0001);
    }

    [TestMethod]
    public void AWidthTooGreatToFitAtAllStopsAtTheViewersOwnFloor()
    {
        // The scroll viewer will not go below its minimum, so a zoom asked for underneath
        // it would be silently overruled and the reading in the top bar would be a lie.
        var opening = CaptureFit.OpeningZoom(60_000, 1150, Minimum, Maximum);

        Assert.AreEqual(Minimum, opening, 0.0001);
    }

    [TestMethod]
    public void AViewportNotYetArrangedIsAnsweredWithNoChange()
    {
        // A scroll viewer reports no viewport until it has been laid out, and the question
        // is asked from a size-changed handler that runs then. Answering 1:1 leaves the
        // capture exactly as it is until the size is real; answering the floor would open
        // every capture at a tenth and never recover.
        Assert.AreEqual(1, CaptureFit.OpeningZoom(800, 0, Minimum, Maximum), 0.0001);
        Assert.AreEqual(1, CaptureFit.OpeningZoom(0, 1150, Minimum, Maximum), 0.0001);
    }

    [TestMethod]
    public void FittingTheWidthOnlyDiffersFromFittingBothWhereTheHeightIsWhatBinds()
    {
        // The claim that makes this one rule rather than a special case for tall captures.
        // If it stopped holding, every ordinary capture would silently start opening at a
        // different magnification than it does today.
        foreach (var (width, height) in new[] { (2300.0, 400.0), (1150.0, 700.0), (400.0, 200.0) })
        {
            var bothAxes = Math.Clamp(
                Math.Min(1, Math.Min(1150 / width, 700 / height)),
                Minimum,
                Maximum);

            Assert.AreEqual(
                bothAxes,
                CaptureFit.OpeningZoom(width, 1150, Minimum, Maximum),
                0.0001,
                $"{width}x{height} is not taller than the viewport in proportion");
        }
    }
}
