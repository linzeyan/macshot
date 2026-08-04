using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Microsoft.UI.Input;

namespace Macshot.Windows.Rendering;

/// <summary>
/// What the pointer should look like over each thing that can be grabbed.
/// </summary>
/// <remarks>
/// Shared by the overlay and the editor so a corner grip means the same thing in both.
/// The mapping is here rather than in either window because it is the one piece of the
/// answer neither of them owns: they know what is under the pointer, this knows what that
/// looks like.
/// </remarks>
internal static class CursorHints
{
    /// <summary>The cursor for one of the eight grips around the capture region.</summary>
    public static InputSystemCursorShape For(SelectionHandle handle) => handle switch
    {
        SelectionHandle.TopLeft or SelectionHandle.BottomRight => InputSystemCursorShape.SizeNorthwestSoutheast,
        SelectionHandle.TopRight or SelectionHandle.BottomLeft => InputSystemCursorShape.SizeNortheastSouthwest,
        SelectionHandle.Top or SelectionHandle.Bottom => InputSystemCursorShape.SizeNorthSouth,
        SelectionHandle.Left or SelectionHandle.Right => InputSystemCursorShape.SizeWestEast,

        // A pointer over the region itself, which is not a grip and not a place a drag
        // does anything.
        _ => InputSystemCursorShape.Cross,
    };

    /// <summary>The cursor for one of the grab points on a selected mark.</summary>
    public static InputSystemCursorShape For(AnnotationHandleKind kind) => kind switch
    {
        AnnotationHandleKind.TopLeft or AnnotationHandleKind.BottomRight =>
            InputSystemCursorShape.SizeNorthwestSoutheast,
        AnnotationHandleKind.TopRight or AnnotationHandleKind.BottomLeft =>
            InputSystemCursorShape.SizeNortheastSouthwest,

        // No system cursor turns or bends anything, so both of those handles borrow the
        // hand: what they have in common is that the shape follows the grip rather than a
        // corner following an axis.
        AnnotationHandleKind.Rotate
            or AnnotationHandleKind.Bend
            or AnnotationHandleKind.BendEnd => InputSystemCursorShape.Hand,

        // An end of a line goes anywhere, so no single axis describes it.
        _ => InputSystemCursorShape.SizeAll,
    };
}
