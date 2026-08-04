using System.Buffers.Binary;
using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

/// <summary>
/// Reading and writing the header the retimed audio is exchanged through.
/// </summary>
[TestClass]
public sealed class WavAudioTests
{
    /// <summary>
    /// A header this writes is one this reads. The export writes a WAV and hands it
    /// straight to Windows, so the two halves never meet in a test on Windows — this pair
    /// is the only place the round trip is checked at all.
    /// </summary>
    [TestMethod]
    public void HeaderAndRead_AgreeWithEachOther()
    {
        var file = new byte[WavAudio.HeaderBytes + 400];
        WavAudio.Header(48_000, 2, 16, 400).CopyTo(file, 0);

        var layout = WavAudio.Read(file);

        Assert.IsNotNull(layout);
        Assert.AreEqual(48_000, layout.Value.SampleRate);
        Assert.AreEqual(2, layout.Value.Channels);
        Assert.AreEqual(16, layout.Value.BitsPerSample);
        Assert.AreEqual(4, layout.Value.BytesPerFrame);
        Assert.AreEqual(WavAudio.HeaderBytes, layout.Value.DataOffset);
        Assert.AreEqual(400, layout.Value.DataBytes);
        Assert.AreEqual(100, layout.Value.Frames);
    }

    /// <summary>
    /// The samples are found by walking the chunks, not by assuming byte 44. A Windows
    /// transcoder routinely writes a fact or LIST chunk before the data, and a reader that
    /// skipped straight to 44 would play that chunk's contents as audio — a burst of
    /// noise at the head of every export, from a file that opens perfectly well.
    /// </summary>
    [TestMethod]
    public void Read_WalksPastAChunkSittingBetweenTheFormatAndTheData()
    {
        var file = WithExtraChunk("LIST"u8, 8);
        var layout = WavAudio.Read(file);

        Assert.IsNotNull(layout);
        Assert.AreEqual(WavAudio.HeaderBytes + 16, layout.Value.DataOffset);
        Assert.AreEqual(40, layout.Value.DataBytes);
    }

    /// <summary>
    /// An odd-sized chunk is padded to an even length and the pad is not counted in its
    /// size. Walking by the size alone puts every later chunk one byte out, which is the
    /// same corruption as above and arrives only for the files that happen to have one.
    /// </summary>
    [TestMethod]
    public void Read_AccountsForThePadByteAfterAnOddSizedChunk()
    {
        var file = WithExtraChunk("fact"u8, 5);
        var layout = WavAudio.Read(file);

        Assert.IsNotNull(layout);

        // Eight bytes of chunk header, five of body, one of pad.
        Assert.AreEqual(WavAudio.HeaderBytes + 14, layout.Value.DataOffset);
        Assert.AreEqual(40, layout.Value.DataBytes);
    }

    /// <summary>
    /// A data chunk claiming more than the file holds is trusted only as far as the file
    /// goes. A write cut short by a full disk keeps the length it intended, and a reader
    /// that believed it would run off the end of the buffer during an export.
    /// </summary>
    [TestMethod]
    public void Read_TrustsTheDataLengthOnlyAsFarAsTheFileActuallyGoes()
    {
        var file = new byte[WavAudio.HeaderBytes + 40];
        WavAudio.Header(48_000, 2, 16, 4_000).CopyTo(file, 0);

        var layout = WavAudio.Read(file);

        Assert.IsNotNull(layout);
        Assert.AreEqual(40, layout.Value.DataBytes);
    }

    /// <summary>
    /// A data length that is not a whole number of frames is cut back to one. Half a frame
    /// is not a frame, and a reader that kept it would put the channels out of step from
    /// that point to the end — audible as the stereo image collapsing partway through.
    /// </summary>
    [TestMethod]
    public void Read_CutsAPartialFrameOffTheEnd()
    {
        var file = new byte[WavAudio.HeaderBytes + 42];
        WavAudio.Header(48_000, 2, 16, 42).CopyTo(file, 0);

        var layout = WavAudio.Read(file);

        Assert.IsNotNull(layout);
        Assert.AreEqual(40, layout.Value.DataBytes);
        Assert.AreEqual(10, layout.Value.Frames);
    }

    /// <summary>
    /// The extensible format is accepted. Windows writes it for anything above two
    /// channels and sometimes for two, the samples after it are ordinary PCM, and
    /// refusing it would reject files this port had just asked Windows to produce.
    /// </summary>
    [TestMethod]
    public void Read_AcceptsTheExtensibleFormatWindowsWritesForItself()
    {
        var file = new byte[WavAudio.HeaderBytes + 40];
        WavAudio.Header(48_000, 2, 16, 40).CopyTo(file, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(20, 2), 0xFFFE);

        Assert.IsNotNull(WavAudio.Read(file));
    }

    /// <summary>
    /// A compressed WAV is refused rather than read as samples. The export asks Windows
    /// for PCM; a file that came back as anything else would be decoded here as though it
    /// were raw, and the result is noise rather than a failure anyone could diagnose.
    /// </summary>
    [TestMethod]
    public void Read_RefusesAFileWhoseSamplesAreNotPcm()
    {
        var file = new byte[WavAudio.HeaderBytes + 40];
        WavAudio.Header(48_000, 2, 16, 40).CopyTo(file, 0);

        // 0x0011 is IMA ADPCM.
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(20, 2), 0x0011);

        Assert.IsNull(WavAudio.Read(file));
    }

    /// <summary>
    /// Something that is not a WAV at all is refused rather than misread. The export
    /// hands this whatever the transcoder produced, and on a machine with no PCM encoder
    /// that can be an empty file or an error page.
    /// </summary>
    [TestMethod]
    public void Read_RefusesWhatIsNotAWavFile()
    {
        Assert.IsNull(WavAudio.Read([]));
        Assert.IsNull(WavAudio.Read("not a wav file at all, not even close"u8));
    }

    /// <summary>A canonical header with one extra chunk wedged before the data.</summary>
    private static byte[] WithExtraChunk(ReadOnlySpan<byte> id, int bodyBytes)
    {
        var padded = bodyBytes + (bodyBytes % 2);
        var file = new byte[WavAudio.HeaderBytes + 8 + padded + 40];
        var header = WavAudio.Header(48_000, 2, 16, 40);

        // Everything up to and including the fmt chunk, which is the first 36 bytes.
        header.AsSpan(0, 36).CopyTo(file);

        id.CopyTo(file.AsSpan(36));
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(40, 4), (uint)bodyBytes);

        // Then the data chunk header the canonical layout would have had at 36.
        header.AsSpan(36, 8).CopyTo(file.AsSpan(44 + padded));

        return file;
    }
}
