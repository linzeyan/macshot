using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Services;

/// <summary>
/// What the user asked to happen to a finished capture.
/// </summary>
/// <remarks>
/// The toolbar's copy, save and pin buttons name one destination each, while Enter means
/// "whatever the preferences say". Without this distinction a press on Save would have to
/// change the preferences to be obeyed, and pressing Copy with auto-save switched on would
/// quietly write a file the user never asked for.
/// </remarks>
public enum CaptureOutcome
{
    /// <summary>Everything the preferences ask for: clipboard, auto-save, thumbnail.</summary>
    Deliver,

    /// <summary>The clipboard, and nothing else.</summary>
    Copy,

    /// <summary>The save folder, and nothing else.</summary>
    Save,

    /// <summary>
    /// Already written, where the user chose, by the window that asked. Nothing left to
    /// do with the pixels — but it is still a capture, so it still goes in the history.
    /// </summary>
    SaveAs,

    /// <summary>A floating window on top of everything.</summary>
    Pin,

    /// <summary>
    /// The destination the preferences name, with the link it comes back with put on the
    /// clipboard. Its own outcome rather than a variant of Copy, because what is copied
    /// is a URL and not the picture — pasting it into an image editor gives text.
    /// </summary>
    Upload,
}

/// <summary>
/// A finished capture and where its pixels were asked to go.
/// </summary>
/// <param name="Editable">
/// The pixels and marks the capture can be rebuilt from, when it can be. Carried
/// alongside the finished image rather than instead of it: what the user approved is
/// the finished image, and the pair is for archiving it in a form that can be edited
/// again. Null when the two would not reproduce it.
/// </param>
/// <param name="WindowTitle">
/// What the captured window called itself, for the <c>{window}</c> filename token. Null
/// for a capture that was dragged out rather than aimed at a window.
/// </param>
public sealed record CaptureCompletion(
    CapturedFrame Frame,
    CaptureOutcome Outcome,
    EditableCapture? Editable = null,
    string? WindowTitle = null);
