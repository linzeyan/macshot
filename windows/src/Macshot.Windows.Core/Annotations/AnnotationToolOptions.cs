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
    /// Whether the mark is drawn in the chosen colour. False for the two tools that
    /// rewrite the pixels they cover rather than drawing over them, and for the pointer,
    /// which draws nothing at all.
    /// </summary>
    public static bool UsesColor(AnnotationTool tool) =>
        Draws(tool) && !IsRegionEffect(tool);

    /// <summary>
    /// Whether the width slider does anything. It does for every mark, though not always
    /// as a width: it is the size of a badge or a label, and the strength of a pixelate
    /// or a blur.
    /// </summary>
    public static bool UsesSize(AnnotationTool tool) => Draws(tool);

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

    /// <summary>Whether the arrow-ends picker applies.</summary>
    public static bool UsesArrowStyle(AnnotationTool tool) => tool == AnnotationTool.Arrow;

    /// <summary>Whether the emoji picker applies.</summary>
    public static bool UsesStamp(AnnotationTool tool) => tool == AnnotationTool.Stamp;

    /// <summary>
    /// What the size control changes for this tool, so the label can say it: the same
    /// slider means a stroke width, a glyph size, or the coarseness of an effect.
    /// </summary>
    public static AnnotationSizeMeaning SizeMeaning(AnnotationTool tool)
    {
        if (IsRegionEffect(tool))
        {
            return AnnotationSizeMeaning.Strength;
        }

        return tool is AnnotationTool.Text or AnnotationTool.Number
            or AnnotationTool.Stamp or AnnotationTool.Loupe
            ? AnnotationSizeMeaning.Extent
            : AnnotationSizeMeaning.Thickness;
    }

    /// <summary>Tools that put a mark on the image at all.</summary>
    private static bool Draws(AnnotationTool tool) => tool
        is not (AnnotationTool.Select or AnnotationTool.Crop or AnnotationTool.ColorSampler
        or AnnotationTool.TranslateOverlay);

    private static bool IsRegionEffect(AnnotationTool tool) =>
        tool is AnnotationTool.Pixelate or AnnotationTool.Blur;
}

/// <summary>What the one size control means for the tool in hand.</summary>
public enum AnnotationSizeMeaning
{
    /// <summary>How thick the stroke is.</summary>
    Thickness,

    /// <summary>How big the mark is: a label, a badge, a stamp, a magnifier.</summary>
    Extent,

    /// <summary>How coarse the effect is: the pixelate block, the blur radius.</summary>
    Strength,
}
