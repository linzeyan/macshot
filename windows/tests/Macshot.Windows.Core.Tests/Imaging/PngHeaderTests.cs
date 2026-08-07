using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Tests.Imaging;

[TestClass]
public sealed class PngHeaderTests
{
    private static byte[] Head(int width, int height, bool signed = true, string chunk = "IHDR")
    {
        var head = new byte[PngHeader.Length];
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (signed)
        {
            signature.CopyTo(head);
        }

        // The IHDR chunk: four bytes of length, then the name, then the two dimensions.
        head[11] = 13;
        System.Text.Encoding.ASCII.GetBytes(chunk).CopyTo(head, 12);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(head.AsSpan(16, 4), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(head.AsSpan(20, 4), height);
        return head;
    }

    /// <summary>
    /// This is where the notification area's menu learns how big a past capture was. PNG
    /// writes its dimensions big-endian, and reading them the other way round would name
    /// an 800-pixel capture 537 million.
    /// </summary>
    [TestMethod]
    public void TryReadSize_ReadsTheDimensionsInTheByteOrderPngWritesThem()
    {
        Assert.IsTrue(PngHeader.TryReadSize(Head(2038, 1588), out var width, out var height));
        Assert.AreEqual(2038, width);
        Assert.AreEqual(1588, height);
    }

    /// <summary>
    /// The history folder is read by name, so anything dropped into it is offered to this.
    /// Without both checks a file that merely ends in .png is reported at whatever two
    /// integers happen to sit at those offsets, and the menu claims a capture billions of
    /// pixels wide.
    /// </summary>
    [TestMethod]
    public void TryReadSize_RefusesBytesThatAreNotTheStartOfAPng()
    {
        Assert.IsFalse(PngHeader.TryReadSize(Head(800, 600, signed: false), out _, out _));
        Assert.IsFalse(PngHeader.TryReadSize(Head(800, 600, chunk: "IDAT"), out _, out _));
        Assert.IsFalse(PngHeader.TryReadSize(Head(0, 600), out _, out _));
        Assert.IsFalse(PngHeader.TryReadSize(new byte[PngHeader.Length - 1], out _, out _));
    }
}
