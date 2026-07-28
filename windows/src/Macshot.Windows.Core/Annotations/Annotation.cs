using System.Globalization;
using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Annotations;

public readonly record struct AnnotationColor(byte Red, byte Green, byte Blue, byte Alpha = byte.MaxValue)
{
    /// <summary>
    /// <c>#AARRGGBB</c>, the form the settings file stores. Alpha is always written
    /// so a round trip cannot quietly turn a translucent marker opaque.
    /// </summary>
    public string ToHex() => $"#{Alpha:X2}{Red:X2}{Green:X2}{Blue:X2}";

    /// <summary>
    /// Accepts <c>#AARRGGBB</c> and <c>#RRGGBB</c>, with or without the hash, because
    /// the settings file is meant to be hand-editable and six digits is what a person
    /// will type.
    /// </summary>
    public static bool TryParseHex(string? text, out AnnotationColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var digits = text.Trim().TrimStart('#');
        if (digits.Length is not (6 or 8)
            || !uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        var alpha = digits.Length == 8 ? (byte)(value >> 24) : byte.MaxValue;
        color = new AnnotationColor((byte)(value >> 16), (byte)(value >> 8), (byte)value, alpha);
        return true;
    }
}

public sealed record AnnotationStyle(
    AnnotationColor Color,
    double StrokeWidth,
    LineStyle LineStyle = LineStyle.Solid,
    double Opacity = 1)
{
    public static AnnotationStyle Default { get; } = new(new AnnotationColor(76, 194, 255), 3);

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(StrokeWidth);
        if (Opacity is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Opacity));
        }
    }
}

/// <summary>
/// One annotation in frame space (physical capture pixels, top-left origin).
/// The type is immutable so that copying is compiler generated: the macOS product
/// hand-writes <c>clone()</c> and has to remember three places whenever a property
/// is added. Use <c>with</c> expressions to derive edited annotations.
/// </summary>
public sealed record Annotation(
    Guid Id,
    AnnotationTool Tool,
    CapturePoint Start,
    CapturePoint End,
    AnnotationStyle Style)
{
    /// <summary>
    /// Freeform samples for tools that are not defined by two corners, such as
    /// <see cref="AnnotationTool.Pencil"/>. Empty for every other tool.
    /// </summary>
    public IReadOnlyList<CapturePoint> Points { get; init; } = [];

    public string? Text { get; init; }

    public int NumberValue { get; init; }

    /// <summary>
    /// Rasterized pixels for the tools Core cannot draw itself — text, number,
    /// stamp — and null for every other tool. Being an ordinary member of the record
    /// is what makes it survive <c>with</c> expressions, so dragging a badge cannot
    /// lose its glyphs. See <c>docs/windows-port/architecture.md</c>, decision D7.
    /// </summary>
    public AnnotationSprite? Sprite { get; init; }

    /// <summary>
    /// Annotations produced by one user action (auto-redact, paste) share a group
    /// id so callers can present and remove them as a single item.
    /// </summary>
    public Guid? GroupId { get; init; }

    public static Annotation Create(
        AnnotationTool tool,
        CapturePoint start,
        CapturePoint end,
        AnnotationStyle? style = null)
    {
        return new Annotation(Guid.NewGuid(), tool, start, end, style ?? AnnotationStyle.Default);
    }

    public static Annotation CreateFreeform(
        AnnotationTool tool,
        IEnumerable<CapturePoint> points,
        AnnotationStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(points);

        var samples = points.ToArray();
        if (samples.Length == 0)
        {
            throw new ArgumentException("A freeform annotation needs at least one sample.", nameof(points));
        }

        return new Annotation(Guid.NewGuid(), tool, samples[0], samples[^1], style ?? AnnotationStyle.Default)
        {
            Points = samples,
        };
    }

    /// <summary>
    /// A sprite-backed annotation with its top-left at <paramref name="origin"/>.
    /// The bounds come from the sprite instead of from a drag, because the sprite is
    /// composited one to one: bounds that disagreed with it would hit test an area
    /// the mark does not cover.
    /// </summary>
    public static Annotation CreateSprite(
        AnnotationTool tool,
        CapturePoint origin,
        AnnotationSprite sprite,
        AnnotationStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(sprite);

        if (!RequiresSprite(tool))
        {
            throw new ArgumentException($"{tool} is drawn from geometry, not from a sprite.", nameof(tool));
        }

        var end = new CapturePoint(origin.X + sprite.Width, origin.Y + sprite.Height);
        return new Annotation(Guid.NewGuid(), tool, origin, end, style ?? AnnotationStyle.Default)
        {
            Sprite = sprite,
        };
    }

    /// <summary>
    /// Tools whose mark is rasterized pixels rather than geometry, and which are
    /// therefore invalid without a <see cref="Sprite"/>.
    /// </summary>
    public static bool RequiresSprite(AnnotationTool tool) =>
        tool is AnnotationTool.Text or AnnotationTool.Number or AnnotationTool.Stamp;

    /// <summary>Tools that describe an interaction rather than a drawn mark cannot be dragged.</summary>
    public bool IsMovable => Tool is not (AnnotationTool.Crop or AnnotationTool.ColorSampler or AnnotationTool.Select);

    /// <summary>Tools that rewrite the pixels inside their bounds instead of drawing on top of them.</summary>
    public bool IsRegionEffect => Tool is AnnotationTool.Pixelate or AnnotationTool.Blur;

    public bool IsFilled => Tool is AnnotationTool.FilledRectangle;

    public CaptureRegion BoundingRect
    {
        get
        {
            if (Points.Count == 0)
            {
                return CaptureRegion.FromPoints(Start.X, Start.Y, End.X, End.Y);
            }

            var minX = Points[0].X;
            var maxX = minX;
            var minY = Points[0].Y;
            var maxY = minY;
            for (var index = 1; index < Points.Count; index++)
            {
                minX = Math.Min(minX, Points[index].X);
                maxX = Math.Max(maxX, Points[index].X);
                minY = Math.Min(minY, Points[index].Y);
                maxY = Math.Max(maxY, Points[index].Y);
            }

            return CaptureRegion.FromPoints(minX, minY, maxX, maxY);
        }
    }

    /// <summary>
    /// Tests whether a frame-space point grabs this annotation. Filled and region
    /// effect tools are grabbed anywhere inside their bounds; outline tools are
    /// grabbed only near the stroke, so a click inside an empty rectangle falls
    /// through to whatever is behind it.
    /// </summary>
    public bool HitTest(CapturePoint point, double threshold = 6)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(threshold);

        var tolerance = threshold + Style.StrokeWidth / 2;
        var bounds = BoundingRect;

        if (IsFilled || IsRegionEffect || Tool is AnnotationTool.Text or AnnotationTool.Number
            or AnnotationTool.Stamp or AnnotationTool.Loupe or AnnotationTool.TranslateOverlay)
        {
            return Contains(bounds, point, tolerance);
        }

        if (Points.Count > 0)
        {
            return HitTestPolyline(Points, point, tolerance);
        }

        return Tool switch
        {
            AnnotationTool.Rectangle => Contains(bounds, point, tolerance)
                && !Contains(Deflate(bounds, tolerance), point, 0),
            AnnotationTool.Ellipse => HitTestEllipseOutline(bounds, point, tolerance),
            _ => DistanceToSegment(Start, End, point) <= tolerance,
        };
    }

    public Annotation Translate(double deltaX, double deltaY)
    {
        return this with
        {
            Start = new CapturePoint(Start.X + deltaX, Start.Y + deltaY),
            End = new CapturePoint(End.X + deltaX, End.Y + deltaY),
            Points = Points.Count == 0
                ? Points
                : Points.Select(point => new CapturePoint(point.X + deltaX, point.Y + deltaY)).ToArray(),
        };
    }

    private static bool HitTestPolyline(IReadOnlyList<CapturePoint> points, CapturePoint point, double tolerance)
    {
        if (points.Count == 1)
        {
            return Distance(points[0], point) <= tolerance;
        }

        for (var index = 1; index < points.Count; index++)
        {
            if (DistanceToSegment(points[index - 1], points[index], point) <= tolerance)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HitTestEllipseOutline(CaptureRegion bounds, CapturePoint point, double tolerance)
    {
        var radiusX = bounds.Width / 2;
        var radiusY = bounds.Height / 2;
        if (radiusX <= 0 || radiusY <= 0)
        {
            return DistanceToSegment(
                new CapturePoint(bounds.X, bounds.Y),
                new CapturePoint(bounds.X + bounds.Width, bounds.Y + bounds.Height),
                point) <= tolerance;
        }

        var normalizedX = (point.X - (bounds.X + radiusX)) / radiusX;
        var normalizedY = (point.Y - (bounds.Y + radiusY)) / radiusY;
        var normalizedDistance = Math.Sqrt(normalizedX * normalizedX + normalizedY * normalizedY);

        // Scaling the normalized error by the smaller radius approximates the real
        // distance to the outline closely enough for a pointer tolerance.
        return Math.Abs(normalizedDistance - 1) * Math.Min(radiusX, radiusY) <= tolerance;
    }

    private static bool Contains(CaptureRegion region, CapturePoint point, double tolerance)
    {
        return point.X >= region.X - tolerance
            && point.X <= region.X + region.Width + tolerance
            && point.Y >= region.Y - tolerance
            && point.Y <= region.Y + region.Height + tolerance;
    }

    private static CaptureRegion Deflate(CaptureRegion region, double amount)
    {
        var width = Math.Max(0, region.Width - amount * 2);
        var height = Math.Max(0, region.Height - amount * 2);
        return new CaptureRegion(region.X + amount, region.Y + amount, width, height);
    }

    private static double Distance(CapturePoint first, CapturePoint second)
    {
        var deltaX = second.X - first.X;
        var deltaY = second.Y - first.Y;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    private static double DistanceToSegment(CapturePoint start, CapturePoint end, CapturePoint point)
    {
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        var lengthSquared = deltaX * deltaX + deltaY * deltaY;
        if (lengthSquared <= 0)
        {
            return Distance(start, point);
        }

        var projection = ((point.X - start.X) * deltaX + (point.Y - start.Y) * deltaY) / lengthSquared;
        projection = Math.Clamp(projection, 0, 1);
        return Distance(new CapturePoint(start.X + deltaX * projection, start.Y + deltaY * projection), point);
    }
}
