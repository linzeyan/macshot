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
/// macshot's default is its video editor. The port has none yet, so
/// <see cref="ShowInFolder"/> is the default here: a recording nobody can find is a
/// recording that did not happen, and pointing at the file is the answer that needs no
/// window.
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
    /// Not one of macshot's three, and here because the port's third is missing: macshot
    /// offers its video editor and this build has none. Doing nothing is the honest
    /// version of that, rather than a menu entry that opens nothing.
    /// </remarks>
    DoNothing,
}
