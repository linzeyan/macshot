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
}
