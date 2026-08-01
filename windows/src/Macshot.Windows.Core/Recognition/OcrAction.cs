namespace Macshot.Windows.Core.Recognition;

/// <summary>
/// What reading a region does with what it read.
/// </summary>
/// <remarks>
/// macshot's <c>ocrAction</c>, in its order. The three answers exist because the two
/// halves — a window to read and correct the text in, and the text on the clipboard —
/// are wanted separately as often as together: someone grabbing a serial number off a
/// dialog wants only the clipboard, and someone reading a paragraph out of an image
/// wants to see what was read before they trust it.
/// </remarks>
public enum OcrAction
{
    /// <summary>Both, which is what someone who has not chosen expects.</summary>
    ShowAndCopy,

    ShowOnly,

    CopyOnly,
}
