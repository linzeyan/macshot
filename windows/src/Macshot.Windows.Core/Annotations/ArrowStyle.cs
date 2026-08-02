namespace Macshot.Windows.Core.Annotations;

/// <summary>
/// What the ends of an arrow look like.
/// </summary>
/// <remarks>
/// macshot's six, each saying something the others do not: a solid head, a banner cut
/// from one piece, a drawn head, a head at each end for a mark that ties two things
/// together, a bar across the tail for a mark that has to say where it starts as well as
/// where it points, and one that looks drawn by hand.
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

    /// <summary>
    /// A wobbling shaft under an open chevron with uneven legs: an arrow that reads as
    /// drawn on the screenshot by hand rather than laid on it by a machine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Last, where macshot puts it. The order is the order of the segments in the toolbar,
    /// and a style inserted anywhere else would sit in a different place in the two
    /// products' pickers.
    /// </para>
    /// <para>
    /// The wobble is not random each time it is drawn — it is derived from the
    /// annotation's own id, so the same arrow comes out the same in the preview, in the
    /// delivered image, and after the file is reopened. An arrow that reshuffled itself on
    /// every render would flicker for the whole length of a drag.
    /// </para>
    /// </remarks>
    Sketchy,
}
