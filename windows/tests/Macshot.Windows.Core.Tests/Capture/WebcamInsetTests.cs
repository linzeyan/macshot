using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

[TestClass]
public sealed class WebcamInsetTests
{
    private static readonly CaptureRegion Region = new(100, 200, 800, 600);

    [TestMethod]
    public void EachCornerIsTheSameDistanceFromTheTwoEdgesItTouches()
    {
        foreach (var corner in Enum.GetValues<WebcamCorner>())
        {
            var (x, y, width, height) = WebcamInset.For(Region, corner, WebcamInset.DefaultSide, 1);

            var left = x - Region.X;
            var right = Region.X + Region.Width - (x + width);
            var top = y - Region.Y;
            var bottom = Region.Y + Region.Height - (y + height);

            Assert.AreEqual(WebcamInset.Padding, Math.Min(left, right), $"{corner} horizontally");
            Assert.AreEqual(WebcamInset.Padding, Math.Min(top, bottom), $"{corner} vertically");
        }
    }

    /// <summary>
    /// The point of naming corners at all: each one has to be a different place, and a
    /// sign error would quietly put two of them on top of each other.
    /// </summary>
    [TestMethod]
    public void TheFourCornersAreFourDifferentPlaces()
    {
        var places = Enum.GetValues<WebcamCorner>()
            .Select(corner => WebcamInset.For(Region, corner, WebcamInset.DefaultSide, 1))
            .ToHashSet();

        Assert.AreEqual(4, places.Count);
    }

    /// <summary>
    /// Windows counts y downwards where macOS counts it up, so this is the assertion that
    /// catches the bubble macshot puts at the bottom appearing at the top.
    /// </summary>
    [TestMethod]
    public void BottomMeansTheBottomOfTheScreenAndNotTheTop()
    {
        var bottom = WebcamInset.For(Region, WebcamCorner.BottomRight, WebcamInset.DefaultSide, 1);
        var top = WebcamInset.For(Region, WebcamCorner.TopRight, WebcamInset.DefaultSide, 1);

        Assert.IsTrue(bottom.Y > top.Y, $"bottom {bottom.Y} should be below top {top.Y}");
    }

    /// <summary>
    /// The size is a number now rather than one of four names, so the number asked for is
    /// the number that has to arrive: anything that rounded or stepped it would turn a
    /// slider back into the presets it replaced.
    /// </summary>
    [TestMethod]
    public void ItIsAlwaysSquareAndTheSizeThatWasAskedFor()
    {
        foreach (var side in new double[] { 80, 97, 120, 301, 480 })
        {
            var (_, _, width, height) = WebcamInset.For(Region, WebcamCorner.TopLeft, side, 1);

            Assert.AreEqual(width, height, $"{side} is not square");
            Assert.AreEqual((int)side, width, $"{side}");
        }
    }

    /// <summary>
    /// The ends of the slider are the ends of what can be stored. A size off either end
    /// can only come from a hand-edited settings file or an imported one, and the bubble
    /// it would draw — a dot, or a window wider than the screen — is one no control in
    /// the app could show the user how to get back from.
    /// </summary>
    [TestMethod]
    public void ASizeOffEitherEndOfTheSliderIsBroughtBackToIt()
    {
        Assert.AreEqual(WebcamInset.MinimumSide, WebcamInset.Clamp(1));
        Assert.AreEqual(WebcamInset.MaximumSide, WebcamInset.Clamp(4000));
        Assert.AreEqual(WebcamInset.DefaultSide, WebcamInset.Clamp(double.NaN));
    }

    /// <summary>
    /// A bubble is a window, and a window is placed in whole pixels — so a slider dragged
    /// to a fraction has to settle somewhere the reading beside it can name.
    /// </summary>
    [TestMethod]
    public void AFractionalSizeIsRoundedRatherThanCarried()
    {
        Assert.AreEqual(121, WebcamInset.Clamp(120.6));
    }

    [TestMethod]
    public void ACircleIsCutAtHalfTheSideAndARoundedRectAtAFifth()
    {
        Assert.AreEqual(60, WebcamInset.CornerRadiusFor(120, WebcamShape.Circle));
        Assert.AreEqual(24, WebcamInset.CornerRadiusFor(120, WebcamShape.RoundedRect));
    }

    [TestMethod]
    public void TheBubbleAndItsInsetBothScaleWithTheDisplay()
    {
        var retina = new CaptureRegion(0, 0, 1600, 1200);

        var (x, y, width, _) = WebcamInset.For(retina, WebcamCorner.TopLeft, WebcamInset.DefaultSide, 2);

        Assert.AreEqual(240, width);
        Assert.AreEqual((int)(WebcamInset.Padding * 2), x);
        Assert.AreEqual((int)(WebcamInset.Padding * 2), y);
    }

    /// <summary>
    /// What a slider costs that four named steps did not: the largest of those was 220 and
    /// the largest of these is 480, so a size chosen against one recording is now easily
    /// wider than the next one. A bubble that covered the whole region would be a camera
    /// instead of a recording rather than a camera in the corner of one, so it gives way
    /// to the region and keeps the inset that tells it from a crop.
    /// </summary>
    [TestMethod]
    public void ABubbleTooBigForTheRegionShrinksToFitInsideItsPadding()
    {
        var small = new CaptureRegion(0, 0, 200, 160);

        var (x, y, width, height) = WebcamInset.For(small, WebcamCorner.TopLeft, 480, 1);

        Assert.AreEqual(160 - (int)(WebcamInset.Padding * 2), width);
        Assert.AreEqual(width, height);
        Assert.AreEqual((int)WebcamInset.Padding, x);
        Assert.AreEqual((int)WebcamInset.Padding, y);
    }

    /// <summary>
    /// The cut has to follow the bubble that was drawn and not the one that was asked for.
    /// Cutting a shrunk bubble to half the size the slider says is not a circle — it is a
    /// window with two corners taken off it.
    /// </summary>
    [TestMethod]
    public void AShrunkBubbleIsStillCutToACircle()
    {
        var small = new CaptureRegion(0, 0, 200, 160);

        var (_, _, width, _) = WebcamInset.For(small, WebcamCorner.TopLeft, 480, 1);

        Assert.AreEqual(width / 2.0, WebcamInset.CornerRadiusFor(width, WebcamShape.Circle));
    }

    /// <summary>
    /// Both sides of the shrink are in pixels: the region already is, and the size and the
    /// padding are taken there by the scale. Comparing a size in points against a region in
    /// pixels would shrink the bubble on exactly the displays with the most room for it —
    /// 240 pixels here is 120 points, which a 400-pixel region has ample room for.
    /// </summary>
    [TestMethod]
    public void TheRoomToFitInIsMeasuredInPixelsAndNotInPoints()
    {
        var doubled = new CaptureRegion(0, 0, 400, 320);

        var (_, _, width, _) = WebcamInset.For(doubled, WebcamCorner.TopLeft, 120, 2);

        Assert.AreEqual(240, width);
    }
}
