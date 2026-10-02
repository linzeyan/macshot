using Macshot.Windows.Core.Annotations;

namespace Macshot.Windows.Core.Tests.Annotations;

[TestClass]
public sealed class LineStyleExtensionsTests
{
    [TestMethod]
    public void Dashed_UsesMacshotRelativeStrokeWidths()
    {
        var pattern = LineStyle.Dashed.CreateDashPattern(4);

        CollectionAssert.AreEqual(new[] { 12d, 8d }, pattern.ToArray());
    }

    [TestMethod]
    public void Dotted_EnforcesReadableMinimumSpacing()
    {
        var pattern = LineStyle.Dotted.CreateDashPattern(1);

        CollectionAssert.AreEqual(new[] { 0d, 6d }, pattern.ToArray());
    }

    /// <summary>
    /// That minimum is macshot's 6 points, so a dotted line drawn on a 200% screen is
    /// spaced 12 frame pixels apart: in frame pixels it would have twice as many dots
    /// there, and read as a different line.
    /// </summary>
    [TestMethod]
    public void Dotted_SpacesItsMinimumInPointsOfTheSurfaceDrawnOn()
    {
        var pattern = LineStyle.Dotted.CreateDashPattern(2, pixelsPerPoint: 2);

        CollectionAssert.AreEqual(new[] { 0d, 12d }, pattern.ToArray());
    }
}
