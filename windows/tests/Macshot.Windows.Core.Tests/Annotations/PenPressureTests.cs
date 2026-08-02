using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Annotations;

[TestClass]
public sealed class PenPressureTests
{
    /// <summary>
    /// A mouse and a finger report a pressure the user did not choose, and the host says so
    /// by passing zero. Recorded anyway, every stroke would come out narrower than the
    /// width slider says while varying nothing — a setting that can only do harm.
    /// </summary>
    [TestMethod]
    public void Stroke_CarriesNoPressureFromADeviceThatReportsNone()
    {
        var editor = Pencil(penPressure: true);

        Draw(editor, pressure: 0);

        Assert.AreEqual(0, editor.Document.Annotations[0].Pressures.Count);
    }

    /// <summary>
    /// The option is what the user asked for, so a pen has to be ignored while it is off.
    /// </summary>
    [TestMethod]
    public void Stroke_CarriesNoPressureWhileTheOptionIsOff()
    {
        var editor = Pencil(penPressure: false);

        Draw(editor, pressure: 0.8);

        Assert.AreEqual(0, editor.Document.Annotations[0].Pressures.Count);
    }

    /// <summary>
    /// Smoothing changes how many samples there are and the pressures are one per sample.
    /// Left unresampled they would be dropped by the rasterizer's count check, so the two
    /// options would quietly cancel each other out — and smoothing is on by default.
    /// </summary>
    [TestMethod]
    public void Stroke_KeepsItsPressuresPairedWithItsSamplesAfterSmoothing()
    {
        var editor = Pencil(penPressure: true);
        editor.Smoothing = PencilSmoothing.Smooth;

        Draw(editor, pressure: 0.7);

        var stroke = editor.Document.Annotations[0];
        Assert.IsTrue(stroke.Points.Count > 0);
        Assert.AreEqual(stroke.Points.Count, stroke.Pressures.Count);
    }

    /// <summary>
    /// A digitizer reporting zero for one frame is reporting noise, not a lifted pen.
    /// Honoured, it would put a break in a stroke drawn in one movement.
    /// </summary>
    [TestMethod]
    public void Stroke_DoesNotBreakWhereTheDigitizerReportsNothing()
    {
        var editor = Pencil(penPressure: true);
        editor.Smoothing = PencilSmoothing.None;

        editor.PointerPressed(new CapturePoint(0, 0), pressure: 0.9);
        editor.PointerMoved(new CapturePoint(10, 0), pressure: 0);
        editor.PointerReleased(new CapturePoint(20, 0), pressure: 0.9);

        Assert.IsTrue(
            editor.Document.Annotations[0].Pressures.All(weight => weight > 0),
            "A sample was recorded at no pressure at all.");
    }

    /// <summary>
    /// The whole point: a stroke pressed hard at one end has to come out wider there. A
    /// mapping that ignored the pressures would pass every test above and still draw an
    /// even line.
    /// </summary>
    [TestMethod]
    public void Rasterizer_DrawsAPressedStrokeWiderThanALightOne()
    {
        const int width = 64;
        const int height = 32;

        var samples = new[] { new CapturePoint(4, 16), new CapturePoint(32, 16), new CapturePoint(60, 16) };
        var stroke = Annotation.CreateFreeform(
            AnnotationTool.Pencil,
            samples,
            AnnotationStyle.Default with { StrokeWidth = 12 },
            [0.05, 0.05, 1.0]);

        var pixels = AnnotationRasterizer.Render(width, height, new byte[width * height * 4], [stroke]);

        Assert.IsTrue(
            Thickness(pixels, width, height, 56) > Thickness(pixels, width, height, 8),
            "The pressed end of the stroke is no wider than the light end.");
    }

    /// <summary>How many rows the stroke covers in one column.</summary>
    private static int Thickness(byte[] pixels, int width, int height, int column)
    {
        var rows = 0;
        for (var y = 0; y < height; y++)
        {
            if (pixels[((y * width) + column) * 4] != 0)
            {
                rows++;
            }
        }

        return rows;
    }

    private static AnnotationEditor Pencil(bool penPressure) =>
        new(new AnnotationDocument())
        {
            Tool = AnnotationTool.Pencil,
            PenPressure = penPressure,
        };

    private static void Draw(AnnotationEditor editor, double pressure)
    {
        editor.PointerPressed(new CapturePoint(0, 0), pressure: pressure);
        for (var step = 1; step <= 6; step++)
        {
            editor.PointerMoved(new CapturePoint(step * 5, step % 2), pressure: pressure);
        }

        editor.PointerReleased(new CapturePoint(35, 0), pressure: pressure);
    }
}
