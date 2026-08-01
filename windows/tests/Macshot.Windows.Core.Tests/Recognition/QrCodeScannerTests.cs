using Macshot.Windows.Core.Recognition;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace Macshot.Windows.Core.Tests.Recognition;

[TestClass]
public sealed class QrCodeScannerTests
{
    [TestMethod]
    public void Scan_ReadsWhatWasEncoded()
    {
        var image = Draw(Encode("https://macshot.app/download"));

        var found = QrCodeScanner.Scan(image.Bgra, image.Width, image.Height);

        Assert.AreEqual(1, found.Count);
        Assert.AreEqual("https://macshot.app/download", found[0].Value);
    }

    [TestMethod]
    public void Scan_FindsEveryCodeInTheSamePicture()
    {
        // The case this exists for: a page showing two codes, not a photograph of one.
        var image = SideBySide(Encode("first"), Encode("second"));

        var found = QrCodeScanner.Scan(image.Bgra, image.Width, image.Height)
            .Select(code => code.Value)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(new[] { "first", "second" }, found);
    }

    [TestMethod]
    public void Scan_OffersOneRowForACodeThatAppearsTwice()
    {
        var image = SideBySide(Encode("same"), Encode("same"));

        var found = QrCodeScanner.Scan(image.Bgra, image.Width, image.Height);

        Assert.AreEqual(1, found.Count);
        Assert.AreEqual("same", found[0].Value);
    }

    [TestMethod]
    public void Scan_AnswersNothingRatherThanThrowingOnAPictureWithNoCodeInIt()
    {
        // A capture with no QR code in it is the ordinary case — this runs on every
        // text recognition — and it must not cost the recognized text.
        var blank = new Image(new byte[200 * 200 * 4], 200, 200);
        Array.Fill(blank.Bgra, (byte)0xFF);

        Assert.AreEqual(0, QrCodeScanner.Scan(blank.Bgra, blank.Width, blank.Height).Count);
    }

    [TestMethod]
    public void Scan_AnswersNothingForABufferThatCannotBeThatPicture()
    {
        Assert.AreEqual(0, QrCodeScanner.Scan([], 0, 0).Count);
        Assert.AreEqual(0, QrCodeScanner.Scan([], 10, 10).Count);
        Assert.AreEqual(0, QrCodeScanner.Scan(new byte[16], 10, 10).Count);
        Assert.AreEqual(0, QrCodeScanner.Scan(new byte[400], -1, 10).Count);
    }

    [TestMethod]
    public void Url_OffersToOpenOnlyWhatIsSafeToHandToTheShell()
    {
        // A QR code is something a stranger printed. Opening one that names a local
        // file or a custom scheme is a way to make a screenshot run something.
        Assert.IsNotNull(new QrCode("https://example.com/a").Url);
        Assert.IsNotNull(new QrCode("http://example.com").Url);

        Assert.IsNull(new QrCode("file:///C:/Windows/System32/calc.exe").Url);
        Assert.IsNull(new QrCode("ftp://example.com/x").Url);
        Assert.IsNull(new QrCode("javascript:alert(1)").Url);
        Assert.IsNull(new QrCode("WIFI:S=guest;T=WPA;P=hunter2;;").Url);
        Assert.IsNull(new QrCode("just some text").Url);
    }

    private sealed record Image(byte[] Bgra, int Width, int Height);

    private static BitMatrix Encode(string content) =>
        new QRCodeWriter().encode(
            content,
            BarcodeFormat.QR_CODE,
            160,
            160,
            new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = 4 });

    /// <summary>Renders a matrix as opaque BGRA, set bits black.</summary>
    private static Image Draw(BitMatrix matrix)
    {
        var bgra = new byte[matrix.Width * matrix.Height * 4];
        for (var y = 0; y < matrix.Height; y++)
        {
            for (var x = 0; x < matrix.Width; x++)
            {
                var value = matrix[x, y] ? (byte)0 : (byte)0xFF;
                var at = ((y * matrix.Width) + x) * 4;
                bgra[at] = value;
                bgra[at + 1] = value;
                bgra[at + 2] = value;
                bgra[at + 3] = 0xFF;
            }
        }

        return new Image(bgra, matrix.Width, matrix.Height);
    }

    /// <summary>Two codes on one white sheet, with a gap so neither runs into the other.</summary>
    private static Image SideBySide(BitMatrix left, BitMatrix right)
    {
        const int Gap = 40;
        var width = left.Width + Gap + right.Width;
        var height = Math.Max(left.Height, right.Height);
        var sheet = new byte[width * height * 4];
        Array.Fill(sheet, (byte)0xFF);

        Blit(sheet, width, Draw(left), 0);
        Blit(sheet, width, Draw(right), left.Width + Gap);
        return new Image(sheet, width, height);
    }

    private static void Blit(byte[] sheet, int sheetWidth, Image source, int atX)
    {
        for (var y = 0; y < source.Height; y++)
        {
            var from = y * source.Width * 4;
            var to = ((y * sheetWidth) + atX) * 4;
            Array.Copy(source.Bgra, from, sheet, to, source.Width * 4);
        }
    }
}
