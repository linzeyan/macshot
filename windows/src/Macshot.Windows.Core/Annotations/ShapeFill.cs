namespace Macshot.Windows.Core.Annotations;

/// <summary>
/// How a rectangle or an ellipse is painted: as an outline, as a solid, or as both.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>RectFillStyle</c> (<c>Annotation.swift:74–78</c>), and the reason it has
/// three rather than a fill switch: the middle one is a translucent wash under a
/// full-strength line, which is how a region is pointed at without hiding what is in it.
/// A boolean cannot say that.
/// </para>
/// <para>
/// The names are macshot's own so the segment labels translate through the same keys.
/// </para>
/// </remarks>
public enum ShapeFill
{
    /// <summary>The line around it, and nothing inside.</summary>
    Stroke,

    /// <summary>The line, over a wash at half the colour's alpha.</summary>
    StrokeAndFill,

    /// <summary>Solid, in the colour at its own alpha, with no line of its own.</summary>
    Fill,
}
