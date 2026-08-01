namespace Macshot.Windows.Core.Capture;

/// <summary>How hard the encoder is asked to work on what it is given.</summary>
/// <remarks>
/// macshot's <c>VideoQuality</c>, with its three tiers and its numbers. Recording always
/// uses <see cref="High"/> in both products; these exist for the one place a user can
/// choose — exporting from the video editor.
/// </remarks>
public enum VideoQuality
{
    Low,
    Medium,
    High,
}

/// <summary>
/// What an export from the video editor produces, before anything is encoded.
/// </summary>
/// <remarks>
/// <para>
/// The arithmetic behind the video editor's bottom bar: the sizes the dimensions menu
/// offers, the bitrate each quality tier asks for, and the estimate shown beside them.
/// All of it is macshot's own, taken from <c>VideoEncodingSettings.swift</c> and the
/// editor's <c>drawButtons</c>, so a recording exported on either machine comes out the
/// same size.
/// </para>
/// <para>
/// In Core because it is arithmetic with edges — a 33% scale that rounds to an odd width
/// the encoder refuses, a 4K capture whose bitrate would otherwise run away — and none
/// of it needs a video to check.
/// </para>
/// </remarks>
public static class VideoExportPlan
{
    /// <summary>
    /// The narrowest export macshot offers. Below this the picture is no longer a
    /// screenshot of anything readable, so the menu leaves the percentage out rather than
    /// offering a size nobody would choose twice.
    /// </summary>
    public const int MinimumWidth = 128;

    /// <summary>The percentages the dimensions menu offers, in macshot's order.</summary>
    public static IReadOnlyList<int> ScalePercentages { get; } = [100, 75, 50, 33, 25];

    public const int MinGifFrameRate = 5;

    public const int MaxGifFrameRate = 30;

    /// <summary>macshot's default for a GIF exported from the editor.</summary>
    public const int DefaultGifFrameRate = 15;

    /// <summary>
    /// The size a percentage produces, rounded down to even in both directions.
    /// </summary>
    /// <remarks>
    /// Even, because H.264's chroma planes are half resolution: an odd width is either
    /// refused by the encoder or silently padded, and the padding shows as a green line
    /// down one edge.
    /// </remarks>
    public static (int Width, int Height) Scaled(int width, int height, int percent)
    {
        var scaledWidth = width * percent / 100;
        var scaledHeight = height * percent / 100;
        return (scaledWidth / 2 * 2, scaledHeight / 2 * 2);
    }

    /// <summary>
    /// The percentages worth offering for a video this size — macshot's list, less any
    /// that would take it below <see cref="MinimumWidth"/>.
    /// </summary>
    public static IReadOnlyList<int> ScaleChoices(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return [100];
        }

        var choices = new List<int>(ScalePercentages.Count);
        foreach (var percent in ScalePercentages)
        {
            // Measured before rounding, as macshot measures it: the guard is about whether
            // the percentage is worth offering, not about the even-numbered result.
            if (percent == 100 || width * percent / 100 >= MinimumWidth)
            {
                choices.Add(percent);
            }
        }

        return choices;
    }

    /// <summary>How the dimensions button reads at a given percentage.</summary>
    public static string DimensionsLabel(int width, int height, int percent)
    {
        if (percent >= 100)
        {
            return $"{width} × {height}";
        }

        var (scaledWidth, scaledHeight) = Scaled(width, height, percent);
        return $"{scaledWidth} × {scaledHeight} ({percent}%)";
    }

    /// <summary>Bits per pixel per frame, which is what each tier actually asks for.</summary>
    /// <remarks>
    /// A ratio rather than a bitrate because screen content with sharp text needs
    /// camera-equivalent bitrates: H.264 softens high-contrast edges below about 0.30,
    /// and the "it is only UI, it will compress" assumption fails the moment anything
    /// scrolls.
    /// </remarks>
    public static double BitsPerPixelPerFrame(VideoQuality quality) => quality switch
    {
        VideoQuality.Low => 0.12,
        VideoQuality.Medium => 0.22,
        _ => 0.40,
    };

    public static int MinBitrate(VideoQuality quality) => quality switch
    {
        VideoQuality.Low => 1_000_000,
        VideoQuality.Medium => 4_000_000,
        _ => 10_000_000,
    };

    public static int MaxBitrate(VideoQuality quality) => quality switch
    {
        VideoQuality.Low => 12_000_000,
        VideoQuality.Medium => 30_000_000,
        _ => 80_000_000,
    };

    /// <summary>
    /// The bitrate to ask the encoder for, in bits a second.
    /// </summary>
    /// <remarks>
    /// H.264 only. macshot defines an HEVC multiplier and never reaches it — both of its
    /// real call sites pass H.264 — so a codec argument here would be a choice this port
    /// does not offer either.
    /// </remarks>
    public static int Bitrate(int width, int height, int frameRate, VideoQuality quality)
    {
        if (width <= 0 || height <= 0 || frameRate <= 0)
        {
            return MinBitrate(quality);
        }

        double pixels = (double)width * height;
        var raw = pixels * frameRate * BitsPerPixelPerFrame(quality) * Taper(pixels);

        return (int)Math.Clamp(raw, MinBitrate(quality), MaxBitrate(quality));
    }

    /// <summary>
    /// What a 4K or larger frame is scaled back by, to stop a high-DPI capture producing
    /// a file nobody can send anywhere.
    /// </summary>
    private static double Taper(double pixels) => pixels switch
    {
        > 3840d * 2160d => 0.80,
        > 1920d * 1080d => 0.92,
        _ => 1.0,
    };

    /// <summary>
    /// Roughly how large the export will be, for the reading beside the buttons.
    /// </summary>
    /// <remarks>
    /// <para>
    /// macshot's own estimate, and deliberately a crude one: the source file's size
    /// scaled by how much of it is kept, by the area being kept, and by what the quality
    /// tier does to the bitrate. Pixels scale quadratically, which is why halving the
    /// dimensions quarters the guess.
    /// </para>
    /// <para>
    /// GIF is a different multiplier entirely — three times the source, tapered by how
    /// much the frame rate is being cut — because every GIF frame is a whole image and
    /// nothing about the source's H.264 size predicts it. It is there to say "much
    /// larger" rather than to be right.
    /// </para>
    /// </remarks>
    public static long EstimatedBytes(
        long sourceBytes,
        double trimmedSeconds,
        double totalSeconds,
        int percent,
        VideoQuality quality,
        int sourceFrameRate,
        int gifFrameRate,
        bool asGif)
    {
        if (sourceBytes <= 0)
        {
            return 0;
        }

        var kept = totalSeconds > 0 ? Math.Clamp(trimmedSeconds / totalSeconds, 0, 1) : 1;
        var scale = percent / 100.0;
        var area = scale * scale;

        if (asGif)
        {
            var rateRatio = sourceFrameRate > 0
                ? Math.Min(gifFrameRate, sourceFrameRate) / (double)Math.Max(sourceFrameRate, 1)
                : 1;

            return (long)(sourceBytes * kept * area * 3.0 * rateRatio);
        }

        return (long)(sourceBytes * kept * area * QualityRatio(quality));
    }

    /// <summary>
    /// What each tier does to the output size relative to High, as macshot's estimate
    /// weighs it. Not the bitrate ratio: this is the one the reading uses.
    /// </summary>
    public static double QualityRatio(VideoQuality quality) => quality switch
    {
        VideoQuality.Low => 0.33,
        VideoQuality.Medium => 0.62,
        _ => 1.0,
    };

    /// <summary>Whether the estimate is worth showing at all.</summary>
    /// <remarks>
    /// macshot shows it only once something would change the file: an untouched export is
    /// the size it already is, and a reading saying so is noise.
    /// </remarks>
    public static bool ShowsEstimate(
        double trimmedSeconds,
        double totalSeconds,
        int percent,
        VideoQuality quality,
        bool asGif)
    {
        if (asGif)
        {
            return true;
        }

        var kept = totalSeconds > 0 ? trimmedSeconds / totalSeconds : 1;
        var area = percent / 100.0 * (percent / 100.0);

        return kept < 0.99 || area < 0.99 || QualityRatio(quality) < 0.99;
    }
}
