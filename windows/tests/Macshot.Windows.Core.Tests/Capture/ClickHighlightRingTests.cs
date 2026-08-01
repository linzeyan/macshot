using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

/// <summary>
/// The ring a click leaves behind in a recording.
/// </summary>
/// <remarks>
/// These pin macshot's numbers rather than the drawing. A ring that grew at a different
/// rate, or faded over a different second, would still look like a click ripple in a
/// screenshot of the code and quite unlike macshot's in a recording played side by side —
/// which is the only place the difference shows.
/// </remarks>
[TestClass]
public sealed class ClickHighlightRingTests
{
    private static byte[] Buffer() => new byte[ClickHighlightRing.Extent * ClickHighlightRing.Extent * 4];

    [TestMethod]
    public void ItStartsAtEighteenAndGrowsBySixtyASecond()
    {
        Assert.AreEqual(18, ClickHighlightRing.RadiusAt(0));
        Assert.AreEqual(36, ClickHighlightRing.RadiusAt(ClickHighlightRing.Lifetime), 1e-9);
    }

    [TestMethod]
    public void ItIsGoneAfterThreeTenthsOfASecond()
    {
        Assert.IsTrue(ClickHighlightRing.IsAlive(0));
        Assert.IsTrue(ClickHighlightRing.IsAlive(0.29));
        Assert.IsFalse(ClickHighlightRing.IsAlive(ClickHighlightRing.Lifetime));
        Assert.AreEqual(0, ClickHighlightRing.FadeAt(ClickHighlightRing.Lifetime));
    }

    [TestMethod]
    public void TheFadeIsLinearAcrossTheLifetime()
    {
        Assert.AreEqual(1, ClickHighlightRing.FadeAt(0), 1e-9);
        Assert.AreEqual(0.5, ClickHighlightRing.FadeAt(ClickHighlightRing.Lifetime / 2), 1e-9);
    }

    /// <summary>
    /// The whole ring has to fit, or the last frame of the animation is drawn clipped —
    /// the one frame where it is largest and most visible.
    /// </summary>
    [TestMethod]
    public void TheBufferHoldsTheRingAtItsLargest()
    {
        var widest = ClickHighlightRing.RadiusAt(ClickHighlightRing.Lifetime) * 2;

        Assert.IsTrue(ClickHighlightRing.Extent >= widest, $"{ClickHighlightRing.Extent} < {widest}");
    }

    [TestMethod]
    public void TheCentreCarriesTheFillAlphaAndNothingElse()
    {
        var pixels = Buffer();
        ClickHighlightRing.Rasterize(0, 1, pixels);

        var middle = (((ClickHighlightRing.Extent / 2) * ClickHighlightRing.Extent) + (ClickHighlightRing.Extent / 2)) * 4;

        // 0.35 of 255, macshot's fill, at full fade.
        Assert.AreEqual(89, pixels[middle + 3]);
    }

    /// <summary>
    /// The outline is the part that says where the ring's edge is; drawn at the fill's
    /// alpha it would read as a soft blob rather than as a ring.
    /// </summary>
    [TestMethod]
    public void TheOutlineIsDarkerThanTheDiscItRingsAndSitsInsideTheEdge()
    {
        var pixels = Buffer();
        ClickHighlightRing.Rasterize(0, 1, pixels);

        var centre = ClickHighlightRing.Extent / 2;
        var row = centre * ClickHighlightRing.Extent;

        // The outline is centred two inside the radius, so it lands on that column.
        var onTheOutline = (row + centre + (int)(ClickHighlightRing.StartRadius - ClickHighlightRing.StrokeInset)) * 4;
        var justInside = (row + centre + (int)ClickHighlightRing.StartRadius - 8) * 4;

        Assert.IsTrue(
            pixels[onTheOutline + 3] > pixels[justInside + 3],
            $"outline {pixels[onTheOutline + 3]} should be stronger than fill {pixels[justInside + 3]}");
    }

    [TestMethod]
    public void ItIsYellowAndPremultiplied()
    {
        var pixels = Buffer();
        ClickHighlightRing.Rasterize(0, 1, pixels);

        var middle = (((ClickHighlightRing.Extent / 2) * ClickHighlightRing.Extent) + (ClickHighlightRing.Extent / 2)) * 4;
        var alpha = pixels[middle + 3];

        // systemYellow is 255, 204, 0 — premultiplied, no channel may exceed the alpha.
        Assert.AreEqual(0, pixels[middle]);
        Assert.IsTrue(pixels[middle + 1] < pixels[middle + 2]);
        Assert.AreEqual(alpha, pixels[middle + 2]);
    }

    [TestMethod]
    public void NothingIsDrawnOnceTheRingIsOver()
    {
        var pixels = Buffer();
        pixels[0] = 200;

        var written = ClickHighlightRing.Rasterize(ClickHighlightRing.Lifetime, 1, pixels);

        Assert.AreEqual(0, written);
        Assert.AreEqual(0, pixels[0], "the buffer is reused, so a dead ring must clear what the last one left");
    }

    /// <summary>
    /// A dense display would otherwise get a ring half the size macshot draws.
    /// </summary>
    [TestMethod]
    public void ADenserDisplayGetsAProportionallyLargerRing()
    {
        Assert.IsTrue(ClickHighlightRing.ExtentAt(2) > ClickHighlightRing.ExtentAt(1) * 1.9);

        var pixels = new byte[ClickHighlightRing.ExtentAt(2) * ClickHighlightRing.ExtentAt(2) * 4];
        var extent = ClickHighlightRing.ExtentAt(2);
        ClickHighlightRing.Rasterize(0, 2, pixels);

        // Twice the radius means the ring still reaches its own edge and no further.
        var row = (extent / 2) * extent;
        Assert.AreEqual(0, pixels[(row + 1) * 4 + 3], "the ring must not touch the buffer's edge");
        Assert.IsTrue(pixels[(row + (extent / 2)) * 4 + 3] > 0, "and must still fill its centre");
    }

    [TestMethod]
    public void TheCornersStayEmptyAtEveryAge()
    {
        var pixels = Buffer();

        foreach (var age in new[] { 0, 0.1, 0.2, 0.29 })
        {
            ClickHighlightRing.Rasterize(age, 1, pixels);

            Assert.AreEqual(0, pixels[3], $"top-left corner is inside the ring at {age}");
        }
    }
}
