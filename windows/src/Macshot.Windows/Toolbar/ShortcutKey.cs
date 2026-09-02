using Macshot.Windows.Core.Input;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace Macshot.Windows.Toolbar;

/// <summary>
/// Reads the keyboard the way a shortcut is stored: the character of the key, and the
/// modifiers held with it.
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

    /// <summary>
    /// The modifiers down right now.
    /// </summary>
    /// <remarks>
    /// Read from the keyboard rather than from the event, because
    /// <c>KeyRoutedEventArgs</c> carries no modifier state — which is why every caller
    /// needs this and why there is only one of it.
    /// </remarks>
    public static HotkeyModifiers Held()
    {
        var modifiers = HotkeyModifiers.None;
        if (IsDown(VirtualKey.Control))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (IsDown(VirtualKey.Menu))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (IsDown(VirtualKey.Shift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        // Either Windows key, and neither is reported by the combined virtual key the
        // other three have.
        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows))
        {
            modifiers |= HotkeyModifiers.Windows;
        }

        return modifiers;
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
