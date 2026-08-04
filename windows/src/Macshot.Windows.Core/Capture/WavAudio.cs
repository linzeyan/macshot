using System.Buffers.Binary;

namespace Macshot.Windows.Core.Capture;

/// <summary>What a WAV file says about itself, and where its samples start.</summary>
/// <param name="SampleRate">Frames per second.</param>
/// <param name="Channels">Samples per frame.</param>
/// <param name="BitsPerSample">Bits in one sample of one channel.</param>
/// <param name="DataOffset">Byte offset of the first sample in the file.</param>
/// <param name="DataBytes">How many bytes of samples follow it.</param>
public readonly record struct WavLayout(
    int SampleRate,
    int Channels,
    int BitsPerSample,
    long DataOffset,
    long DataBytes)
{
    /// <summary>Bytes in one frame — one sample of every channel.</summary>
    public int BytesPerFrame => Channels * (BitsPerSample / 8);

    /// <summary>How many frames the file holds.</summary>
    public long Frames => BytesPerFrame > 0 ? DataBytes / BytesPerFrame : 0;
}

/// <summary>
/// Reads and writes the header of a PCM WAV file.
/// </summary>
/// <remarks>
/// <para>
/// Here rather than in the Windows half because it is byte arithmetic and nothing else,
/// and because getting it wrong is silent: a header off by one chunk produces a file that
/// plays as a burst of noise rather than one that fails to open. That is exactly the class
/// of bug a test on this machine catches and a screenshot of the VM does not.
/// </para>
/// <para>
/// WAV is the exchange format for the retimed audio in <see cref="AudioRetime"/> for one
/// reason: it is the only encoding Windows will both write from a
/// <c>MediaTranscoder</c> and read back as raw samples, so it is where an audio track has
/// to be turned into numbers before it can be re-timed and turned back.
/// </para>
/// </remarks>
public static class WavAudio
{
    /// <summary>The size of the header <see cref="Header"/> writes.</summary>
    /// <remarks>
    /// Canonical WAV: the RIFF descriptor, one fmt chunk of the minimum sixteen bytes,
    /// and the data chunk's own eight. Nothing this port writes needs more, though
    /// <see cref="Read"/> accepts files that have more because a transcoder often emits
    /// a fact or LIST chunk beside them.
    /// </remarks>
    public const int HeaderBytes = 44;

    /// <summary>Uncompressed integer samples. The only format this reads.</summary>
    private const int FormatPcm = 1;

    /// <summary>
    /// The same, wrapped so the file can name its channel layout.
    /// </summary>
    /// <remarks>
    /// Accepted because a Windows transcoder writes it for anything above two channels
    /// and sometimes for two: the first sixteen bytes of the fmt chunk are identical, and
    /// the samples that follow are ordinary PCM. Refusing it would reject files this port
    /// itself had just asked Windows to produce.
    /// </remarks>
    private const int FormatExtensible = 0xFFFE;

    /// <summary>
    /// Reads what <paramref name="file"/> says about itself, or nothing when it is not a
    /// PCM WAV this can use.
    /// </summary>
    /// <remarks>
    /// The chunks are walked rather than assumed to be at fixed offsets. "fmt " and
    /// "data" are only required to appear somewhere after the RIFF descriptor, and a
    /// transcoder routinely puts a fact or LIST chunk between them — a reader that
    /// assumed the samples began at byte 44 would treat that chunk's contents as audio.
    /// </remarks>
    public static WavLayout? Read(ReadOnlySpan<byte> file)
    {
        // The RIFF descriptor is twelve bytes and every chunk header is eight, so
        // anything shorter than one of each cannot describe samples at all.
        if (file.Length < 12 + 8
            || !file[..4].SequenceEqual("RIFF"u8)
            || !file.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            return null;
        }

        var format = 0;
        var channels = 0;
        var sampleRate = 0;
        var bits = 0;
        var at = 12;

        while (at + 8 <= file.Length)
        {
            var id = file.Slice(at, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(at + 4, 4));
            var body = at + 8;

            if (id.SequenceEqual("fmt "u8) && size >= 16 && body + 16 <= file.Length)
            {
                format = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(body, 2));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(body + 2, 2));
                sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(body + 4, 4));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(body + 14, 2));
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (format is not (FormatPcm or FormatExtensible)
                    || channels <= 0
                    || sampleRate <= 0
                    || bits <= 0
                    || bits % 8 != 0)
                {
                    return null;
                }

                // Trusted only as far as the file actually goes. A recording whose write
                // was cut short keeps the length it intended in its header, and reading
                // to it would run off the end of the buffer.
                var available = file.Length - body;
                var bytes = Math.Min((long)size, available);
                var frame = channels * (bits / 8);

                return new WavLayout(
                    sampleRate,
                    channels,
                    bits,

                    // Truncated to whole frames: half a frame of samples is not one, and
                    // a reader that kept it would put the channels out of step from there
                    // to the end of the file.
                    body,
                    bytes - (bytes % frame));
            }

            // Chunks are padded to an even length, and the pad byte is not counted in the
            // size. Walking by the size alone puts every later chunk one byte out.
            at = body + (int)size + ((int)size % 2);
        }

        return null;
    }

    /// <summary>
    /// Writes a canonical <see cref="HeaderBytes"/>-byte header for
    /// <paramref name="dataBytes"/> of samples.
    /// </summary>
    public static byte[] Header(int sampleRate, int channels, int bitsPerSample, long dataBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bitsPerSample);
        ArgumentOutOfRangeException.ThrowIfNegative(dataBytes);

        var header = new byte[HeaderBytes];
        var span = header.AsSpan();
        var block = channels * (bitsPerSample / 8);

        "RIFF"u8.CopyTo(span);

        // Everything after this field, which is the file less the eight bytes of "RIFF"
        // and the size itself.
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4, 4), (uint)(dataBytes + HeaderBytes - 8));
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(16, 4), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(20, 2), FormatPcm);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(22, 2), (ushort)channels);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(24, 4), (uint)sampleRate);

        // Bytes per second, which a player uses to size its own buffers. Derived rather
        // than passed in, because a byte rate that disagreed with the other three fields
        // is the kind of thing only some players notice.
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(28, 4), (uint)(sampleRate * block));
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(32, 2), (ushort)block);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(34, 2), (ushort)bitsPerSample);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(40, 4), (uint)dataBytes);

        return header;
    }
}
