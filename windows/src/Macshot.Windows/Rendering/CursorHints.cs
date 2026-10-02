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

    /// <summary>The cursor for what a press on the annotation canvas would do.</summary>
    public static InputSystemCursorShape For(PointerCursor cursor) => cursor switch
    {
        PointerCursor.Arrow => InputSystemCursorShape.Arrow,
        PointerCursor.Grab => InputSystemCursorShape.SizeAll,
        PointerCursor.ResizeFalling => InputSystemCursorShape.SizeNorthwestSoutheast,
        PointerCursor.ResizeRising => InputSystemCursorShape.SizeNortheastSouthwest,
        PointerCursor.ResizeVertical => InputSystemCursorShape.SizeNorthSouth,
        PointerCursor.ResizeHorizontal => InputSystemCursorShape.SizeWestEast,
        _ => InputSystemCursorShape.Cross,
    };
}
