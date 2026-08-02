namespace Macshot.Windows.Core.Annotations;

/// <summary>
/// Which parts of <see cref="AnnotationStyle"/> a tool actually reads.
/// </summary>
/// <remarks>
/// <para>
/// A toolbar that offers every option for every tool is offering controls that do
/// nothing: a dash pattern on a pixelated block, a colour on a blur. Worse, it teaches
/// the user that the controls do not mean anything, which makes the ones that do harder
/// to find.
/// </para>
/// <para>
/// The answers live here rather than in the toolbar because they are facts about the
/// rasterizer — it is the thing that reads the style — and a copy of them in the UI would
/// go stale the first time a tool changed how it draws.
/// </para>
/// </remarks>
public static class AnnotationToolOptions
{
    /// <summary>
    /// Whether the mark is drawn in the chosen colour. True of the censor tool as well,
    /// though only its solid mode paints in it — the other three replace the pixels with
    /// something derived from what was there. False of the spotlight, whose two colours
    /// are the black it dims with and the white it rings the light with, neither of them
    /// chosen.
    /// </summary>
    public static bool UsesColor(AnnotationTool tool) => Draws(tool) && !IsSpotlight(tool);

    /// <summary>
    /// Whether the width slider does anything. It does for every mark that is drawn,
    /// though not always as a width: it is also the size of a badge or a label. The
    /// censor tool is the exception — how much of a redaction survives is not left to a
    /// slider, so its cell and its radius are fixed and derived from the region. The
    /// spotlight is the other: its ring is a hairline at any size, and the strength it
    /// does have is how far the dim outside it goes, not how wide anything is drawn.
    /// </summary>
    public static bool UsesSize(AnnotationTool tool) =>
        Draws(tool) && !IsRegionEffect(tool) && !IsSpotlight(tool);

    /// <summary>
    /// Whether the dash picker is offered. macshot's own five
    /// (<c>ToolOptionsRowView.swift:144</c>), and the list is shorter than "every mark
    /// drawn as a stroke" on purpose.
    /// </summary>
    /// <remarks>
    /// The highlighter, the ruler and the loupe are the ones left out. Each is a stroke
    /// and each would take a dash, but none of them is a mark whose line is the subject:
    /// a dashed highlighter is a row of blots, a dashed ruler is harder to read a span
    /// off, and the loupe's circle is a frame round what it magnifies. Offering a control
    /// for something nobody wants is how a row of eight becomes a row nobody reads.
    /// </remarks>
    public static bool UsesLineStyle(AnnotationTool tool) => tool
        is AnnotationTool.Pencil
        or AnnotationTool.Line
        or AnnotationTool.Arrow
        or AnnotationTool.Rectangle
        or AnnotationTool.Ellipse;

    /// <summary>
    /// Whether the halo controls are offered — the rim under a mark, in a colour of its
    /// own.
    /// </summary>
    /// <remarks>
    /// macshot's two lists at once: the arrow's own (<c>ToolOptionsRowView.swift:155</c>),
    /// the generic four beside it (<c>:266</c>), and the loupe's, which it draws from its
    /// own controls (<c>:131</c>). Notably not the same set as the dash picker, which is
    /// what this used to be derived from: the badge takes a halo and no dash, and the
    /// pencil takes a dash and no halo.
    /// </remarks>
    public static bool UsesOutline(AnnotationTool tool) => tool
        is AnnotationTool.Arrow
        or AnnotationTool.Line
        or AnnotationTool.Rectangle
        or AnnotationTool.Ellipse
        or AnnotationTool.Number
        or AnnotationTool.Loupe;

    /// <summary>
    /// Whether the corner-rounding control applies. The outlined rectangle only: the
    /// filled one is the redaction tool, and a redaction with rounded corners leaves the
    /// pixels it was meant to cover showing at each one.
    /// </summary>
    public static bool UsesCornerRadius(AnnotationTool tool) => tool == AnnotationTool.Rectangle;

    /// <summary>Whether the arrow-ends picker applies.</summary>
    public static bool UsesArrowStyle(AnnotationTool tool) => tool == AnnotationTool.Arrow;

    /// <summary>
    /// Whether the outline/wash/solid picker applies. The two closed shapes only —
    /// macshot's own answer, and the reason is that nothing else here encloses an area
    /// there would be a difference between filling and not.
    /// </summary>
    public static bool UsesShapeFill(AnnotationTool tool) =>
        tool is AnnotationTool.Rectangle or AnnotationTool.Ellipse;

    /// <summary>Whether the emoji picker applies.</summary>
    public static bool UsesStamp(AnnotationTool tool) => tool == AnnotationTool.Stamp;

    /// <summary>Whether the censor tool's four modes apply.</summary>
    public static bool UsesCensorMode(AnnotationTool tool) => IsRegionEffect(tool);

    /// <summary>
    /// Whether the censor tool's scope — the whole region, or only the text found inside
    /// it — applies. The same tools as the mode, because it is the second half of the same
    /// question: what is covered, and how.
    /// </summary>
    public static bool UsesCensorScope(AnnotationTool tool) => IsRegionEffect(tool);

    /// <summary>Whether the badge's numbering — its format and where it starts — applies.</summary>
    public static bool UsesNumberFormat(AnnotationTool tool) => tool == AnnotationTool.Number;

    /// <summary>Whether the ruler's unit applies.</summary>
    public static bool UsesMeasureUnit(AnnotationTool tool) => tool == AnnotationTool.Measure;

    /// <summary>Whether the loupe's magnification applies.</summary>
    public static bool UsesLoupeMagnification(AnnotationTool tool) => tool == AnnotationTool.Loupe;

    /// <summary>
    /// Whether the dim slider applies — how far down the spotlight takes what is outside
    /// it. The spotlight's only real setting, and the reason it is on this row rather than
    /// in the settings window: how much dimming is enough depends on what is being pointed
    /// at, which is on screen at the time.
    /// </summary>
    public static bool UsesDimStrength(AnnotationTool tool) => IsSpotlight(tool);

    /// <summary>
    /// Whether the spotlight's own border control applies — solid or dashed, and nothing
    /// else.
    /// </summary>
    /// <remarks>
    /// Its own two-way control rather than the general dash picker, which is why
    /// <see cref="UsesLineStyle"/> passes the spotlight over. Dotted is missing on purpose:
    /// the ring sits on whatever the capture happens to show, and a dotted hairline at
    /// 0.6 opacity over a photograph is a row of specks rather than an edge. macshot offers
    /// the same two (<c>ToolOptionsRowView.swift:406-430</c>).
    /// </remarks>
    public static bool UsesSpotlightBorder(AnnotationTool tool) => IsSpotlight(tool);

    /// <summary>
    /// Whether the highlighter's snap-to-text option applies.
    /// </summary>
    /// <remarks>
    /// The marker only. The pencil could be snapped to a line of text as easily, but a
    /// pencil is the tool you reach for when the thing you want to mark is not a line of
    /// text — snapping it would be undoing the choice the user made by picking it.
    /// </remarks>
    public static bool UsesSmartSnap(AnnotationTool tool) => tool == AnnotationTool.Marker;

    /// <summary>
    /// Whether the pen-pressure option applies. Freeform tools only: a shape dragged out
    /// by two corners has no along-the-stroke for a pressure to vary over.
    /// </summary>
    public static bool UsesPressure(AnnotationTool tool) => tool == AnnotationTool.Pencil;

    /// <summary>
    /// What the size control changes for this tool, so the label can say it: the same
    /// slider means a stroke width or the size of a glyph.
    /// </summary>
    public static AnnotationSizeMeaning SizeMeaning(AnnotationTool tool) =>
        tool is AnnotationTool.Text or AnnotationTool.Number
            or AnnotationTool.Stamp or AnnotationTool.Loupe
            ? AnnotationSizeMeaning.Extent
            : AnnotationSizeMeaning.Thickness;

    /// <summary>Tools that put a mark on the image at all.</summary>
    private static bool Draws(AnnotationTool tool) => tool
        is not (AnnotationTool.Select or AnnotationTool.Crop or AnnotationTool.ColorSampler
        or AnnotationTool.TranslateOverlay);

    private static bool IsRegionEffect(AnnotationTool tool) => tool is AnnotationTool.Censor;

    /// <summary>
    /// The tool that lights a region by taking everything else down, rather than by
    /// putting anything of its own over it.
    /// </summary>
    private static bool IsSpotlight(AnnotationTool tool) => tool is AnnotationTool.Highlight;
}

/// <summary>What the one size control means for the tool in hand.</summary>
public enum AnnotationSizeMeaning
{
    /// <summary>How thick the stroke is.</summary>
    Thickness,

    /// <summary>How big the mark is: a label, a badge, a stamp, a magnifier.</summary>
    Extent,
}
