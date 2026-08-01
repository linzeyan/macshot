namespace Macshot.Windows.Core.Capture;

/// <summary>The modifiers held when a key went down.</summary>
[Flags]
public enum KeystrokeModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Windows = 8,
    CapsLock = 16,
}

/// <summary>
/// What the keystroke pill says.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>keyNameFromCode</c> and <c>modifierSymbols</c>, in Windows' own words. This
/// is the one place the port deliberately does not copy the Mac: macshot writes ⌘⌥⌃⇧,
/// and those keys are not on the keyboard the viewer of a Windows recording is looking at.
/// A pill saying "⌘ C" over a Windows screen recording teaches the wrong shortcut, which is
/// the exact opposite of what the feature is for.
/// </para>
/// <para>
/// Everything else follows macshot: the same order (control, alt, shift, then the platform
/// key), the same uppercasing, the same silence for a modifier pressed on its own unless
/// every keystroke is being shown.
/// </para>
/// </remarks>
public static class KeystrokeNames
{
    /// <summary>
    /// Between the parts. macshot separates its glyphs with a space, which reads as one
    /// symbol; spelled-out names need the plus sign Windows itself writes shortcuts with.
    /// </summary>
    public const string Separator = " + ";

    /// <summary>
    /// Whether this key is only ever a modifier, and so has no name of its own. Both the
    /// generic codes Windows sends and the sided ones a low-level hook sees.
    /// </summary>
    public static bool IsModifier(int virtualKey) => virtualKey switch
    {
        0x10 or 0x11 or 0x12 => true,                          // Shift, Control, Alt
        0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 => true,   // and their left/right pairs
        0x5B or 0x5C => true,                                   // the two Windows keys
        0x14 => true,                                           // Caps Lock
        _ => false,
    };

    /// <summary>
    /// The name of a key that has one whatever the layout is, or null when the layout has
    /// to be asked what the key types.
    /// </summary>
    public static string? NameFor(int virtualKey) => virtualKey switch
    {
        0x0D => "Enter",
        0x09 => "Tab",
        0x20 => "Space",
        0x08 => "Backspace",
        0x2E => "Delete",
        0x2D => "Insert",
        0x1B => "Esc",
        0x26 => "↑",
        0x28 => "↓",
        0x25 => "←",
        0x27 => "→",
        0x24 => "Home",
        0x23 => "End",
        0x21 => "Page Up",
        0x22 => "Page Down",
        >= 0x70 and <= 0x7B => $"F{virtualKey - 0x6F}",
        _ => null,
    };

    /// <summary>
    /// The modifiers on their own, which is what shows while a chord is being reached for.
    /// </summary>
    public static string DescribeModifiers(KeystrokeModifiers modifiers)
    {
        var parts = new List<string>(5);

        if (modifiers.HasFlag(KeystrokeModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(KeystrokeModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(KeystrokeModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(KeystrokeModifiers.Windows))
        {
            parts.Add("Win");
        }

        if (modifiers.HasFlag(KeystrokeModifiers.CapsLock))
        {
            parts.Add("Caps Lock");
        }

        return string.Join(Separator, parts);
    }

    /// <summary>
    /// The whole keystroke, or empty when there is nothing worth showing.
    /// </summary>
    /// <param name="typed">
    /// What the key types on the layout in force, or the null character when it types
    /// nothing. Only consulted for keys with no name of their own, and uppercased — a
    /// pill saying "ctrl + c" is a shortcut nobody writes down that way.
    /// </param>
    public static string Describe(int virtualKey, char typed, KeystrokeModifiers modifiers)
    {
        if (IsModifier(virtualKey))
        {
            return string.Empty;
        }

        var key = NameFor(virtualKey)
            ?? (typed > ' ' ? char.ToUpperInvariant(typed).ToString() : string.Empty);

        if (key.Length == 0)
        {
            return string.Empty;
        }

        // Caps Lock is left out of a chord: it is a state rather than something the user
        // is holding, and a pill saying "Caps Lock + Ctrl + C" describes a keystroke that
        // was never pressed.
        var held = DescribeModifiers(modifiers & ~KeystrokeModifiers.CapsLock);

        return held.Length == 0 ? key : held + Separator + key;
    }

    /// <summary>
    /// Whether this keystroke belongs on screen at all.
    /// </summary>
    /// <remarks>
    /// macshot's split. With every keystroke shown, a recording of somebody typing an email
    /// is a recording of that email; with only shortcuts shown, what appears is what the
    /// viewer is meant to learn. Shift alone does not make a shortcut — it is how capitals
    /// are typed — which is why it is not among the three.
    /// </remarks>
    public static bool WorthShowing(int virtualKey, KeystrokeModifiers modifiers, bool showAll)
    {
        if (showAll)
        {
            return true;
        }

        if (IsModifier(virtualKey))
        {
            return false;
        }

        return (modifiers & (KeystrokeModifiers.Control | KeystrokeModifiers.Alt | KeystrokeModifiers.Windows))
            != KeystrokeModifiers.None;
    }
}
