namespace Macshot.Windows.Core.Output;

/// <summary>
/// What happens the moment a recording stops.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>recordingOnStop</c>. A recording always goes to a file — minutes of video
/// do not belong on the clipboard and there is nowhere else to put them — so this is
/// about what happens <em>next</em>, not about where it went.
/// </para>
/// <para>
/// The order here is not macshot's — the settings menu lists macshot's three in
/// macshot's order, and maps them onto these — because these values are written into the
/// settings file by name. Renumbering them is free; renaming or removing one is not,
/// since a value the file has and this enum has not resets every other preference with
/// it.
/// </para>
/// </remarks>
public enum RecordingOnStop
{
    /// <summary>
    /// Open the folder with the recording picked out — macshot's "Show in Finder", by
    /// the only name it can have on this side.
    /// </summary>
    ShowInFolder,

    /// <summary>
    /// Put the file itself on the clipboard, so it can be pasted into a chat window or a
    /// mail as an attachment.
    /// </summary>
    CopyToClipboard,

    /// <summary>Leave it alone; the panel already says where it went.</summary>
    /// <remarks>
    /// Not one of macshot's three, and no longer offered: it stood in for the video
    /// editor while the port had none. Kept because settings written while it was on
    /// offer name it, and a name this enum does not know takes every other preference in
    /// the file down with it.
    /// </remarks>
    DoNothing,

    /// <summary>
    /// Open the recording in the video editor, which is macshot's own default: a
    /// recording is trimmed far more often than it is used whole.
    /// </summary>
    OpenEditor,
}
