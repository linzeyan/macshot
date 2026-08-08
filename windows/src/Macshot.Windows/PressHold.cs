using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Microsoft.UI.Dispatching;

namespace Macshot.Windows;

/// <summary>
/// The clock behind the pencil's hold-to-select: a press that stays still long enough
/// stops drawing and takes hold of the mark underneath instead.
/// </summary>
/// <remarks>
/// <para>
/// A tap and a drag with a freehand tool both draw — a single dot is a deliberate mark —
/// so neither is left over to mean "pick this up". macshot waits 300 ms instead
/// (<c>OverlayView.swift:8309-8347</c>), and cancels the wait as soon as the pointer has
/// moved far enough to be drawing rather than resting.
/// </para>
/// <para>
/// Only the waiting is here. What a hold takes hold of, and the fact that the ink laid
/// down before it expired is thrown away, are <see cref="AnnotationEditor.LongPressed"/>'s
/// — one copy of the rules for the overlay and the editor, which own the pointer but not
/// the meaning of what it does.
/// </para>
/// </remarks>
internal sealed class PressHold
{
    private readonly AnnotationEditor _editor;
    private readonly Action _redraw;
    private readonly DispatcherQueueTimer _timer;

    private CapturePoint _from;
    private EditorModifiers _modifiers;

    /// <param name="queue">
    /// The dispatcher the host's pointer events arrive on, so the hold fires on the same
    /// thread the editor is being driven from.
    /// </param>
    /// <param name="redraw">What to call once a hold has changed what is on screen.</param>
    public PressHold(DispatcherQueue queue, AnnotationEditor editor, Action redraw)
    {
        ArgumentNullException.ThrowIfNull(queue);

        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _redraw = redraw ?? throw new ArgumentNullException(nameof(redraw));

        _timer = queue.CreateTimer();
        _timer.Interval = AnnotationEditor.HoldToSelect;
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => Expired();
    }

    /// <summary>
    /// Starts waiting on a press that drew rather than grabbing, if the tool in hand is
    /// one that selects by being held. Does nothing for every other tool, which select on
    /// the click and have nothing to wait for.
    /// </summary>
    public void Watch(CapturePoint point, EditorModifiers modifiers)
    {
        if (!_editor.SelectsByHolding(modifiers))
        {
            return;
        }

        _from = point;

        // The modifiers as they were when the press landed, not as they are when it
        // expires: the press has already been let through as a drawing gesture on the
        // strength of them, and reading them again would let a key tapped during the hold
        // change what the press had been.
        _modifiers = modifiers;
        _timer.Start();
    }

    /// <summary>
    /// Gives up on the hold once the pointer has travelled far enough to be drawing.
    /// </summary>
    /// <remarks>
    /// macshot's own slack, and the same three pixels a press has to cross before it is a
    /// shape at all (<c>OverlayView.swift:5683-5691</c>). Anything tighter and a hold
    /// would be cancelled by the tremor of holding still — which is why it is scaled, the
    /// way <see cref="AnnotationHandles.GrabRadius"/> is: this is a distance a hand fails
    /// to travel, so three frame pixels would be half the tolerance on a 200% display that
    /// it is on a 100% one.
    /// </remarks>
    public void Moved(CapturePoint point)
    {
        if (!_timer.IsRunning)
        {
            return;
        }

        var slack = AnnotationEditor.MinimumDragDistance * _editor.Scale;
        var deltaX = point.X - _from.X;
        var deltaY = point.Y - _from.Y;
        if ((deltaX * deltaX) + (deltaY * deltaY) > slack * slack)
        {
            _timer.Stop();
        }
    }

    /// <summary>Ends the wait, whatever the press turned out to be.</summary>
    public void Ended() => _timer.Stop();

    private void Expired()
    {
        if (_editor.LongPressed(_from, _modifiers))
        {
            _redraw();
        }
    }
}
