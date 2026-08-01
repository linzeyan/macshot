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
            var (x, y, width, height) = WebcamInset.For(Region, corner, WebcamSize.Medium, 1);

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
            .Select(corner => WebcamInset.For(Region, corner, WebcamSize.Medium, 1))
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
        var bottom = WebcamInset.For(Region, WebcamCorner.BottomRight, WebcamSize.Medium, 1);
        var top = WebcamInset.For(Region, WebcamCorner.TopRight, WebcamSize.Medium, 1);

        Assert.IsTrue(bottom.Y > top.Y, $"bottom {bottom.Y} should be below top {top.Y}");
    }

    [TestMethod]
    public void ItIsAlwaysSquareAndTheSizeTheSettingNames()
    {
        foreach (var size in Enum.GetValues<WebcamSize>())
        {
            var (_, _, width, height) = WebcamInset.For(Region, WebcamCorner.TopLeft, size, 1);

            Assert.AreEqual(width, height, $"{size} is not square");
            Assert.AreEqual((int)WebcamInset.SideFor(size), width, $"{size}");
        }
    }

    [TestMethod]
    public void TheStepsAreMacshotSFourAndTheyGrow()
    {
        Assert.AreEqual(80, WebcamInset.SideFor(WebcamSize.Small));
        Assert.AreEqual(120, WebcamInset.SideFor(WebcamSize.Medium));
        Assert.AreEqual(160, WebcamInset.SideFor(WebcamSize.Large));
        Assert.AreEqual(220, WebcamInset.SideFor(WebcamSize.ExtraLarge));
    }

    [TestMethod]
    public void ACircleIsCutAtHalfTheSideAndARoundedRectAtAFifth()
    {
        Assert.AreEqual(60, WebcamInset.CornerRadiusFor(WebcamSize.Medium, WebcamShape.Circle));
        Assert.AreEqual(24, WebcamInset.CornerRadiusFor(WebcamSize.Medium, WebcamShape.RoundedRect));
    }

    [TestMethod]
    public void TheBubbleAndItsInsetBothScaleWithTheDisplay()
    {
        var retina = new CaptureRegion(0, 0, 1600, 1200);

        var (x, y, width, _) = WebcamInset.For(retina, WebcamCorner.TopLeft, WebcamSize.Medium, 2);

        Assert.AreEqual(240, width);
        Assert.AreEqual((int)(WebcamInset.Padding * 2), x);
        Assert.AreEqual((int)(WebcamInset.Padding * 2), y);
    }

    /// <summary>
    /// A region smaller than the bubble is a region nobody should have to think about:
    /// the bubble stays its size and hangs off, which is visible and fixable, rather than
    /// being silently resized into something that is not the size that was chosen.
    /// </summary>
    [TestMethod]
    public void ARegionTooSmallForTheBubbleStillGetsTheBubbleItAskedFor()
    {
        var tiny = new CaptureRegion(0, 0, 60, 60);

        var (_, _, width, height) = WebcamInset.For(tiny, WebcamCorner.BottomRight, WebcamSize.Medium, 1);

        Assert.AreEqual(120, width);
        Assert.AreEqual(120, height);
    }
}
