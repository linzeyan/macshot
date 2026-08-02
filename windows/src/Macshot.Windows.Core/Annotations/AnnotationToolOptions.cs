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
    /// Whether the mark is drawn as a stroke, and so takes the dash pattern. A fill, a
    /// region effect and a sprite each ignore it.
    /// </summary>
    public static bool UsesLineStyle(AnnotationTool tool) => tool
        is AnnotationTool.Pencil
        or AnnotationTool.Line
        or AnnotationTool.Arrow
        or AnnotationTool.Marker
        or AnnotationTool.Highlight
        or AnnotationTool.Measure
        or AnnotationTool.Loupe
        or AnnotationTool.Rectangle
        or AnnotationTool.Ellipse;

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
