namespace Macshot.Windows.Core.Capture;

/// <summary>
/// The size, rate, and bitrate one recording is encoded at.
/// </summary>
/// <remarks>
/// <para>
/// Separated from the recorder because it is arithmetic with consequences nobody
/// sees until a file will not play: an odd width the encoder rejects outright, or a
/// bitrate low enough to turn text to mush on a display nobody tested on. None of
/// that needs a compositor to exercise.
/// </para>
/// <para>
/// The plan describes the <em>output</em>. What the compositor hands over is whatever
/// size the display or window happens to be, and the transcoder scales that into
/// these dimensions, so the source is deliberately not made to fit.
/// </para>
/// </remarks>
public sealed record RecordingPlan
{
    /// <summary>
    /// What a screen recording is taken at unless something asks otherwise. Enough
    /// for a pointer crossing a demo to look continuous, and not so much that an
    /// encoder spends a core keeping up with a desktop that mostly sits still.
    /// </summary>
    public const int DefaultFrameRate = 30;

    public const int MinFrameRate = 5;
    public const int MaxFrameRate = 60;

    public const uint MinBitrate = 1_000_000;
    public const uint MaxBitrate = 40_000_000;

    /// <summary>
    /// Bits spent per pixel per frame. Screen content is flat colour and sharp edges
    /// rather than film grain, so it compresses far better than camera video of the
    /// same size; this is the rate at which text stays readable without the file
    /// growing faster than anyone wants to keep it.
    /// </summary>
    private const double BitsPerPixel = 0.1;

    private RecordingPlan(int width, int height, int frameRate, uint bitrate)
    {
        Width = width;
        Height = height;
        FrameRate = frameRate;
        Bitrate = bitrate;
    }

    public int Width { get; }

    public int Height { get; }

    public int FrameRate { get; }

    /// <summary>Target video bitrate, in bits per second.</summary>
    public uint Bitrate { get; }

    /// <summary>How long one frame is meant to last.</summary>
    public TimeSpan FrameInterval => TimeSpan.FromSeconds(1.0 / FrameRate);

    /// <summary>
    /// Works out how to encode a source of the given size.
    /// </summary>
    /// <remarks>
    /// Both dimensions come back even because H.264 stores colour at half resolution
    /// in each direction, so an odd one has no whole chroma sample to go in and the
    /// encoder refuses the profile. Rounding down rather than up keeps the output
    /// inside what was captured; the half pixel that costs is not visible, while a
    /// recording that never starts is.
    /// </remarks>
    public static RecordingPlan Resolve(int sourceWidth, int sourceHeight, int frameRate = DefaultFrameRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);

        var width = ToEven(sourceWidth);
        var height = ToEven(sourceHeight);
        var rate = Math.Clamp(frameRate, MinFrameRate, MaxFrameRate);
        var bitrate = (double)width * height * rate * BitsPerPixel;

        return new RecordingPlan(
            width,
            height,
            rate,
            (uint)Math.Clamp(bitrate, MinBitrate, MaxBitrate));
    }

    /// <summary>
    /// The next even number at or below <paramref name="value"/>, but never zero: a
    /// window one pixel wide is absurd rather than impossible, and it still has to
    /// produce a profile the encoder will accept.
    /// </summary>
    private static int ToEven(int value) => Math.Max(2, value - (value % 2));
}

/// <summary>
/// Decides which arriving frames are kept, so the encoder is fed at the rate the
/// recording claims to run at.
/// </summary>
/// <remarks>
/// <para>
/// The compositor delivers a frame whenever the content changes, which on a 144 Hz
/// display is up to 144 of them a second. Handing all of those to an encoder that
/// was told it is making 30 fps video costs the time and the file size of a 144 fps
/// recording and looks no better.
/// </para>
/// <para>
/// Timestamps stay on the real clock rather than being renumbered onto the grid, so
/// a machine that stalls mid-recording produces a video that pauses — which is what
/// happened — instead of one that silently speeds up.
/// </para>
/// </remarks>
public sealed class FrameCadence
{
    private readonly TimeSpan _interval;
    private TimeSpan _next;

    public FrameCadence(TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        _interval = interval;
    }

    /// <summary>How many frames have been turned away.</summary>
    public int Dropped { get; private set; }

    /// <summary>Whether a frame that arrived at <paramref name="elapsed"/> is wanted.</summary>
    public bool ShouldKeep(TimeSpan elapsed)
    {
        if (elapsed < _next)
        {
            Dropped++;
            return false;
        }

        // Advanced past the arrival rather than by one interval. After a stall the
        // grid is otherwise left in the past, and every frame of the burst that
        // follows would be let through until it had caught up.
        do
        {
            _next += _interval;
        }
        while (_next <= elapsed);

        return true;
    }
}
