using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

[TestClass]
public sealed class BeautifyMeshTests
{
    /// <summary>
    /// A mesh with the middle control point pulled well off centre, so that a sampler
    /// that quietly ignored the points would give a different answer from one that
    /// solves for them.
    /// </summary>
    private static BeautifyMesh Warped() => new(
        [
            0, 0, 0.7, 0, 1, 0,
            0, 0.3, 0.25, 0.7, 1, 0.6,
            0, 1, 0.65, 1, 1, 1,
        ],
        [
            new AnnotationColor(0, 0, 0), new AnnotationColor(0, 0, 128), new AnnotationColor(0, 0, 255),
            new AnnotationColor(0, 128, 0), new AnnotationColor(128, 128, 128), new AnnotationColor(0, 128, 255),
            new AnnotationColor(255, 0, 0), new AnnotationColor(255, 0, 128), new AnnotationColor(255, 255, 255),
        ]);

    [TestMethod]
    public void Sample_AtAGridCorner_IsThatCornersOwnColour()
    {
        var mesh = Warped();

        // The four corners of the unit square are control points, so nothing is
        // interpolated at them: whatever the patch inversion does in between, it has to
        // agree with the definition here.
        Assert.AreEqual(mesh.Colors[0], mesh.Sample(0, 0));
        Assert.AreEqual(mesh.Colors[2], mesh.Sample(1, 0));
        Assert.AreEqual(mesh.Colors[6], mesh.Sample(0, 1));
        Assert.AreEqual(mesh.Colors[8], mesh.Sample(1, 1));
    }

    [TestMethod]
    public void Sample_AtTheDisplacedMiddlePoint_IsThatPointsColour()
    {
        // The middle control point sits at (0.25, 0.7) rather than at the centre. This
        // is the assertion that fails if the mesh is sampled as though it were a plain
        // bilinear square: at (0.25, 0.7) that would give a blend, not the grey itself.
        Assert.AreEqual(Warped().Colors[4], Warped().Sample(0.25, 0.7));
    }

    [TestMethod]
    public void CreateSampler_AnswersForAPointRegardlessOfWhichPointsCameBeforeIt()
    {
        var mesh = Warped();
        var sampler = mesh.CreateSampler();

        // A sampler scanning an image and a sampler asked for one point have to agree.
        // Two patches of a mesh this warped overlap near the displaced middle point, so
        // both can answer for a point there — and a sampler that tried the previous
        // pixel's patch first would answer by where it had been rather than by the mesh.
        for (var row = 0; row <= 32; row++)
        {
            for (var column = 0; column <= 32; column++)
            {
                var u = column / 32.0;
                var v = row / 32.0;
                Assert.AreEqual(mesh.Sample(u, v), sampler.Sample(u, v), $"at {u},{v}");
            }
        }
    }

    [TestMethod]
    public void Catalogue_HoldsOneUsableMeshForEachOfMacshotsFirstEighteenStyles()
    {
        Assert.AreEqual(18, BeautifyMeshes.Catalogue.Count);

        for (var index = 0; index < BeautifyMeshes.Catalogue.Count; index++)
        {
            var mesh = BeautifyMeshes.Catalogue[index];
            Assert.AreEqual(18, mesh.Points.Length, $"style {index}");
            Assert.AreEqual(9, mesh.Colors.Length, $"style {index}");
        }
    }

    [TestMethod]
    public void Catalogue_KeepsEveryBorderPointOnTheBorder()
    {
        // What makes the mesh cover the whole background. An edge control point pulled
        // inwards would leave a wedge of the output with no patch over it, and the
        // sampler would answer for it with whatever the nearest patch clamps to.
        foreach (var mesh in BeautifyMeshes.Catalogue)
        {
            for (var index = 0; index < 9; index++)
            {
                var (row, column) = (index / 3, index % 3);
                var x = mesh.Points[index * 2];
                var y = mesh.Points[(index * 2) + 1];

                if (row == 0)
                {
                    Assert.AreEqual(0, y);
                }

                if (row == 2)
                {
                    Assert.AreEqual(1, y);
                }

                if (column == 0)
                {
                    Assert.AreEqual(0, x);
                }

                if (column == 2)
                {
                    Assert.AreEqual(1, x);
                }
            }
        }
    }

    [TestMethod]
    public void Styles_AreMeshedForTheFirstEighteenAndLinearAfterThem()
    {
        Assert.IsNotNull(BeautifyRenderer.Styles[0].Mesh);
        Assert.IsNotNull(BeautifyRenderer.Styles[17].Mesh);

        // The nineteenth is where macshot's own linear gradients start, and drawing one
        // of those through a mesh would be inventing a background it does not have.
        Assert.IsNull(BeautifyRenderer.Styles[18].Mesh);
    }

    [TestMethod]
    public void Render_PaintsTheBackgroundFromTheMeshRatherThanTheFallbackStops()
    {
        var style = BeautifyRenderer.Styles[0];
        var (width, height, output) = BeautifyRenderer.Render(
            8,
            8,
            new byte[8 * 8 * 4],
            new BeautifyOptions(StyleIndex: 0, Padding: 4, ShadowRadius: 0));

        // The first background pixel, taken from the mesh and from the linear fallback
        // the styles used to be drawn with. They disagree, which is the point of the
        // change, so matching the first is evidence and matching the second would be
        // evidence that nothing happened.
        var meshed = BeautifyMeshes.Catalogue[0].Sample(0.5 / width, 0.5 / height);
        var linear = style.Sample(0);

        Assert.AreEqual(meshed.Blue, output[0]);
        Assert.AreEqual(meshed.Green, output[1]);
        Assert.AreEqual(meshed.Red, output[2]);
        Assert.AreNotEqual(linear, meshed);
    }
}
