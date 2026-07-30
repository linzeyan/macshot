namespace Macshot.Windows.Core.Annotations;

/// <summary>
/// The three colours the toolbar is drawn from, and the ones derived from them.
/// </summary>
/// <param name="Background">The strip itself.</param>
/// <param name="Accent">The tool in hand, and the selection chrome that matches it.</param>
/// <param name="Icon">The icons, and any text that has no icon.</param>
/// <remarks>
/// Three, not thirty. Hover and press are worked out from the other two rather than set,
/// because a palette where they can disagree is a palette someone can make unreadable —
/// and the toolbar is over a screenshot, where unreadable means invisible.
/// </remarks>
public readonly record struct ToolbarColors(
    AnnotationColor Background,
    AnnotationColor Accent,
    AnnotationColor Icon)
{
    /// <summary>Near-black, opaque, so icons read over any capture.</summary>
    public static AnnotationColor DefaultBackground { get; } = new(31, 31, 31);

    /// <summary>macshot's purple.</summary>
    public static AnnotationColor DefaultAccent { get; } = new(140, 77, 217);

    public static AnnotationColor DefaultIcon { get; } = new(255, 255, 255);

    public static ToolbarColors Default { get; } = new(DefaultBackground, DefaultAccent, DefaultIcon);

    /// <summary>A button under the pointer: the icon colour at a twelfth.</summary>
    public AnnotationColor Hover => Icon with { Alpha = 31 };

    /// <summary>A button being pressed: the accent, softened.</summary>
    public AnnotationColor Pressed => Accent with { Alpha = 153 };
}
