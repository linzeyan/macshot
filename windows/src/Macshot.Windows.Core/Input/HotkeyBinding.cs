using System.Globalization;

namespace Macshot.Windows.Core.Input;

/// <summary>The modifier keys a hotkey is held down with, as Windows numbers them.</summary>
/// <remarks>
/// The values are <c>MOD_ALT</c>, <c>MOD_CONTROL</c>, <c>MOD_SHIFT</c> and
/// <c>MOD_WIN</c> from <c>RegisterHotKey</c>. Spelled out here rather than in the
/// Windows layer so the value that is parsed, stored, and shown to the user is the
/// one that is registered, with nothing to translate in between.
/// </remarks>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008,
}

/// <summary>
/// One configurable shortcut: the modifiers, and the virtual key held with them.
/// </summary>
/// <remarks>
/// <para>
/// A virtual key rather than a character, for the same reason the macOS product uses
/// <c>keyCode</c>: the character a key produces depends on the layout, and a shortcut
/// that moves when the user switches to a Russian keyboard is broken for them.
/// </para>
/// <para>
/// The text form is what goes in the settings file and what preferences shows, so it
/// has to survive a round trip exactly. Parsing is deliberately forgiving about
/// spacing, order, and the names people actually type — <c>Ctrl</c>, <c>Control</c>,
/// <c>Win</c>, <c>Cmd</c> — because the file is meant to be hand-editable.
/// </para>
/// </remarks>
public sealed record HotkeyBinding(HotkeyModifiers Modifiers, uint Key)
{
    /// <summary>
    /// Virtual key codes for the keys whose name is not simply the character on them.
    /// </summary>
    /// <remarks>
    /// The punctuation keys are here under the character they print on a US layout, which
    /// is the only thing a virtual key code can be named after — <c>VK_OEM_1</c> is
    /// semicolon there and something else elsewhere, and Windows registers the code
    /// either way. <c>+</c> is deliberately absent: it separates the parts of a binding,
    /// so a key named <c>+</c> could not be written down.
    /// </remarks>
    private static readonly (string Name, uint Key)[] NamedKeys =
    [
        ("Backspace", 0x08),
        ("Tab", 0x09),
        ("Enter", 0x0D),
        ("Space", 0x20),
        ("PageUp", 0x21),
        ("PageDown", 0x22),
        ("End", 0x23),
        ("Home", 0x24),
        ("Left", 0x25),
        ("Up", 0x26),
        ("Right", 0x27),
        ("Down", 0x28),
        ("PrintScreen", 0x2C),
        ("Insert", 0x2D),
        ("Delete", 0x2E),
        ("F1", 0x70),
        ("F2", 0x71),
        ("F3", 0x72),
        ("F4", 0x73),
        ("F5", 0x74),
        ("F6", 0x75),
        ("F7", 0x76),
        ("F8", 0x77),
        ("F9", 0x78),
        ("F10", 0x79),
        ("F11", 0x7A),
        ("F12", 0x7B),
        (";", 0xBA),
        ("=", 0xBB),
        (",", 0xBC),
        ("-", 0xBD),
        (".", 0xBE),
        ("/", 0xBF),
        ("`", 0xC0),
        ("[", 0xDB),
        ("\\", 0xDC),
        ("]", 0xDD),
        ("'", 0xDE),
    ];

    /// <summary>
    /// The shortcuts macshot ships bound, with Control where macOS holds Command.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The keys are macshot's own (<c>HotkeySlot.defaultKeyCode</c>): X for an area, F
    /// for the screen, R for recording an area, H for the history, T for reading text,
    /// S for a quick capture. Only the modifier differs, because Command is the one part
    /// of a macOS shortcut that has no key here.
    /// </para>
    /// <para>
    /// The other six shortcuts macshot offers ship bound to nothing, and are absent here
    /// for that reason rather than by omission. Recording the whole screen is one of
    /// them: the R belongs to recording an area, which is the recording a person starts.
    /// </para>
    /// </remarks>
    public static HotkeyBinding CaptureArea { get; } =
        new(HotkeyModifiers.Control | HotkeyModifiers.Shift, 'X');

    public static HotkeyBinding CaptureAllScreens { get; } =
        new(HotkeyModifiers.Control | HotkeyModifiers.Shift, 'F');

    public static HotkeyBinding RecordArea { get; } =
        new(HotkeyModifiers.Control | HotkeyModifiers.Shift, 'R');

    public static HotkeyBinding History { get; } =
        new(HotkeyModifiers.Control | HotkeyModifiers.Shift, 'H');

    public static HotkeyBinding CaptureText { get; } =
        new(HotkeyModifiers.Control | HotkeyModifiers.Shift, 'T');

    public static HotkeyBinding QuickCapture { get; } =
        new(HotkeyModifiers.Control | HotkeyModifiers.Shift, 'S');

    /// <summary>
    /// Whether this is a shortcut Windows will actually register.
    /// </summary>
    /// <remarks>
    /// A bare key is refused rather than allowed. macshot's hotkeys are global, so one
    /// without a modifier would swallow that key in every program on the machine — and
    /// the user who set it would have no way left to type it in order to change it
    /// back.
    /// </remarks>
    public bool IsValid => Key != 0 && Modifiers != HotkeyModifiers.None;

    /// <summary>
    /// Whether writing this binding down and reading it back gives this binding again.
    /// </summary>
    /// <remarks>
    /// The settings file holds the text form, so a binding that does not survive the round
    /// trip cannot be stored — and a shortcut recorder must refuse such a key at the
    /// moment it is pressed rather than let it be saved and come back as something else.
    /// Every key Windows can report has a code; not every code has a name a person could
    /// have typed, which is the gap this closes.
    /// </remarks>
    public bool CanBeStored => TryParse(ToString(), out var parsed) && parsed == this;

    /// <summary>
    /// The form stored in the settings file and shown in preferences, always in the
    /// same modifier order so a round trip cannot rewrite what the user typed.
    /// </summary>
    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(NameOf(Key));
        return string.Join("+", parts);
    }

    /// <summary>
    /// What separates the chords of a shortcut that answers to more than one.
    /// </summary>
    /// <remarks>
    /// macshot's own separator (<c>EditorCommandShortcutManager.displayString</c>), used
    /// for storing as well as for showing: <see cref="ToString"/> never writes a space, so
    /// <c>Ctrl+/ / Ctrl+Y</c> splits back into exactly the two chords it was written from
    /// and a person editing the file by hand sees what preferences showed them.
    /// </remarks>
    public const string ListSeparator = " / ";

    /// <summary>
    /// Several bindings as one string, in the order a press is matched against them.
    /// </summary>
    public static string Format(IReadOnlyList<HotkeyBinding> bindings) =>
        string.Join(ListSeparator, bindings);

    /// <summary>
    /// Reads back what <see cref="Format"/> wrote, or nothing at all when any part of it
    /// is not a shortcut.
    /// </summary>
    /// <remarks>
    /// All or nothing on purpose. Keeping the readable half of a damaged list would leave
    /// a shortcut that half works, and the settings window would show the survivors as
    /// though that were the user's choice; returning nothing lets the caller tell a
    /// damaged value from a deliberately empty one and hand the defaults back.
    /// </remarks>
    public static IReadOnlyList<HotkeyBinding> ParseList(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var bindings = new List<HotkeyBinding>(2);
        foreach (var part in text.Split(
            ListSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryParse(part, out var binding))
            {
                return [];
            }

            bindings.Add(binding);
        }

        return bindings;
    }

    /// <summary>
    /// Reads a binding written as <c>Ctrl+Shift+X</c>, or returns false when it says
    /// nothing usable.
    /// </summary>
    public static bool TryParse(string? text, out HotkeyBinding binding)
    {
        binding = CaptureArea;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        uint key = 0;

        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToUpperInvariant())
            {
            case "CTRL":
            case "CONTROL":
                modifiers |= HotkeyModifiers.Control;
                continue;
            case "ALT":
            case "OPTION":
                modifiers |= HotkeyModifiers.Alt;
                continue;
            case "SHIFT":
                modifiers |= HotkeyModifiers.Shift;
                continue;
            case "WIN":
            case "WINDOWS":
            case "CMD":
            case "COMMAND":
                modifiers |= HotkeyModifiers.Windows;
                continue;
            }

            // Anything that is not a modifier is the key, and there may be only one.
            // A second one means the text is not a shortcut at all.
            if (key != 0 || !TryParseKey(raw, out key))
            {
                return false;
            }
        }

        var parsed = new HotkeyBinding(modifiers, key);
        if (!parsed.IsValid)
        {
            return false;
        }

        binding = parsed;
        return true;
    }

    /// <summary>
    /// The stored binding, or <paramref name="fallback"/> when the file holds
    /// something unusable — an unregistrable shortcut must not leave the user with no
    /// way to take a capture at all.
    /// </summary>
    public static HotkeyBinding ParseOrDefault(string? text, HotkeyBinding fallback) =>
        TryParse(text, out var parsed) ? parsed : fallback;

    /// <summary>
    /// The stored binding, where nothing stored means the shortcut is deliberately off.
    /// </summary>
    /// <remarks>
    /// Blank and unreadable are told apart on purpose. Half of macshot's shortcuts ship
    /// bound to nothing and any of the rest can be taken off, so blank has to mean off —
    /// falling back there would hand back the default the user just removed, and the
    /// shortcut could never be cleared. Text that is neither blank nor a shortcut is a
    /// damaged file instead, and does fall back, so one bad line cannot leave someone
    /// with no way to take a capture.
    /// </remarks>
    public static HotkeyBinding? ParseOptional(string? text, HotkeyBinding? fallback) =>
        string.IsNullOrWhiteSpace(text) ? null
            : TryParse(text, out var parsed) ? parsed
            : fallback;

    private static bool TryParseKey(string text, out uint key)
    {
        foreach (var (name, value) in NamedKeys)
        {
            if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase))
            {
                key = value;
                return true;
            }
        }

        // A single letter or digit is its own virtual key code, which is why the
        // alphanumeric keys need no table.
        if (text.Length == 1 && char.IsAsciiLetterOrDigit(text[0]))
        {
            key = char.ToUpperInvariant(text[0]);
            return true;
        }

        key = 0;
        return false;
    }

    private static string NameOf(uint key)
    {
        foreach (var (name, value) in NamedKeys)
        {
            if (value == key)
            {
                return name;
            }
        }

        return key is (>= 'A' and <= 'Z') or (>= '0' and <= '9')
            ? ((char)key).ToString()
            : key.ToString("X2", CultureInfo.InvariantCulture);
    }
}
