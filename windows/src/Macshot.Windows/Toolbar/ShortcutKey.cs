using Windows.System;

namespace Macshot.Windows.Toolbar;

/// <summary>
/// Turns a key press into the character a single-key shortcut is stored as.
/// </summary>
/// <remarks>
/// Shared by the two ends of the feature on purpose: the settings window writes what this
/// says, and the overlay looks up what this says. Two separate readings of a keyboard
/// would eventually disagree, and the shortcut that stopped working would be the one
/// nobody could explain.
/// </remarks>
internal static class ShortcutKey
{
    /// <summary>
    /// What <paramref name="key"/> produces, or empty for a key nothing can be bound to.
    /// </summary>
    /// <remarks>
    /// Read off the virtual key rather than the character the keyboard produced, so that a
    /// dead key or a composing IME cannot arrive as a letter — the overlay is not a text
    /// field, and the same physical key must pick the same tool whatever else is being
    /// typed. Letters, digits and Space, which is every key macshot binds.
    /// </remarks>
    public static string Of(VirtualKey key) => key switch
    {
        VirtualKey.Space => " ",
        >= VirtualKey.A and <= VirtualKey.Z => ((char)('a' + (key - VirtualKey.A))).ToString(),
        >= VirtualKey.Number0 and <= VirtualKey.Number9 => ((char)('0' + (key - VirtualKey.Number0))).ToString(),
        _ => string.Empty,
    };
}
