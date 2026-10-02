namespace Macshot.Windows.Core.Annotations;

/// <summary>
/// What the pointer says a press over the annotation canvas would do, in terms each window
/// maps to its own platform's cursors. See <see cref="AnnotationEditor.CursorAt"/>.
/// </summary>
public enum PointerCursor
{
    /// <summary>A press draws.</summary>
    Crosshair,

    /// <summary>The pointer tool over empty canvas, where a press takes hold of nothing.</summary>
    Arrow,

    /// <summary>
    /// A press takes hold of a mark, or one is being held: macshot's open and closed hand.
    /// </summary>
    /// <remarks>
    /// One value for both, because Windows has no hand that grips: its only hand points,
    /// which says "link". Windows draws this as the four-way move arrow, which is what its
    /// own apps show over something that can be dragged.
    /// </remarks>
    Grab,

    /// <summary>A handle that resizes from the top-left or bottom-right corner.</summary>
    ResizeFalling,

    /// <summary>A handle that resizes from the top-right or bottom-left corner.</summary>
    ResizeRising,

    /// <summary>A handle on the top or bottom edge.</summary>
    ResizeVertical,

    /// <summary>A handle on the left or right edge.</summary>
    ResizeHorizontal,

    /// <summary>
    /// A press draws with the pencil or the marker: the crosshair, with a dot the size of
    /// what it would lay down drawn under it. See <see cref="AnnotationEditor.BrushRadius"/>.
    /// </summary>
    /// <remarks>
    /// macshot hides the pointer and lets the dot alone stand for it
    /// (<c>OverlayView.swift:1405-1411</c>). A WinUI window cannot: there is no empty
    /// system shape, every other cursor has to come from a Win32 resource this app does
    /// not carry, and ShowCursor was measured not to reach it — the count went below zero
    /// and the pointer stayed on screen.
    /// </remarks>
    Brush,
}
