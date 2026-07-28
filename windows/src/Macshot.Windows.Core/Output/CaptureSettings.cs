using Macshot.Windows.Core.Annotations;

namespace Macshot.Windows.Core.Output;

/// <summary>
/// Everything the user can change about how a finished capture is delivered.
/// </summary>
/// <remarks>
/// This is the Windows counterpart of the macOS <c>UserDefaults</c> keys, gathered
/// into one value instead of scattered loose keys, so the delivery path takes a
/// single argument and the whole thing round-trips through one JSON file. It lives
/// in Core with no persistence of its own: reading and writing the file is the
/// app's job, validating the values is this type's.
/// </remarks>
public sealed record CaptureSettings
{
    public const int MinQuality = 1;
    public const int MaxQuality = 100;
    public const int MinThumbnailSeconds = 1;
    public const int MaxThumbnailSeconds = 60;
    public const double MinStrokeWidth = 1;
    public const double MaxStrokeWidth = 64;

    public static CaptureSettings Default { get; } = new();

    public CaptureImageFormat Format { get; init; } = CaptureImageFormat.Png;

    /// <summary>Encoder quality for lossy formats, 1–100. Ignored for PNG.</summary>
    public int Quality { get; init; } = 90;

    /// <summary>
    /// Where captures are written. Null means "wherever the app decides", which is
    /// Pictures\Macshot; storing null rather than that resolved path keeps the file
    /// portable between machines whose Pictures folder is redirected differently.
    /// </summary>
    public string? SaveDirectory { get; init; }

    public string FilenameTemplate { get; init; } = Output.FilenameTemplate.Default;

    public bool CopyToClipboard { get; init; } = true;

    /// <summary>Writes the capture to <see cref="SaveDirectory"/> without asking.</summary>
    public bool AutoSave { get; init; } = true;

    public bool ShowThumbnail { get; init; } = true;

    /// <summary>
    /// What a screen recording is written as. MP4 by default because it is what a
    /// recording should be; GIF costs size and colour, and is chosen for where the
    /// file has to go rather than for what it is.
    /// </summary>
    public RecordingFormat RecordingFormat { get; init; } = RecordingFormat.Mp4;

    public int ThumbnailSeconds { get; init; } = 6;

    /// <summary>
    /// The drawing style the toolbar was last left on, as <c>#AARRGGBB</c>. This is
    /// remembered rather than configured — nobody opens a settings window to pick
    /// the colour of the next arrow — which is why it has no preferences UI. It is
    /// the counterpart of the macOS <c>currentStrokeWidth</c> family of defaults.
    /// </summary>
    public string AnnotationColor { get; init; } = AnnotationStyle.Default.Color.ToHex();

    public double AnnotationStrokeWidth { get; init; } = AnnotationStyle.Default.StrokeWidth;

    public LineStyle AnnotationLineStyle { get; init; } = LineStyle.Solid;

    public AnnotationStyle ToAnnotationStyle()
    {
        var color = Annotations.AnnotationColor.TryParseHex(AnnotationColor, out var parsed)
            ? parsed
            : AnnotationStyle.Default.Color;
        return new AnnotationStyle(color, Math.Clamp(AnnotationStrokeWidth, MinStrokeWidth, MaxStrokeWidth), AnnotationLineStyle);
    }

    public CaptureSettings WithAnnotationStyle(AnnotationStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);

        return this with
        {
            AnnotationColor = style.Color.ToHex(),
            AnnotationStrokeWidth = style.StrokeWidth,
            AnnotationLineStyle = style.LineStyle,
        };
    }

    /// <summary>
    /// Clamps every field into range. The settings file is user-editable and can
    /// also be stale after an upgrade, so nothing downstream may assume it is sane;
    /// this is the one place that repairs it.
    /// </summary>
    public CaptureSettings Normalized()
    {
        return this with
        {
            Format = Enum.IsDefined(Format) ? Format : CaptureImageFormat.Png,
            RecordingFormat = Enum.IsDefined(RecordingFormat) ? RecordingFormat : RecordingFormat.Mp4,
            Quality = Math.Clamp(Quality, MinQuality, MaxQuality),
            SaveDirectory = string.IsNullOrWhiteSpace(SaveDirectory) ? null : SaveDirectory.Trim(),
            FilenameTemplate = string.IsNullOrWhiteSpace(FilenameTemplate)
                ? Output.FilenameTemplate.Default
                : FilenameTemplate.Trim(),
            ThumbnailSeconds = Math.Clamp(ThumbnailSeconds, MinThumbnailSeconds, MaxThumbnailSeconds),

            // Round-tripped through the parser so an unreadable colour becomes the
            // default here, rather than silently at every point that draws.
            AnnotationColor = (Annotations.AnnotationColor.TryParseHex(AnnotationColor, out var color)
                ? color
                : AnnotationStyle.Default.Color).ToHex(),
            AnnotationStrokeWidth = double.IsFinite(AnnotationStrokeWidth)
                ? Math.Clamp(AnnotationStrokeWidth, MinStrokeWidth, MaxStrokeWidth)
                : AnnotationStyle.Default.StrokeWidth,
            AnnotationLineStyle = Enum.IsDefined(AnnotationLineStyle) ? AnnotationLineStyle : LineStyle.Solid,
        };
    }
}
