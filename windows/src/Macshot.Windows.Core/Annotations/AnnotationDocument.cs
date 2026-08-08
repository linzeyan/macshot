using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Annotations;

/// <summary>
/// The mutable annotation list behind one capture, plus its undo history.
/// </summary>
/// <remarks>
/// History is snapshot based rather than a log of typed edit entries. Because
/// <see cref="Annotation"/> is immutable, a snapshot is a cheap array of
/// references, and batch edits (auto-redact producing many annotations at once)
/// need no grouping bookkeeping to undo as one step. See
/// <c>docs/windows-port/architecture.md</c>, decision D2.
///
/// Not thread-safe by design: this is UI-thread state.
/// </remarks>
public sealed class AnnotationDocument
{
    /// <summary>Bounds memory on long editing sessions; the oldest state is dropped first.</summary>
    public const int MaxHistoryDepth = 100;

    private readonly List<Annotation> _annotations = [];
    private readonly LinkedList<Annotation[]> _undo = new();
    private readonly Stack<Annotation[]> _redo = new();

    public event EventHandler? Changed;

    public IReadOnlyList<Annotation> Annotations => _annotations;

    public bool CanUndo => _undo.Count > 0;

    /// <summary>
    /// How many steps are on the undo stack, which is how many edits deep this document
    /// is.
    /// </summary>
    /// <remarks>
    /// For telling an edited capture from an untouched one — see <see cref="EditorState"/>.
    /// Every edit pushes a step and every undo pops one, so a depth back at the number it
    /// was at is a document back at the state it was in, and drawing then undoing leaves
    /// nothing behind to prompt about. macshot compares <c>undoStack.count</c> the same way
    /// (<c>DetachedEditorWindowController.swift:250-254</c>).
    ///
    /// It saturates at <see cref="MaxHistoryDepth"/>, so an edit made past the hundredth
    /// on one capture, after a save that was also past the hundredth, does not register.
    /// That bound is this port's; the alternative is an unbounded stack of array snapshots.
    /// </remarks>
    public int UndoDepth => _undo.Count;

    public bool CanRedo => _redo.Count > 0;

    public void Add(Annotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        Commit(() => _annotations.Add(annotation));
    }

    /// <summary>Adds a batch that undo treats as a single step.</summary>
    public void AddRange(IEnumerable<Annotation> annotations)
    {
        ArgumentNullException.ThrowIfNull(annotations);

        var batch = annotations.ToArray();
        if (batch.Length == 0)
        {
            return;
        }

        if (Array.Exists(batch, annotation => annotation is null))
        {
            throw new ArgumentException("The batch contains a null annotation.", nameof(annotations));
        }

        Commit(() => _annotations.AddRange(batch));
    }

    public bool Remove(Guid id)
    {
        var index = _annotations.FindIndex(annotation => annotation.Id == id);
        if (index < 0)
        {
            return false;
        }

        Commit(() => _annotations.RemoveAt(index));
        return true;
    }

    /// <summary>
    /// Removes several annotations by id, as one undo step. False when none of them was
    /// here.
    /// </summary>
    /// <remarks>
    /// The counterpart of <see cref="AddRange"/>, for the one keystroke that can remove
    /// several marks at once: Delete over a multi-selection. One step per mark would make
    /// undoing it a sequence of keystrokes that puts the group back one member at a time.
    /// </remarks>
    public bool RemoveRange(IEnumerable<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var doomed = ids.ToHashSet();
        if (doomed.Count == 0 || !_annotations.Exists(annotation => doomed.Contains(annotation.Id)))
        {
            return false;
        }

        Commit(() => _annotations.RemoveAll(annotation => doomed.Contains(annotation.Id)));
        return true;
    }

    /// <summary>Removes every annotation sharing <paramref name="groupId"/> as one undo step.</summary>
    public bool RemoveGroup(Guid groupId)
    {
        if (!_annotations.Exists(annotation => annotation.GroupId == groupId))
        {
            return false;
        }

        Commit(() => _annotations.RemoveAll(annotation => annotation.GroupId == groupId));
        return true;
    }

    /// <summary>Swaps in an edited copy, matched by <see cref="Annotation.Id"/>.</summary>
    public bool Replace(Annotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);

        var index = _annotations.FindIndex(existing => existing.Id == annotation.Id);
        if (index < 0)
        {
            return false;
        }

        Commit(() => _annotations[index] = annotation);
        return true;
    }

    /// <summary>
    /// Swaps in edited copies of several annotations as one undo step. False unless every
    /// one of them is here to be replaced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For the one gesture that edits several marks at once: dragging a multi-selection.
    /// One <see cref="Replace(Annotation)"/> per mark would push one undo step per mark,
    /// and Ctrl+Z would then walk the group back to where it started one member at a time.
    /// </para>
    /// <para>
    /// All or nothing, because a partial move is a state the user never asked for and
    /// could not have produced by hand. If one of them has gone, the caller is working
    /// from a selection the document has moved on from.
    /// </para>
    /// </remarks>
    public bool ReplaceRange(IEnumerable<Annotation> annotations)
    {
        ArgumentNullException.ThrowIfNull(annotations);

        var batch = annotations.ToArray();
        if (batch.Length == 0)
        {
            return false;
        }

        if (Array.Exists(batch, annotation => annotation is null))
        {
            throw new ArgumentException("The batch contains a null annotation.", nameof(annotations));
        }

        var indexes = new int[batch.Length];
        for (var index = 0; index < batch.Length; index++)
        {
            indexes[index] = _annotations.FindIndex(existing => existing.Id == batch[index].Id);
            if (indexes[index] < 0)
            {
                return false;
            }
        }

        Commit(() =>
        {
            for (var index = 0; index < batch.Length; index++)
            {
                _annotations[indexes[index]] = batch[index];
            }
        });

        return true;
    }

    /// <summary>
    /// Swaps in a copy of an annotation without recording an undo step.
    /// </summary>
    /// <remarks>
    /// For finishing a mark the user has already made rather than changing one: a ruler's
    /// reading is rasterized after the drag that drew the ruler, and the reading is not a
    /// second thing to take back. Recording a step for it would make the first Ctrl+Z
    /// strip the number off a ruler and leave the ruler behind.
    /// </remarks>
    public bool Amend(Annotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);

        var index = _annotations.FindIndex(existing => existing.Id == annotation.Id);
        if (index < 0)
        {
            return false;
        }

        _annotations[index] = annotation;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Swaps one annotation for several, without recording an undo step.
    /// </summary>
    /// <remarks>
    /// The batch counterpart of <see cref="Amend(Annotation)"/>, and for the same reason:
    /// a censor region set to cover only the text inside it is one drag that resolves into
    /// several boxes once the pixels have been read. The drag already recorded its step, so
    /// the resolution must not record a second one — Ctrl+Z would otherwise put the region
    /// back and need pressing again to take it away.
    /// </remarks>
    public bool Amend(Guid id, IEnumerable<Annotation> replacements)
    {
        ArgumentNullException.ThrowIfNull(replacements);

        var index = _annotations.FindIndex(existing => existing.Id == id);
        if (index < 0)
        {
            return false;
        }

        var batch = replacements.ToArray();
        if (Array.Exists(batch, annotation => annotation is null))
        {
            throw new ArgumentException("The batch contains a null annotation.", nameof(replacements));
        }

        _annotations.RemoveAt(index);
        _annotations.InsertRange(index, batch);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Clear()
    {
        if (_annotations.Count == 0)
        {
            return false;
        }

        Commit(_annotations.Clear);
        return true;
    }

    /// <summary>
    /// Replaces the contents and forgets the history.
    /// </summary>
    /// <remarks>
    /// For when the pixels underneath have been replaced rather than drawn on — cropped,
    /// flipped, framed. Every state in the history describes marks on an image that no
    /// longer exists, and undoing into one would put them back at coordinates that have
    /// moved. Whatever it takes to undo the operation itself is the caller's to keep,
    /// which is why this does not try to be an undo step of its own.
    /// </remarks>
    public void Reset(IEnumerable<Annotation>? annotations = null)
    {
        var restored = annotations?.ToArray() ?? [];
        if (Array.Exists(restored, annotation => annotation is null))
        {
            throw new ArgumentException("The batch contains a null annotation.", nameof(annotations));
        }

        _undo.Clear();
        _redo.Clear();
        _annotations.Clear();
        _annotations.AddRange(restored);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Returns the topmost annotation under a frame-space point, or null.</summary>
    public Annotation? HitTest(CapturePoint point, double threshold = 6)
    {
        for (var index = _annotations.Count - 1; index >= 0; index--)
        {
            if (_annotations[index].HitTest(point, threshold))
            {
                return _annotations[index];
            }
        }

        return null;
    }

    public bool Undo()
    {
        if (_undo.Last is not { } previous)
        {
            return false;
        }

        _undo.RemoveLast();
        _redo.Push(_annotations.ToArray());
        Restore(previous.Value);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        PushUndoState();
        Restore(_redo.Pop());
        return true;
    }

    private void Commit(Action mutate)
    {
        PushUndoState();
        mutate();

        // A new edit invalidates any forward history, exactly like the macOS editor.
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void PushUndoState()
    {
        _undo.AddLast(_annotations.ToArray());
        if (_undo.Count > MaxHistoryDepth)
        {
            _undo.RemoveFirst();
        }
    }

    private void Restore(Annotation[] state)
    {
        _annotations.Clear();
        _annotations.AddRange(state);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
