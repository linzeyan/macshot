namespace Macshot.Windows.Core.Capture;

/// <summary>
/// The size and rate a recording is written as a GIF at.
/// </summary>
/// <remarks>
/// <para>
/// A GIF is not a video format. Every frame is its own LZW-compressed image over a
/// 256-colour palette, with nothing carried between frames, so a full-resolution GIF
/// of a display costs tens of megabytes a second and no viewer thanks you for it.
/// Shrinking the frame and slowing the rate is not a nicety here — it is what makes
/// the format usable at all, which is why it is decided in one place and tested.
/// </para>
/// <para>
/// The trade is deliberately weighted towards size. Anyone who wants the recording
/// to be faithful has MP4; GIF is chosen when the destination only takes a GIF.
/// </para>
/// </remarks>
public sealed record GifRecordingPlan
{
    /// <summary>
    /// Frames a second. Low because each one is a whole image: at 30 the file is two
    /// and a half times the size for motion nobody looking at a GIF expects.
    /// </summary>
    public const int DefaultFrameRate = 12;

    public const int MinFrameRate = 2;

    /// <summary>
    /// macshot's own ceiling — <c>GIFEncoder.swift:29</c> caps whatever it is asked for
    /// at 30, and its export control offers 5 to 30. The ceiling was 24 here for no
    /// reason but caution, which is a different thing from a limit.
    /// </summary>
    public const int MaxFrameRate = 30;

    /// <summary>
    /// The longest edge a GIF frame is allowed. Wide enough to read a window's text,
    /// small enough that a minute of recording is a file that can be attached to
    /// something.
    /// </summary>
    public const int MaximumEdge = 960;

    /// <summary>
    /// Frames past which the recording stops being written. At the default rate this
    /// is about two minutes, far longer than anything that should be a GIF, and it is
    /// the bound that stops a forgotten recording from filling the disk.
    /// </summary>
    public const int MaximumFrames = 1_500;

    private GifRecordingPlan(int width, int height, int frameRate)
    {
        Width = width;
        Height = height;
        FrameRate = frameRate;
    }

    public int Width { get; }

    public int Height { get; }

    public int FrameRate { get; }

    /// <summary>How long one frame is meant to last.</summary>
    public TimeSpan FrameInterval => TimeSpan.FromSeconds(1.0 / FrameRate);

    /// <summary>
    /// Works out the frame size and rate for a source of the given size.
    /// </summary>
    /// <remarks>
    /// A source already smaller than the ceiling is left alone rather than scaled up
    /// to it: enlarging a capture invents pixels and costs file size for both.
    /// </remarks>
    public static GifRecordingPlan Resolve(int sourceWidth, int sourceHeight, int frameRate = DefaultFrameRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);

        var longest = Math.Max(sourceWidth, sourceHeight);
        var scale = longest > MaximumEdge ? (double)MaximumEdge / longest : 1;

        return new GifRecordingPlan(
            Math.Max(1, (int)Math.Round(sourceWidth * scale)),
            Math.Max(1, (int)Math.Round(sourceHeight * scale)),
            Math.Clamp(frameRate, MinFrameRate, MaxFrameRate));
    }
}

/// <summary>
/// Turns the time between two frames into the delay a GIF can store.
/// </summary>
/// <remarks>
/// <para>
/// GIF measures delays in hundredths of a second, as whole numbers. Twelve frames a
/// second is 8.33 of them, so rounding every frame the same way makes the recording
/// play four percent fast, and each frame after the first compounds it. Carrying the
/// remainder into the next frame is what keeps a minute of recording a minute long.
/// </para>
/// <para>
/// This is exactly the sort of arithmetic that stays wrong for the whole life of a
/// feature without anyone being able to point at it, which is why it lives here
/// rather than inside the encoder loop.
/// </para>
/// </remarks>
public sealed class GifFrameTiming
{
    /// <summary>
    /// The shortest delay worth writing. Viewers have treated 0 and 1 as "use 10"
    /// since the browsers of the nineties did, so anything below this plays far
    /// slower than asked rather than faster.
    /// </summary>
    public const int MinimumDelay = 2;

    private const double HundredthsPerSecond = 100;

    private double _carried;

    /// <summary>The delay, in hundredths of a second, for a frame shown this long.</summary>
    public int Next(TimeSpan gap)
    {
        var exact = (gap.TotalSeconds * HundredthsPerSecond) + _carried;
        var delay = (int)Math.Round(exact, MidpointRounding.AwayFromZero);

        if (delay < MinimumDelay)
        {
            // The frame was shorter than a GIF can express. Nothing is carried
            // forward, because the time that could not be stored is not owed to the
            // next frame — it is time the format cannot represent at all.
            _carried = 0;
            return MinimumDelay;
        }

        _carried = exact - delay;
        return delay;
    }
}
