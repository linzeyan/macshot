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

    /// <summary>
    /// macshot offers 15, 24, 30, 60 and 120 — <c>SettingsWindowController.swift:1551</c>.
    /// 120 is for recording a UI animation, which is the case the setting exists for.
    /// </summary>
    public const int MaxFrameRate = 120;

    public const uint MinBitrate = 10_000_000;
    public const uint MaxBitrate = 80_000_000;

    /// <summary>The five rates macshot's menu offers, lowest first.</summary>
    public static IReadOnlyList<int> OfferedFrameRates { get; } = [15, 24, 30, 60, 120];

    /// <summary>
    /// What a frame-rate menu should hold given what the settings file currently says:
    /// macshot's five, plus <paramref name="current"/> if it is not one of them.
    /// </summary>
    /// <remarks>
    /// The file takes any rate between <see cref="MinFrameRate"/> and
    /// <see cref="MaxFrameRate"/> and is meant to be edited by hand. A menu holding only
    /// the five would have nothing to select for a file that says 45, land on the first
    /// entry, and write 15 back — a settings window that changes settings by being
    /// opened.
    /// </remarks>
    public static IReadOnlyList<int> FrameRateChoices(int current)
    {
        var rate = Math.Clamp(current, MinFrameRate, MaxFrameRate);
        return OfferedFrameRates.Contains(rate)
            ? OfferedFrameRates
            : [.. OfferedFrameRates.Append(rate).Order()];
    }

    /// <summary>
    /// Bits spent per pixel per frame, and the number this file used to get wrong.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It was 0.1 here on the reasoning that screen content is flat colour and sharp
    /// edges rather than film grain, so it compresses better than camera video. The
    /// first half of that is true and the conclusion does not follow: H.264's
    /// psy-tuned DCT softens exactly those high-contrast edges below roughly 0.30, so
    /// "low entropy UI" stops being true the moment anything scrolls. macshot says so
    /// in as many words — <c>VideoEncodingSettings.swift:8–15</c> — and records at
    /// 0.40, which is what this is now.
    /// </para>
    /// <para>
    /// macshot has three tiers and uses <c>.high</c> for every screen recording
    /// (<c>RecordingEngine.swift:461</c>); the other two are for exporting from its
    /// video editor, which this port does not have. So there is one tier here rather
    /// than a setting nobody would find, and it is the one macshot actually records at.
    /// </para>
    /// </remarks>
    private const double BitsPerPixelPerFrame = 0.40;

    /// <summary>
    /// Where the taper starts, and what it costs. A 4K capture at 0.40 asks for more
    /// than anyone wants to keep, and the extra bits buy least where there are most
    /// pixels to hide them in — macshot's own bands, <c>VideoEncodingSettings.swift:122–129</c>.
    /// </summary>
    private const long FullHdPixels = 1920L * 1080;

    private const long UltraHdPixels = 3840L * 2160;

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

        var pixels = (long)width * height;
        var bitrate = pixels * rate * BitsPerPixelPerFrame * Taper(pixels);

        return new RecordingPlan(
            width,
            height,
            rate,
            (uint)Math.Clamp(bitrate, MinBitrate, MaxBitrate));
    }

    /// <summary>
    /// What the bitrate is multiplied by at this many pixels. Stepped rather than
    /// continuous because macshot's is, and a recording made on one machine should
    /// not be a different size from the same recording made on the other.
    /// </summary>
    private static double Taper(long pixels) => pixels switch
    {
        > UltraHdPixels => 0.80,
        > FullHdPixels => 0.92,
        _ => 1.0,
    };

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
