using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Annotations;

/// <summary>
/// Writes annotations out as JSON and reads them back, so a capture can be reopened
/// with its marks still separate from the pixels.
/// </summary>
/// <remarks>
/// <para>
/// This is what the history needs to be re-editable. Without it an archived capture is
/// a flat image: reopening it shows the arrow, but the arrow cannot be moved, restyled
/// or taken off, which is exactly what someone reopens a capture to do.
/// </para>
/// <para>
/// Sprites are stored as their pixels rather than as the text and font that produced
/// them. It is the larger of the two files by far, and it is the only one that is
/// certainly right: a sprite is rasterized by DirectWrite with whatever fonts the
/// machine had at the time, and re-rasterizing it later on a machine missing one of
/// them would silently change what the annotation says. See
/// <c>docs/windows-port/architecture.md</c>, decision D7. The pixels are deflated
/// before they are encoded, because a glyph sprite is mostly transparent and
/// compresses to a fraction of its size.
/// </para>
/// <para>
/// Nothing here throws on the way in. The file sits in a folder the user can edit,
/// delete from and copy into, so every way it can be wrong has to end as "no
/// annotations" rather than as an exception over a capture they only wanted to look at.
/// </para>
/// </remarks>
public static class AnnotationFile
{
    /// <summary>
    /// What the reader expects. Bumped only for a change a reader of the old shape
    /// would get wrong; a new optional field is not one, because a missing value reads
    /// back as its default.
    /// </summary>
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The annotations as a document, ready to be written beside the capture.</summary>
    public static string Write(IEnumerable<Annotation> annotations)
    {
        ArgumentNullException.ThrowIfNull(annotations);

        var stored = new StoredDocument(
            CurrentVersion,
            [.. annotations.Select(ToStored)]);

        return JsonSerializer.Serialize(stored, SerializerOptions);
    }

    /// <summary>
    /// The annotations a document holds, or none at all when it holds nothing this
    /// version understands.
    /// </summary>
    /// <remarks>
    /// One unreadable annotation costs only itself: the rest of the document is still
    /// the user's work, and dropping all of it because one mark was written by a newer
    /// version would be the worse of the two failures.
    /// </remarks>
    public static IReadOnlyList<Annotation> Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var document = JsonSerializer.Deserialize<StoredDocument>(json, SerializerOptions);
            if (document?.Annotations is not { } stored || document.Version > CurrentVersion)
            {
                return [];
            }

            return [.. stored.Select(FromStored).Where(annotation => annotation is not null).Select(annotation => annotation!)];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (NotSupportedException)
        {
            // A value of the right shape but the wrong type — a string where a number
            // belongs — which a hand-edited file can easily produce.
            return [];
        }
    }

    private static StoredAnnotation ToStored(Annotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);

        return new StoredAnnotation
        {
            Id = annotation.Id,
            Tool = annotation.Tool.ToString(),
            StartX = annotation.Start.X,
            StartY = annotation.Start.Y,
            EndX = annotation.End.X,
            EndY = annotation.End.Y,
            Color = annotation.Style.Color.ToHex(),
            StrokeWidth = annotation.Style.StrokeWidth,
            LineStyle = annotation.Style.LineStyle.ToString(),
            Opacity = annotation.Style.Opacity,
            ArrowStyle = annotation.Style.ArrowStyle.ToString(),
            CornerRadius = annotation.Style.CornerRadius,
            CensorMode = annotation.Style.CensorMode.ToString(),
            ShapeFill = annotation.Style.ShapeFill.ToString(),
            ArrowReversed = annotation.Style.ArrowReversed,
            Outline = annotation.Style.Outline?.ToHex(),
            FontSize = annotation.Style.FontSize,
            FontFamily = annotation.Style.FontFamily,
            Bold = annotation.Style.Bold,
            Italic = annotation.Style.Italic,
            Underline = annotation.Style.Underline,
            Strikethrough = annotation.Style.Strikethrough,
            TextAlignment = annotation.Style.TextAlignment.ToString(),
            TextBackground = annotation.Style.TextBackground?.ToHex(),
            TextOutline = annotation.Style.TextOutline?.ToHex(),
            TextGlyphStroke = annotation.Style.TextGlyphStroke?.ToHex(),
            DimOpacity = annotation.Style.DimOpacity,
            NumberFormat = annotation.Style.NumberFormat.ToString(),
            MeasureInPoints = annotation.Style.MeasureInPoints,
            LoupeMagnification = annotation.Style.LoupeMagnification,

            // Flattened rather than an array of objects: a smoothed pencil stroke runs
            // to hundreds of samples, and {"x":1,"y":2} costs four times what 1,2 does
            // for exactly the same two numbers.
            Points = annotation.Points.Count == 0
                ? null
                : [.. annotation.Points.SelectMany(point => new[] { point.X, point.Y })],
            Pressures = annotation.Pressures.Count == 0 ? null : [.. annotation.Pressures],
            Text = annotation.Text,
            NumberValue = annotation.NumberValue,
            GroupId = annotation.GroupId,
            Rotation = annotation.Rotation,
            Bend = annotation.Bend,
            Sprite = annotation.Sprite is { } sprite
                ? new StoredSprite(sprite.Width, sprite.Height, Pack(sprite.Pixels))
                : null,
        };
    }

    private static Annotation? FromStored(StoredAnnotation stored)
    {
        if (!Enum.TryParse<AnnotationTool>(stored.Tool, out var tool) || !Enum.IsDefined(tool))
        {
            return null;
        }

        var color = AnnotationColor.TryParseHex(stored.Color, out var parsed)
            ? parsed
            : AnnotationStyle.Default.Color;

        // A style that cannot be read falls back rather than dropping the mark. Where
        // the annotation is matters more than what colour it was, and the fallback is
        // visible where a missing annotation is not.
        var style = new AnnotationStyle(
            color,
            stored.StrokeWidth > 0 ? stored.StrokeWidth : AnnotationStyle.Default.StrokeWidth,
            Enum.TryParse<LineStyle>(stored.LineStyle, out var lineStyle) && Enum.IsDefined(lineStyle)
                ? lineStyle
                : LineStyle.Solid,
            Math.Clamp(stored.Opacity, 0, 1),
            Enum.TryParse<ArrowStyle>(stored.ArrowStyle, out var arrowStyle) && Enum.IsDefined(arrowStyle)
                ? arrowStyle
                : Annotations.ArrowStyle.Filled,
            Math.Max(0, stored.CornerRadius),

            // Absent from files written before the censor tool had one, and from every
            // file written while this was missing here — which is why the fallback is the
            // enum's own first mode rather than a throw.
            Enum.TryParse<CensorMode>(stored.CensorMode, out var censor) && Enum.IsDefined(censor)
                ? censor
                : Annotations.CensorMode.Pixelate,

            // Absent from every file written before shapes could be filled, where the
            // outline was the only thing a rectangle could be. Stroke is what those files
            // meant, so it is what they reopen as.
            Enum.TryParse<ShapeFill>(stored.ShapeFill, out var fillStyle) && Enum.IsDefined(fillStyle)
                ? fillStyle
                : Annotations.ShapeFill.Stroke)
        {
            FontSize = stored.FontSize > 0 ? stored.FontSize : AnnotationStyle.DefaultFontSize,
            FontFamily = stored.FontFamily ?? string.Empty,
            Bold = stored.Bold,
            Italic = stored.Italic,
            Underline = stored.Underline,
            Strikethrough = stored.Strikethrough,

            // Absent from every file written before the row could align a label, where it
            // reads back as the left edge — which is where an unaligned label already sat.
            TextAlignment = Enum.TryParse<LabelAlignment>(stored.TextAlignment, out var hung)
                && Enum.IsDefined(hung)
                    ? hung
                    : LabelAlignment.Left,
            ArrowReversed = stored.ArrowReversed,
            Outline = AnnotationColor.TryParseHex(stored.Outline, out var halo) ? halo : null,
            TextBackground = AnnotationColor.TryParseHex(stored.TextBackground ?? string.Empty, out var fill)
                ? fill
                : null,
            TextOutline = AnnotationColor.TryParseHex(stored.TextOutline ?? string.Empty, out var edge)
                ? edge
                : null,
            TextGlyphStroke = AnnotationColor.TryParseHex(stored.TextGlyphStroke ?? string.Empty, out var glyph)
                ? glyph
                : null,

            // Absent from every file written before the spotlight had a strength, where it
            // reads back as zero — which is no dim at all, and would reopen a spotlight as
            // a bare rectangle. macshot's own fallback, for the same reason.
            DimOpacity = stored.DimOpacity > 0
                ? Math.Min(1, stored.DimOpacity)
                : AnnotationStyle.DefaultDimOpacity,

            // Absent from files written before a badge could be lettered, where the only
            // thing a badge could count in was digits.
            NumberFormat = Enum.TryParse<NumberFormat>(stored.NumberFormat, out var counting)
                && Enum.IsDefined(counting)
                    ? counting
                    : Annotations.NumberFormat.Decimal,
            MeasureInPoints = stored.MeasureInPoints,

            // Zero in every file written before the loupe had a slider, and a loupe that
            // reopened at no magnification would be a circle drawn for no reason.
            LoupeMagnification = stored.LoupeMagnification >= 1
                ? Math.Min(stored.LoupeMagnification, AnnotationStyle.MaxLoupeMagnification)
                : AnnotationStyle.DefaultLoupeMagnification,
        };

        var sprite = Unpack(stored.Sprite);

        // A sprite-backed tool without its pixels would hit test an area that draws
        // nothing, which reads as an invisible mark the user cannot get rid of.
        if (sprite is null && Annotation.RequiresSprite(tool))
        {
            return null;
        }

        var points = ToPoints(stored.Points);

        return new Annotation(
            stored.Id == Guid.Empty ? Guid.NewGuid() : stored.Id,
            tool,
            new CapturePoint(stored.StartX, stored.StartY),
            new CapturePoint(stored.EndX, stored.EndY),
            style)
        {
            Points = points,

            // Dropped unless there is exactly one for each sample. A half-length list
            // would taper the start of the stroke and leave the rest at one width, which
            // is worse than the even stroke a missing list gives.
            Pressures = stored.Pressures is { } weights && weights.Length == points.Count
                ? weights
                : [],
            Text = stored.Text,
            NumberValue = stored.NumberValue,
            GroupId = stored.GroupId,
            Rotation = double.IsFinite(stored.Rotation) ? stored.Rotation : 0,
            Bend = double.IsFinite(stored.Bend) ? stored.Bend : 0,
            Sprite = sprite,
        };
    }

    /// <summary>
    /// Pairs the flattened samples back up, ignoring a trailing odd one: half a point
    /// is not a point, and a hand-edited file can easily hold one.
    /// </summary>
    private static IReadOnlyList<CapturePoint> ToPoints(double[]? flattened)
    {
        if (flattened is null || flattened.Length < 2)
        {
            return [];
        }

        var points = new CapturePoint[flattened.Length / 2];
        for (var index = 0; index < points.Length; index++)
        {
            points[index] = new CapturePoint(flattened[index * 2], flattened[(index * 2) + 1]);
        }

        return points;
    }

    private static string Pack(ReadOnlySpan<byte> pixels)
    {
        using var packed = new MemoryStream();
        using (var deflate = new DeflateStream(packed, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(pixels);
        }

        return Convert.ToBase64String(packed.ToArray());
    }

    private static AnnotationSprite? Unpack(StoredSprite? stored)
    {
        if (stored is not { Width: > 0, Height: > 0 } sprite || string.IsNullOrEmpty(sprite.Pixels))
        {
            return null;
        }

        try
        {
            using var packed = new MemoryStream(Convert.FromBase64String(sprite.Pixels));
            using var deflate = new DeflateStream(packed, CompressionMode.Decompress);
            using var pixels = new MemoryStream();
            deflate.CopyTo(pixels);

            // Checked before the constructor sees it: the constructor's answer to a
            // buffer of the wrong size is an exception, and this is the one place that
            // knows the buffer came from a file rather than from a bitmap.
            var expected = checked((long)sprite.Width * sprite.Height * 4);
            return pixels.Length == expected
                ? new AnnotationSprite(sprite.Width, sprite.Height, pixels.ToArray())
                : null;
        }
        catch (Exception exception) when (exception is FormatException or InvalidDataException or OverflowException)
        {
            return null;
        }
    }

    private sealed record StoredDocument(int Version, StoredAnnotation[] Annotations);

    /// <summary>
    /// The stored shape of one annotation. Flat rather than nested, because the style
    /// is five values and a nested object for them would be five more lines of braces
    /// in a file that is already the largest thing in the history folder.
    /// </summary>
    private sealed record StoredAnnotation
    {
        public Guid Id { get; init; }

        /// <summary>
        /// The tool's name rather than its number, and a string rather than the enum
        /// itself.
        /// </summary>
        /// <remarks>
        /// A name so that reordering the enum cannot turn every stored arrow into an
        /// ellipse. A string so that a name this version does not know costs one
        /// annotation instead of the whole document: the serializer's own enum converter
        /// throws on an unknown name, and the throw takes every other mark with it.
        /// </remarks>
        public string Tool { get; init; } = string.Empty;

        public double StartX { get; init; }

        public double StartY { get; init; }

        public double EndX { get; init; }

        public double EndY { get; init; }

        public string Color { get; init; } = string.Empty;

        public double StrokeWidth { get; init; }

        public string LineStyle { get; init; } = string.Empty;

        public double Opacity { get; init; } = 1;

        public string ArrowStyle { get; init; } = string.Empty;

        public string ShapeFill { get; init; } = string.Empty;

        public double CornerRadius { get; init; }

        /// <summary>
        /// How a censored region is covered. Written since the tool grew four modes;
        /// a file from before that reads back as the first of them.
        /// </summary>
        public string CensorMode { get; init; } = string.Empty;

        public double FontSize { get; init; }

        public string? FontFamily { get; init; }

        public bool Bold { get; init; }

        public bool Italic { get; init; }

        public bool Underline { get; init; }

        public bool Strikethrough { get; init; }

        /// <summary>
        /// Which edge a label's lines are hung from, by name for the reason
        /// <see cref="Tool"/> is by name. Written since the row could align one; a file
        /// from before that reads back as the left edge.
        /// </summary>
        public string TextAlignment { get; init; } = string.Empty;

        public bool ArrowReversed { get; init; }

        public string? Outline { get; init; }

        public string? TextBackground { get; init; }

        public string? TextOutline { get; init; }

        /// <summary>The line round each glyph, or absent for none.</summary>
        public string? TextGlyphStroke { get; init; }

        /// <summary>
        /// How dark a spotlight takes what is outside it. Written since the tool
        /// existed; a file from before that reads back at macshot's own strength.
        /// </summary>
        public double DimOpacity { get; init; }

        /// <summary>
        /// What a badge counts in, by name for the reason <see cref="Tool"/> is by name.
        /// Written since badges could be lettered; a file from before that reads back as
        /// digits.
        /// </summary>
        public string NumberFormat { get; init; } = string.Empty;

        /// <summary>Whether a ruler's reading is in points rather than captured pixels.</summary>
        public bool MeasureInPoints { get; init; }

        /// <summary>
        /// How much a loupe enlarges. Zero in files written before it was adjustable,
        /// which reads back as the default rather than as no magnification at all.
        /// </summary>
        public double LoupeMagnification { get; init; }

        public double[]? Points { get; init; }

        /// <summary>
        /// One pen pressure per sample in <see cref="Points"/>, or absent for a stroke of
        /// one width. Not flattened like the points are — there is only one number per
        /// sample, so there is nothing to pair up.
        /// </summary>
        public double[]? Pressures { get; init; }

        public string? Text { get; init; }

        public int NumberValue { get; init; }

        public Guid? GroupId { get; init; }

        public double Rotation { get; init; }

        public double Bend { get; init; }

        public StoredSprite? Sprite { get; init; }
    }

    private sealed record StoredSprite(int Width, int Height, string Pixels);
}
