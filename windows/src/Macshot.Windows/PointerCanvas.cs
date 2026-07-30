using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace Macshot.Windows;

/// <summary>
/// A canvas that can say what the pointer looks like over it.
/// </summary>
/// <remarks>
/// <para>
/// This class exists because <c>UIElement.ProtectedCursor</c> is protected: only a
/// subclass can set it, so a plain <see cref="Canvas"/> in markup has no way to answer
/// the question. The input surfaces of the overlay and the editor are this instead.
/// </para>
/// <para>
/// The cursor matters more here than in an ordinary window. Both surfaces are one large
/// canvas that means different things in different places — drag out a region, grab a
/// grip, reshape a mark, pick a colour — and with everything drawn rather than built from
/// controls, the pointer is the only thing that can say which of those a press will do.
/// </para>
/// </remarks>
public sealed class PointerCanvas : Canvas
{
    /// <summary>
    /// One cursor per shape, kept for the life of the process. They are immutable and
    /// shared by every window on the thread; making a new one per pointer move would
    /// allocate a system resource dozens of times a second.
    /// </summary>
    private static readonly Dictionary<InputSystemCursorShape, InputCursor> Cursors = [];

    private InputSystemCursorShape? _shape;

    public void UseCursor(InputSystemCursorShape shape)
    {
        // Assigned only on a change. Setting ProtectedCursor invalidates the cursor for
        // the whole element, and pointer moves arrive far faster than the shape changes.
        if (_shape == shape)
        {
            return;
        }

        _shape = shape;
        ProtectedCursor = CursorFor(shape);
    }

    private static InputCursor CursorFor(InputSystemCursorShape shape)
    {
        if (!Cursors.TryGetValue(shape, out var cursor))
        {
            cursor = InputSystemCursor.Create(shape);
            Cursors[shape] = cursor;
        }

        return cursor;
    }
}
