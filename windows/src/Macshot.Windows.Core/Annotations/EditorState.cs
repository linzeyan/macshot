using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Annotations;

/// <summary>
/// Everything about an open capture the user can have changed, as one comparable value.
/// </summary>
/// <remarks>
/// <para>
/// The editor has to answer one question in two places: has this capture been edited since
/// it was last written down? A Done button appears when the answer is yes, and closing the
/// window asks before throwing the edits away when the answer is yes. macshot asks it once
/// and uses it for both (<c>DetachedEditorWindowController.swift:250-264</c> and
/// <c>:268-281</c>), which is why this is one value and not two predicates.
/// </para>
/// <para>
/// Compared by value, never by re-serializing the capture and comparing bytes. macshot
/// tried the bytes and records why it stopped (<c>:38-41</c>): re-encoding a PNG and
/// round-tripping floats through JSON are not stable, so a window with nothing done to it
/// asked "Save changes?" on the way out — and a prompt that appears when there is nothing
/// to save is one people learn to dismiss without reading.
/// </para>
/// <para>
/// Three components, because the editor has three ways to change a capture and no more.
/// Marks are drawn, moved and deleted, which moves <see cref="UndoDepth"/>. Crop, flip,
/// frame and add-capture replace the pixels, which cannot be an undo step of its own —
/// they flatten the marks — and so are counted separately in
/// <see cref="ImageOperations"/>. The adjust sliders are neither: they are a layer over the
/// image that the delivered pixels are taken through, so the options themselves are the
/// state. macshot splits it the same way, into an undo depth and a
/// <c>CaptureEditState</c>.
/// </para>
/// </remarks>
/// <param name="UndoDepth">
/// <see cref="AnnotationDocument.UndoDepth"/> at the moment this was taken.
/// </param>
/// <param name="ImageOperations">
/// How many operations that replaced the pixels have been applied and not undone.
/// </param>
/// <param name="Effects">What the adjust sliders are asking for.</param>
public readonly record struct EditorState(
    int UndoDepth,
    int ImageOperations,
    ImageEffectsOptions Effects)
{
    /// <summary>
    /// Whether anything has been changed since <paramref name="saved"/> was taken.
    /// </summary>
    /// <remarks>
    /// Not "is later than": an edit undone back to where it started is not an edit, and a
    /// window whose marks have all been taken off again must close without asking. Plain
    /// inequality is what gives that, and it is the reason the depth is compared rather
    /// than a counter that only ever climbs.
    /// </remarks>
    public bool DiffersFrom(EditorState saved) => this != saved;
}
