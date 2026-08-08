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
    double Opacity = 1,
    ArrowStyle ArrowStyle = ArrowStyle.Filled,
    double CornerRadius = 0,
    CensorMode CensorMode = CensorMode.Pixelate,
    ShapeFill ShapeFill = ShapeFill.Stroke)
{
    /// <summary>
    /// How much of the colour's alpha a <see cref="Annotations.ShapeFill.StrokeAndFill"/>
    /// wash keeps — macshot's <c>alphaComponent * 0.5</c>
    /// (<c>Annotation.swift:1467</c>). Half rather than a fixed value, so a colour
    /// already dialled down stays dialled down.
    /// </summary>
    public const double WashAlpha = 0.5;

    /// <summary>macshot's own starting size for a label — <c>Annotation.swift:159</c>.</summary>
    public const double DefaultFontSize = 20;

    /// <summary>The smallest and largest a label can be set to from the toolbar.</summary>
    /// <remarks>
    /// macshot's own two bounds — <c>ToolOptionsRowView.swift:1780, 1791</c>. The ceiling
    /// is generous because a label put on a 4K screenshot to be read in a slide is not the
    /// same size as one put beside a menu item, and a tool that stopped halfway would send
    /// the user to an image editor for the difference.
    /// </remarks>
    public const double MinFontSize = 8;

    public const double MaxFontSize = 200;

    /// <summary>
    /// How far one press of the row's − or + moves a label's size. One point, as macshot
    /// steps it: the row shows the number it lands on, so a coarser step would skip sizes
    /// the user can see written there and would have no other way to reach.
    /// </summary>
    public const double FontSizeStep = 1;

    /// <summary>
    /// How wide the line round each glyph is drawn, as a fraction of the label's size.
    /// macshot's <c>OutlineTextRenderer.autoWidthFraction</c>.
    /// </summary>
    /// <remarks>
    /// A fraction rather than a width, because the whole job of the outline is to hold a
    /// label apart from whatever is behind it: a fixed width would be a hairline at 72
    /// point and would swallow the glyphs at 8.
    /// </remarks>
    public const double GlyphStrokeFraction = 0.09;

    /// <summary>
    /// How much wider than the mark its halo is drawn — macshot's <c>strokeWidth + 6</c>.
    /// </summary>
    public const double OutlineSpread = 6;

    /// <summary>
    /// How big a stamp is placed — macshot's own 64 (<c>OverlayView.swift:568-571</c>).
    /// </summary>
    /// <remarks>
    /// Its own number rather than a multiple of the stroke width, for the reason the font
    /// size is: a stamp is placed with a click, so nothing about the gesture says how big
    /// it should be, and deriving it from the stroke meant a bigger tick could not be had
    /// without a thicker arrow after it. 64 is about the size of a large icon, which is
    /// what a stamp is standing in for.
    /// </remarks>
    public const double DefaultStampSize = 64;

    /// <summary>The smallest the row offers. Below it a colour emoji is a coloured smudge.</summary>
    public const double MinStampSize = 16;

    /// <summary>
    /// The largest. macshot's ceiling: past it the stamp is the picture rather than a mark
    /// on it.
    /// </summary>
    public const double MaxStampSize = 256;

    /// <summary>
    /// How much a loupe enlarges what is under it by default. Two, as on macOS: enough to
    /// read a hairline, little enough that the circle still shows its surroundings rather
    /// than four fat pixels.
    /// </summary>
    public const double DefaultLoupeMagnification = 2;

    /// <summary>
    /// The weakest magnification worth having. Below this the circle is a circle drawn on
    /// the screenshot for no reason, and the tool would be offering the user a way to
    /// produce one.
    /// </summary>
    public const double MinLoupeMagnification = 1.1;

    /// <summary>macshot's ceiling. Past it the loupe shows pixels rather than content.</summary>
    public const double MaxLoupeMagnification = 6;

    /// <summary>
    /// How wide a loupe is drawn — macshot's own 120 (<c>OverlayView.swift:597-600</c>).
    /// </summary>
    /// <remarks>
    /// Its own number rather than a multiple of the stroke width, which is what it used to
    /// be: a loupe is placed rather than dragged out, so nothing about the gesture says how
    /// big it should be, and deriving it from the stroke meant the circle could not be
    /// sized without also thickening the next arrow. It is the number the row's slider
    /// actually sets — 120 across at 2x shows about sixty pixels of the capture, which is a
    /// word or a control rather than a letter or a whole panel.
    /// </remarks>
    public const double DefaultLoupeSize = 120;

    /// <summary>
    /// The smallest the row offers. Below it the ring and the magnified pixels are the same
    /// few, and there is nothing to read inside the circle.
    /// </summary>
    public const double MinLoupeSize = 40;

    /// <summary>
    /// The largest. macshot's ceiling: past this the loupe covers more of the capture than
    /// it explains, and the reader loses the thing being pointed at.
    /// </summary>
    public const double MaxLoupeSize = 320;

    /// <summary>
    /// How dark a spotlight takes everything outside it to begin with — macshot's own
    /// <c>0.55</c> (<c>Annotation.swift:170</c>). Strong enough that the eye goes to the
    /// bright part first, weak enough that what surrounds it can still be read.
    /// </summary>
    public const double DefaultDimOpacity = 0.55;

    /// <summary>
    /// The weakest dim the row offers. Below it the spotlight stops being one: the capture
    /// outside is as bright as the part inside, and the ring is a rectangle drawn for no
    /// reason. macshot's own floor.
    /// </summary>
    public const double MinDimOpacity = 0.1;

    /// <summary>
    /// The strongest. macshot stops short of 1 on purpose — a spotlight that takes its
    /// surroundings to black has cropped the capture rather than pointed into it, and the
    /// tool for cropping is the one called Crop.
    /// </summary>
    public const double MaxDimOpacity = 0.95;

    public static AnnotationStyle Default { get; } = new(new AnnotationColor(76, 194, 255), 3);

    /// <summary>
    /// How big a label's text is, in frame pixels, independent of the stroke width.
    /// </summary>
    /// <remarks>
    /// Its own number rather than a multiple of <see cref="StrokeWidth"/>, which is what
    /// it used to be: one slider for both meant a label could not be sized without also
    /// resizing the next arrow drawn. macshot keeps <c>fontSize</c> apart from the stroke
    /// for the same reason.
    /// </remarks>
    public double FontSize { get; init; } = DefaultFontSize;

    /// <summary>
    /// The face a label is set in, or empty for the system font. A family this machine
    /// does not have falls back to the system font where the text is rendered rather
    /// than being refused here — a settings file moved between machines is expected to
    /// name a font one of them lacks.
    /// </summary>
    public string FontFamily { get; init; } = string.Empty;

    /// <summary>Whether a label is set bold.</summary>
    public bool Bold { get; init; }

    /// <summary>Whether a label is set italic.</summary>
    public bool Italic { get; init; }

    /// <summary>Whether a label is underlined.</summary>
    public bool Underline { get; init; }

    /// <summary>Whether a label is struck through.</summary>
    /// <remarks>
    /// Its own switch beside the other three rather than one of four weights, which is
    /// what this row used to offer. They are not alternatives: a label can be bold and
    /// underlined at once, and macshot gives each of them a button that turns on
    /// independently (<c>ToolOptionsRowView.swift:919–942</c>). A picker that made them
    /// exclusive would take away combinations the user can plainly see are possible.
    /// </remarks>
    public bool Strikethrough { get; init; }

    /// <summary>Which edge a label's lines are hung from.</summary>
    public LabelAlignment TextAlignment { get; init; } = LabelAlignment.Left;

    /// <summary>
    /// The line drawn round each glyph, or null for none. macshot's
    /// <c>textGlyphStrokeColor</c>.
    /// </summary>
    /// <remarks>
    /// Not the same thing as <see cref="TextOutline"/>, which is the line round the pill
    /// behind the whole label. This one follows the letters, and it is what makes white
    /// text readable over a screenshot that is white in some places and black in others —
    /// the case where no single fill colour works and a pill would cover the thing being
    /// pointed at.
    /// </remarks>
    public AnnotationColor? TextGlyphStroke { get; init; }

    /// <summary>
    /// The pill drawn behind a label, or null for none. macshot's <c>textBgColor</c>,
    /// with its 4 of padding and its 4 corner — <c>Annotation.swift:1646–1655</c>.
    /// </summary>
    public AnnotationColor? TextBackground { get; init; }

    /// <summary>
    /// The 2-wide line around that pill, or null for none. macshot's
    /// <c>textOutlineColor</c>, which is what makes a label readable over a screenshot
    /// whose colours the fill happens to match.
    /// </summary>
    public AnnotationColor? TextOutline { get; init; }

    /// <summary>
    /// Whether an arrow points back the way it was drawn.
    /// </summary>
    /// <remarks>
    /// macshot's <c>arrowReversed</c>. It exists because an arrow is drawn from where the
    /// hand starts to where the hand stops, and what it should point at is often where the
    /// hand started — pointing at a menu item means dragging out of the menu, which is
    /// exactly the drag that is hardest to aim.
    /// </remarks>
    public bool ArrowReversed { get; init; }

    /// <summary>
    /// A contrasting halo laid under the mark, or null for none.
    /// </summary>
    /// <remarks>
    /// macshot's <c>outlineColor</c>. A red arrow over a red button is invisible, and the
    /// answer is not to change the arrow's colour — it is to put a rim round it. Drawn as
    /// the same path stroked <see cref="OutlineSpread"/> wider and always solid, because a
    /// dashed halo is a row of dots round a dashed line and reads as neither.
    /// </remarks>
    public AnnotationColor? Outline { get; init; }

    /// <summary>
    /// How dark everything outside a spotlight is taken: 0 leaves the capture as it was
    /// and 1 is black. Read by <see cref="AnnotationTool.Highlight"/> and by nothing else.
    /// </summary>
    /// <remarks>
    /// Its own number rather than <see cref="Opacity"/>, which says how translucent a mark
    /// is drawn. A spotlight's mark is the hairline round it, so sharing the one number
    /// would tie the two together — asking for a fainter border would lift the dim, and
    /// the tool's only real control would have a side effect nobody asked for. macshot
    /// keeps <c>dimOpacity</c> apart from the colour's alpha for the same reason.
    /// </remarks>
    public double DimOpacity { get; init; } = DefaultDimOpacity;

    /// <summary>
    /// What a numbered badge counts in. On the style rather than on the annotation
    /// because it is picked before the badge is placed and remembered after it, which is
    /// what every other tool setting here is.
    /// </summary>
    public NumberFormat NumberFormat { get; init; } = NumberFormat.Decimal;

    /// <summary>
    /// Whether the ruler reports its span in points rather than in captured pixels.
    /// </summary>
    /// <remarks>
    /// macshot's <c>measureInPoints</c>. Both answers are true at once and neither is the
    /// obviously useful one: a designer working to a layout wants the points a rule was
    /// specified in, and anyone checking a screenshot against an asset wants the pixels it
    /// actually occupies. Which one a reading means has to be said, so the tool says it.
    /// </remarks>
    public bool MeasureInPoints { get; init; }

    /// <summary>
    /// How much the loupe enlarges what is under it.
    /// </summary>
    /// <remarks>
    /// A number rather than the fixed 2 it used to be, because what is being magnified
    /// decides it: a code sample needs barely any, and a one-pixel misalignment needs as
    /// much as the tool will give.
    /// </remarks>
    public double LoupeMagnification { get; init; } = DefaultLoupeMagnification;

    /// <summary>
    /// How wide the loupe is placed, in frame pixels. Read by
    /// <see cref="AnnotationTool.Loupe"/> and by nothing else.
    /// </summary>
    /// <remarks>
    /// A loupe is placed with a click rather than dragged out, so unlike every other
    /// region on the canvas its size comes from the row instead of from the gesture — which
    /// is why the number has to live somewhere, and why macshot keeps <c>loupeSize</c>
    /// beside the magnification rather than deriving one from the other.
    /// </remarks>
    public double LoupeSize { get; init; } = DefaultLoupeSize;

    /// <summary>
    /// How big a stamp's glyph is drawn, in frame pixels. Read by
    /// <see cref="AnnotationTool.Stamp"/> and by nothing else.
    /// </summary>
    /// <remarks>
    /// Beside <see cref="FontSize"/> rather than sharing it, though both size a glyph: a
    /// label and a stamp are placed for different reasons and are wanted at different
    /// sizes, and one number for both would mean a tick could not be made bigger without
    /// enlarging the next caption too.
    /// </remarks>
    public double StampSize { get; init; } = DefaultStampSize;

    /// <summary>
    /// Where one press of the row's − or + lands, from wherever the size is now.
    /// </summary>
    /// <remarks>
    /// Here rather than in the toolbar so the two bounds are enforced in the same place
    /// they are declared. The row holds the size the user is walking rather than reading it
    /// back off the style each press, and a size that had gone missing — a settings file
    /// with no label size in it yet — would otherwise step from nothing at all.
    /// </remarks>
    public static double StepFontSize(double from, int steps) => Math.Clamp(
        (double.IsFinite(from) ? from : DefaultFontSize) + (steps * FontSizeStep),
        MinFontSize,
        MaxFontSize);

    /// <summary>
    /// How wide to draw the line round each glyph of a label of this size, in the same
    /// units the size is given in. Never thinner than a whole unit: below that the outline
    /// is an antialiasing artefact rather than an edge, which is worse than none.
    /// </summary>
    public static double GlyphStrokeWidth(double fontSize) =>
        Math.Max(1, Math.Clamp(fontSize, MinFontSize, MaxFontSize) * GlyphStrokeFraction);

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(StrokeWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(CornerRadius);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(FontSize);
        ArgumentOutOfRangeException.ThrowIfLessThan(LoupeMagnification, 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(LoupeSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(StampSize);
        if (Opacity is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Opacity));
        }

        if (DimOpacity is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(DimOpacity));
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

    /// <summary>
    /// How hard the pen was pressed at each sample in <see cref="Points"/>, from 0 to 1,
    /// or empty for a stroke of one width.
    /// </summary>
    /// <remarks>
    /// <para>
    /// macshot's <c>pressures</c>. Parallel to <see cref="Points"/> rather than carried on
    /// each point, because every other tool has points and none of them has a pressure —
    /// a nullable field on <see cref="CapturePoint"/> would double the size of every
    /// rectangle in the document to describe something only the pencil can produce.
    /// </para>
    /// <para>
    /// Empty is not the same as all-0.5: it means the stroke was drawn with the pressure
    /// option off, and its width must not be touched at all.
    /// </para>
    /// </remarks>
    public IReadOnlyList<double> Pressures { get; init; } = [];

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

    /// <summary>
    /// Radians clockwise about the centre of <see cref="BoundingRect"/>.
    /// </summary>
    /// <remarks>
    /// Held rather than baked into the points, so a rotation can be taken back by
    /// dragging the handle to where it started. Rewriting <see cref="Start"/> and
    /// <see cref="End"/> in place would lose the upright rectangle, and every further
    /// drag would compound the last one's rounding.
    /// </remarks>
    public double Rotation { get; init; }

    /// <summary>
    /// Intermediate anchors a line, arrow or ruler is bent through, in the order they are
    /// passed, between <see cref="Start"/> and <see cref="End"/>. Empty for a mark with two
    /// ends and nothing between them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Its own list rather than a second use of <see cref="Points"/>, though both are a run
    /// of points on the same record. A non-empty <see cref="Points"/> means "this mark
    /// <em>is</em> its samples", and three places read it that way —
    /// <see cref="BoundingRect"/> takes the bounds from the samples alone,
    /// <see cref="HitTest"/> grabs along them at a stroke's tolerance, and
    /// <see cref="AnnotationHandles.For"/> offers no handles at all. A three-anchor arrow
    /// stored there would inherit every one of those. They are not the same kind of list
    /// either: a pencil's samples are the path that was drawn, and these are waypoints a
    /// curve is fitted through.
    /// </para>
    /// <para>
    /// The intermediate anchors only, where macOS stores the whole chain including both
    /// ends (<c>anchorPoints</c>, <c>Annotation.swift:183</c>) and then keeps
    /// <c>startPoint</c> and <c>endPoint</c> agreeing with its first and last by hand in
    /// five separate places. On an immutable record that duplication is a bug waiting for
    /// the sixth: here <see cref="AnchorPath"/> derives the chain, so the ends cannot drift
    /// away from it.
    /// </para>
    /// <para>
    /// Treated as immutable, the way <see cref="Points"/> is — derive an edited mark with a
    /// <c>with</c> expression rather than writing into the list.
    /// </para>
    /// </remarks>
    public IReadOnlyList<CapturePoint> Waypoints { get; init; } = [];

    /// <summary>
    /// How far the bend's control point sits to the side of the straight path between the
    /// ends, as a fraction of that path's length. Zero is straight.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One control point, as macOS has: it bows a line with
    /// <c>curve(to:controlPoint1:controlPoint2:)</c> passing the same point twice
    /// (<c>Annotation.swift:891</c>). Together with <see cref="BendAlong"/> this is that
    /// point, so the curve drawn here is the curve macshot draws.
    /// </para>
    /// <para>
    /// A fraction rather than a distance, so a bent arrow keeps its shape when it is
    /// dragged longer — which is what the user drew, rather than what a control point
    /// pinned at an absolute offset would give them. This is the one place the port does
    /// not copy macOS, which stores the point in screen coordinates and so lets a bow
    /// flatten out as the line it belongs to is stretched.
    /// </para>
    /// <para>
    /// Dead once the mark carries <see cref="Waypoints"/>: the two describe the same shape
    /// and only one of them can be drawn, so adding the first anchor clears this — as
    /// macshot clears its <c>controlPoint</c> (<c>OverlayView.swift:8797</c>).
    /// </para>
    /// </remarks>
    public double Bend { get; init; }

    /// <summary>
    /// Where that control point sits along the straight path, as a fraction of its length
    /// away from the middle. Zero is level with the middle; positive is towards the end.
    /// </summary>
    /// <remarks>
    /// The second half of a control point that macshot drags freely
    /// (<c>OverlayView.swift:6011</c>, which stores the pointer where it is). Without it a
    /// bow could only ever be symmetric, and the asymmetric bulge — the one that clears an
    /// obstacle near one end of an arrow rather than in the middle of it — was
    /// unreachable however far the handle was dragged.
    /// </remarks>
    public double BendAlong { get; init; }

    public static Annotation Create(
        AnnotationTool tool,
        CapturePoint start,
        CapturePoint end,
        AnnotationStyle? style = null)
    {
        return new Annotation(Guid.NewGuid(), tool, start, end, style ?? AnnotationStyle.Default);
    }

    /// <param name="pressures">
    /// How hard the pen was pressed at each sample, or null for a stroke of one width.
    /// A list that does not match <paramref name="points"/> is ignored rather than
    /// refused: pressure is an embellishment, and losing it is better than losing the
    /// stroke the user drew.
    /// </param>
    public static Annotation CreateFreeform(
        AnnotationTool tool,
        IEnumerable<CapturePoint> points,
        AnnotationStyle? style = null,
        IEnumerable<double>? pressures = null)
    {
        ArgumentNullException.ThrowIfNull(points);

        var samples = points.ToArray();
        if (samples.Length == 0)
        {
            throw new ArgumentException("A freeform annotation needs at least one sample.", nameof(points));
        }

        var weights = pressures?.ToArray() ?? [];
        return new Annotation(Guid.NewGuid(), tool, samples[0], samples[^1], style ?? AnnotationStyle.Default)
        {
            Points = samples,
            Pressures = weights.Length == samples.Length ? weights : [],
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

    /// <summary>
    /// Tools that can be given intermediate <see cref="Waypoints"/>: the three macOS offers
    /// them on (<c>OverlayView.swift:5493</c>). An area shape is its bounding rectangle and
    /// a freehand stroke is already a path, so neither has anywhere to put one.
    /// </summary>
    public static bool AcceptsWaypoints(AnnotationTool tool) =>
        tool is AnnotationTool.Line or AnnotationTool.Arrow or AnnotationTool.Measure;

    /// <summary>Whether this mark has been bent through anchors — macshot's <c>hasMultiAnchor</c>.</summary>
    public bool HasWaypoints => Waypoints.Count > 0;

    /// <summary>
    /// The full ordered chain the mark runs along: its start, each anchor in turn, its end.
    /// macshot's <c>waypoints</c> (<c>Annotation.swift:187</c>), derived here rather than
    /// stored so the ends cannot disagree with it.
    /// </summary>
    public IReadOnlyList<CapturePoint> AnchorPath
    {
        get
        {
            if (Waypoints.Count == 0)
            {
                return [Start, End];
            }

            var chain = new CapturePoint[Waypoints.Count + 2];
            chain[0] = Start;
            for (var index = 0; index < Waypoints.Count; index++)
            {
                chain[index + 1] = Waypoints[index];
            }

            chain[^1] = End;
            return chain;
        }
    }

    /// <summary>
    /// How long the mark is as drawn, in frame pixels: what the ruler reports, and the
    /// length a bend is a fraction of.
    /// </summary>
    /// <remarks>
    /// The straight distance between the ends until the mark is bent through anchors, and
    /// the length of the curve after that — a ruler that still reported the chord would be
    /// writing a number on the picture that does not describe the line beside it. This is
    /// the one place the port goes past macOS rather than matching it: macshot's own
    /// <c>drawMeasure</c> (<c>Annotation.swift:1843</c>) draws a bent ruler as a straight
    /// line between its ends and reports that, while its hit test follows the curve, so a
    /// ruler there can be grabbed along a path nothing drew.
    /// </remarks>
    public double Span => HasWaypoints
        ? SmoothPath.Length(AnchorPath)
        : Distance(Start, End);

    /// <summary>Tools that describe an interaction rather than a drawn mark cannot be dragged.</summary>
    public bool IsMovable => Tool is not (AnnotationTool.Crop or AnnotationTool.ColorSampler or AnnotationTool.Select);

    /// <summary>Tools that rewrite the pixels inside their bounds instead of drawing on top of them.</summary>
    public bool IsRegionEffect => Tool is AnnotationTool.Censor;

    public CaptureRegion BoundingRect
    {
        get
        {
            if (Points.Count == 0)
            {
                if (Waypoints.Count == 0)
                {
                    return CaptureRegion.FromPoints(Start.X, Start.Y, End.X, End.Y);
                }

                // The anchors as well as the ends. A mark bent well clear of the straight
                // line between them has to be outlined and hit tested where it actually
                // runs, or the selection chrome sits beside the mark it belongs to.
                // macshot widens its own bounding rect the same way (Annotation.swift:349).
                var left = Math.Min(Start.X, End.X);
                var right = Math.Max(Start.X, End.X);
                var top = Math.Min(Start.Y, End.Y);
                var bottom = Math.Max(Start.Y, End.Y);
                foreach (var anchor in Waypoints)
                {
                    left = Math.Min(left, anchor.X);
                    right = Math.Max(right, anchor.X);
                    top = Math.Min(top, anchor.Y);
                    bottom = Math.Max(bottom, anchor.Y);
                }

                return CaptureRegion.FromPoints(left, top, right, bottom);
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
    /// Tests whether a frame-space point grabs this annotation. Marks that cover what is
    /// under them are grabbed anywhere inside their bounds; outline tools are grabbed
    /// only near the stroke, so a click inside an empty rectangle falls through to
    /// whatever is behind it.
    /// </summary>
    public bool HitTest(CapturePoint point, double threshold = 6)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(threshold);

        var tolerance = threshold + Style.StrokeWidth / 2;
        var bounds = BoundingRect;

        // The spotlight is grabbed inside its bounds as well, though what it covers is
        // everything else: the lit rectangle is the mark, and asking the user to find its
        // hairline would be asking them to aim at the one part of it that is a pixel wide.
        if (IsRegionEffect || Tool is AnnotationTool.Text or AnnotationTool.Number
            or AnnotationTool.Stamp or AnnotationTool.Loupe or AnnotationTool.TranslateOverlay
            or AnnotationTool.Highlight)
        {
            return Contains(bounds, point, tolerance);
        }

        if (Points.Count > 0)
        {
            return HitTestPolyline(Points, point, tolerance);
        }

        // A mark bent through anchors is grabbed along the curve it is drawn as, never
        // along the chord between its ends — macshot's own branch for the same three tools
        // (Annotation.swift:394-422). Without it a bent arrow answers to clicks on empty
        // canvas and ignores clicks on itself.
        if (HasWaypoints && AcceptsWaypoints(Tool))
        {
            return HitTestPolyline(SmoothPath.Through(AnchorPath), point, tolerance);
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
            Waypoints = Waypoints.Count == 0
                ? Waypoints
                : Waypoints.Select(anchor => new CapturePoint(anchor.X + deltaX, anchor.Y + deltaY)).ToArray(),
        };
    }

    /// <summary>
    /// This mark with one more anchor in it, put on the span of <see cref="AnchorPath"/>
    /// nearest <paramref name="point"/>. Unchanged for a tool that has nowhere to put one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// macshot's <c>addAnchorPoint</c> (<c>OverlayView.swift:8763</c>). Inserted into the
    /// chain at the span it was aimed at rather than appended, so a press near the start of
    /// a mark bends it near its start — appended, every new anchor would land at the far
    /// end and the second one would fold the mark back over itself.
    /// </para>
    /// <para>
    /// Projected onto that span rather than dropped where the pointer was, and held a
    /// twentieth clear of both its ends as macshot holds it: an anchor landing on top of
    /// the point beside it gives the spline a span of no length, which comes out as a kink
    /// rather than as the curve the user asked for.
    /// </para>
    /// </remarks>
    public Annotation WithAnchorAt(CapturePoint point)
    {
        if (!AcceptsWaypoints(Tool))
        {
            return this;
        }

        var chain = AnchorPath;
        var nearest = 1;
        var nearestDistance = double.MaxValue;
        for (var span = 1; span < chain.Count; span++)
        {
            var distance = DistanceToSegment(chain[span - 1], chain[span], point);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = span;
            }
        }

        var from = chain[nearest - 1];
        var to = chain[nearest];
        var deltaX = to.X - from.X;
        var deltaY = to.Y - from.Y;
        var lengthSquared = (deltaX * deltaX) + (deltaY * deltaY);
        var along = lengthSquared < 0.001
            ? 0.5
            : Math.Clamp(
                (((point.X - from.X) * deltaX) + ((point.Y - from.Y) * deltaY)) / lengthSquared,
                0.05,
                0.95);

        // The chain's span index counts from Start, so inserting before the anchor that
        // ends the span means inserting one place earlier in the intermediate list.
        var anchors = new List<CapturePoint>(Waypoints.Count + 1);
        anchors.AddRange(Waypoints);
        anchors.Insert(nearest - 1, new CapturePoint(from.X + (deltaX * along), from.Y + (deltaY * along)));

        return this with
        {
            Waypoints = anchors,

            // The single bend goes the moment the first anchor arrives: the two describe
            // the same shape, only the anchors are drawn, and a bend left set would sit
            // under a grip that no longer exists.
            Bend = 0,
            BendAlong = 0,

            // A ruler's reading is a claim about a length this has just changed, so the
            // old glyphs have to go. The host renders a new sprite for any ruler without
            // one as soon as the gesture ends.
            Sprite = Tool == AnnotationTool.Measure ? null : Sprite,
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
