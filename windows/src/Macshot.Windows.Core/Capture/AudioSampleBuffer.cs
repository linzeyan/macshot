namespace Macshot.Windows.Core.Capture;

/// <summary>
/// Holds what one source of sound has produced, and hands it out in samples of a fixed
/// size — filling the rest with silence when there is not enough.
/// </summary>
/// <remarks>
/// <para>
/// The silence is the point. A Windows loopback endpoint delivers nothing at all while
/// the machine is quiet rather than delivering zeroes, so a track built from what
/// arrived would be short by exactly the length of every silent passage, and the sound
/// would slide earlier and earlier against the picture. The recorder asks for one sample
/// every twenty milliseconds whatever happened, and a source with nothing to say fills
/// its sample with zeroes.
/// </para>
/// <para>
/// The opposite case is a source that produces faster than the track is drained — a
/// stalled encoder, a machine under load. Buffering it all would trade memory for a
/// backlog that plays back as sound running behind, so past the cap the oldest is
/// dropped: a recording that skips a moment is better than one that never catches up.
/// </para>
/// </remarks>
public sealed class AudioSampleBuffer
{
    private readonly Queue<short[]> _pending = new();
    private readonly int _capacity;

    private int _held;
    private int _offset;

    /// <param name="capacity">
    /// How many samples' worth may wait. Two seconds by default: long enough to ride out
    /// a stall, short enough that what is dropped is a moment rather than a passage.
    /// </param>
    public AudioSampleBuffer(int capacity = AudioPlan.SampleRate * AudioPlan.Channels * 2)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
    }

    /// <summary>How many samples are waiting to be taken.</summary>
    public int Pending => _held;

    /// <summary>How many samples have been dropped for want of room.</summary>
    public int Dropped { get; private set; }

    /// <summary>Takes a copy of what a source produced.</summary>
    public void Append(ReadOnlySpan<short> samples)
    {
        if (samples.IsEmpty)
        {
            return;
        }

        lock (_pending)
        {
            _pending.Enqueue(samples.ToArray());
            _held += samples.Length;

            while (_held > _capacity && _pending.Count > 1)
            {
                var oldest = _pending.Dequeue();
                _held -= oldest.Length - _offset;
                Dropped += oldest.Length - _offset;
                _offset = 0;
            }
        }
    }

    /// <summary>
    /// Fills <paramref name="into"/>, with silence wherever the source had nothing.
    /// Returns how much of it was real sound.
    /// </summary>
    public int Take(Span<short> into)
    {
        var written = 0;

        lock (_pending)
        {
            while (written < into.Length && _pending.Count > 0)
            {
                var head = _pending.Peek();
                var available = head.Length - _offset;
                var wanted = Math.Min(available, into.Length - written);

                head.AsSpan(_offset, wanted).CopyTo(into[written..]);
                written += wanted;
                _offset += wanted;
                _held -= wanted;

                if (_offset == head.Length)
                {
                    _pending.Dequeue();
                    _offset = 0;
                }
            }
        }

        into[written..].Clear();
        return written;
    }
}
