namespace Macshot.Windows.Core.Annotations;

/// <summary>
/// What the ends of an arrow look like.
/// </summary>
/// <remarks>
/// <para>
/// The four here are the ones that say something different: a solid head, a drawn one,
/// a head at each end for a mark that ties two things together, and a bar across the
/// tail for a mark that has to say where it starts as well as where it points.
/// </para>
/// <para>
/// macOS also offers a thick arrow. It is left out because the width slider is already
/// on the toolbar and does the same thing more finely — an option that duplicates a
/// control beside it teaches the user that neither is worth reading.
/// </para>
/// </remarks>
public enum ArrowStyle
{
    /// <summary>A filled triangle at the far end. What an arrow looks like.</summary>
    Filled,

    /// <summary>
    /// The whole arrow as one solid shape: a shaft that widens from its tail into a
    /// broad head. macshot's <c>thick</c>.
    /// </summary>
    /// <remarks>
    /// Second, where macshot puts it. The order is the order of the segments in the
    /// toolbar, and a style appended to the end of this enum would sit in a different
    /// place in the two products' pickers.
    /// </remarks>
    Banner,

    /// <summary>Two strokes at the far end, so the head is drawn rather than solid.</summary>
    Open,

    /// <summary>A filled head at both ends, for a mark that joins two things.</summary>
    Double,

    /// <summary>A filled head at the far end and a bar across the near one.</summary>
    Tail,
}
