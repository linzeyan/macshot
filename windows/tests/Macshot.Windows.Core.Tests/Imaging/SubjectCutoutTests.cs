using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

[TestClass]
public sealed class SubjectCutoutTests
{
    private const int Side = SubjectCutout.ModelSide;

    /// <summary>
    /// The frame is BGRA and the model was trained on RGB. A swapped pair costs mask
    /// quality without costing a single error, so nothing else in the app would report it.
    /// </summary>
    [TestMethod]
    public void Prepare_PutsTheChannelsInTheOrderTheModelWasTrainedOn()
    {
        // Pure red, which is only distinguishable from pure blue by where it lands.
        var frame = Filled(4, 4, blue: 0, green: 0, red: 255);

        var tensor = SubjectCutout.Prepare(frame, 4, 4);

        var plane = Side * Side;
        Assert.IsTrue(tensor[0] > tensor[2 * plane], "the red channel must reach the model's first plane, not its third");
    }

    /// <summary>
    /// The reference implementation divides by the frame's own brightest channel rather
    /// than by 255. A dark capture normalized against 255 arrives as a picture the model
    /// was never shown, and comes back with no subject in it.
    /// </summary>
    [TestMethod]
    public void Prepare_NormalizesAgainstTheFramesOwnRangeSoADarkCaptureIsStillLegible()
    {
        var dim = Filled(4, 4, blue: 20, green: 20, red: 20);
        var bright = Filled(4, 4, blue: 255, green: 255, red: 255);

        var dimTensor = SubjectCutout.Prepare(dim, 4, 4);
        var brightTensor = SubjectCutout.Prepare(bright, 4, 4);

        Assert.AreEqual(brightTensor[0], dimTensor[0], 1e-5f, "a uniformly dim frame carries the same information as a bright one");
    }

    /// <summary>
    /// A frame with nothing in it must not produce NaNs. The division has no denominator
    /// there, and a NaN tensor comes back from the model as a mask that fails no check and
    /// cuts out nothing.
    /// </summary>
    [TestMethod]
    public void Prepare_SurvivesAFrameWithNoBrightnessInItAtAll()
    {
        var frame = Filled(4, 4, blue: 0, green: 0, red: 0);

        var tensor = SubjectCutout.Prepare(frame, 4, 4);

        foreach (var value in tensor)
        {
            Assert.IsFalse(float.IsNaN(value), "a black frame must normalize to numbers, not to NaN");
        }
    }

    /// <summary>
    /// The model takes a fixed square whatever the capture's shape. A tensor of the wrong
    /// length is rejected by the runtime, so this is the difference between the feature
    /// working and it throwing on every non-square selection.
    /// </summary>
    [TestMethod]
    public void Prepare_ProducesTheModelsInputSizeFromAnyShapeOfCapture()
    {
        var wide = SubjectCutout.Prepare(Filled(64, 9, 10, 20, 30), 64, 9);
        var tall = SubjectCutout.Prepare(Filled(9, 64, 10, 20, 30), 9, 64);

        Assert.AreEqual(3 * Side * Side, wide.Length);
        Assert.AreEqual(3 * Side * Side, tall.Length);
    }

    /// <summary>
    /// The model expresses a confident mask over whatever range it likes. Read literally,
    /// a prediction spanning 0.2 to 0.4 is a uniform haze over the whole picture instead of
    /// a subject cut out of it.
    /// </summary>
    [TestMethod]
    public void ToCoverage_StretchesANarrowPredictionAcrossTheFullRange()
    {
        float[] prediction = [0.2f, 0.3f, 0.4f];

        var coverage = SubjectCutout.ToCoverage(prediction);

        Assert.AreEqual(0, coverage[0]);
        Assert.AreEqual(255, coverage[2]);
        Assert.IsTrue(coverage[1] is > 120 and < 136, $"the middle of the range must land in the middle, was {coverage[1]}");
    }

    /// <summary>
    /// A model that separated nothing must produce an empty cut-out, which the user is
    /// told about. Stretched the other way it would produce a fully opaque one, which
    /// silently does nothing and looks like the button is broken.
    /// </summary>
    [TestMethod]
    public void ToCoverage_TreatsAFlatPredictionAsNoSubjectRatherThanAsAFullMask()
    {
        var coverage = SubjectCutout.ToCoverage([0.5f, 0.5f, 0.5f]);

        CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, coverage);
    }

    /// <summary>
    /// Nothing promises a mask comes back the size that went in — Foundry's does not. Read
    /// at the wrong stride it shears the cut-out diagonally across the picture, which reads
    /// as a bad model rather than as a bad loop.
    /// </summary>
    [TestMethod]
    public void Cut_SamplesTheMaskByPositionSoAMismatchedSizeCannotShearTheImage()
    {
        var frame = Filled(8, 8, 10, 20, 30);

        // A mask half the frame's size, opaque on its left half.
        var coverage = new byte[4 * 4];
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 2; x++)
            {
                coverage[(y * 4) + x] = 255;
            }
        }

        var cut = SubjectCutout.Cut(frame, 8, 8, coverage, 4, 4, out _);

        Assert.AreEqual(255, AlphaAt(cut, 8, 0, 4), "the mask's left half must cover the frame's left half at any scale");
        Assert.AreEqual(0, AlphaAt(cut, 8, 7, 4), "and its right half must stay clear");
    }

    /// <summary>
    /// A 320-pixel mask over a 4K capture is a twelvefold magnification. Sampled nearest it
    /// staircases every edge of the subject, which is the one part of a cut-out anyone
    /// looks at closely.
    /// </summary>
    [TestMethod]
    public void Cut_InterpolatesTheMaskSoAnUpscaledEdgeIsNotAStaircase()
    {
        var frame = Filled(16, 1, 10, 20, 30);
        byte[] coverage = [0, 255];

        var cut = SubjectCutout.Cut(frame, 16, 1, coverage, 2, 1, out _);

        var midway = AlphaAt(cut, 16, 8, 0);
        Assert.IsTrue(midway is > 0 and < 255, $"the step between two mask pixels must be spread across the frame, was {midway}");
    }

    /// <summary>
    /// Removing a background changes what is transparent, not what colour anything is. A
    /// backend that touched the colour channels would tint every capture it cut.
    /// </summary>
    [TestMethod]
    public void Cut_ChangesTransparencyAndNothingElse()
    {
        var frame = Filled(4, 4, blue: 11, green: 22, red: 33);
        var coverage = new byte[4 * 4];
        Array.Fill(coverage, (byte)128);

        var cut = SubjectCutout.Cut(frame, 4, 4, coverage, 4, 4, out _);

        for (var pixel = 0; pixel < 16; pixel++)
        {
            Assert.AreEqual(11, cut[pixel * 4]);
            Assert.AreEqual(22, cut[(pixel * 4) + 1]);
            Assert.AreEqual(33, cut[(pixel * 4) + 2]);
        }
    }

    /// <summary>
    /// The common failure is a region with no subject in it. macshot says so rather than
    /// handing back a blank rectangle, and this count is what lets it tell the difference.
    /// </summary>
    [TestMethod]
    public void Cut_ReportsThatNothingWasLiftedWhenTheMaskIsEmpty()
    {
        var frame = Filled(4, 4, 10, 20, 30);

        SubjectCutout.Cut(frame, 4, 4, new byte[16], 4, 4, out var lifted);

        Assert.AreEqual(0, lifted);
    }

    private static byte[] Filled(int width, int height, byte blue, byte green, byte red)
    {
        var pixels = new byte[width * height * 4];
        for (var pixel = 0; pixel < width * height; pixel++)
        {
            pixels[pixel * 4] = blue;
            pixels[(pixel * 4) + 1] = green;
            pixels[(pixel * 4) + 2] = red;
            pixels[(pixel * 4) + 3] = 255;
        }

        return pixels;
    }

    private static int AlphaAt(byte[] pixels, int width, int x, int y) => pixels[(((y * width) + x) * 4) + 3];
}
