namespace Macshot.Windows.Core.Input;

/// <summary>What an editor command shortcut asks the canvas to do.</summary>
public enum EditorCommand
{
    Undo,
    Redo,
}

/// <summary>
/// One editor command, and the chords it answers to out of the box.
/// </summary>
/// <param name="Id">
/// What the setting is stored under. macshot's own identifier
/// (<c>EditorCommandShortcutManager.Action.rawValue</c>), so a settings file written by
/// either product means the same thing to the other.
/// </param>
/// <param name="Defaults">
/// A list rather than one chord, because Redo ships answering to two.
/// </param>
public readonly record struct EditorCommandShortcut(
    string Id,
    string Label,
    EditorCommand Command,
    IReadOnlyList<HotkeyBinding> Defaults);

/// <summary>
/// The shortcuts for Undo and Redo, which are held down with a modifier and are the
/// user's to move.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>EditorCommandShortcutManager</c>, and deliberately separate from the
/// overlay's single-key <see cref="Annotations.ToolShortcuts"/> for the reason that file
/// gives: a plain letter is a tool you switch to, and a chord is a command you run. The
/// two lists can both name Undo without either being the other's fallback.
/// </para>
/// <para>
/// Where macOS holds Command this holds Control, which is both what ⌘Z means and what a
/// Windows user reaches for — the only place in this feature where the two products'
/// defaults could have disagreed and do not. Redo keeps both of macshot's chords,
/// Ctrl+Shift+Z and Ctrl+Y, because Windows programs are split on which one it is.
/// </para>
/// </remarks>
public static class EditorCommandShortcuts
{
    /// <summary>Both commands, in the order a press is matched against them.</summary>
    public static IReadOnlyList<EditorCommandShortcut> All { get; } =
    [
        new("undo", "Undo", EditorCommand.Undo, [new HotkeyBinding(HotkeyModifiers.Control, 'Z')]),
        new("redo", "Redo", EditorCommand.Redo,
        [
            new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Shift, 'Z'),
            new HotkeyBinding(HotkeyModifiers.Control, 'Y'),
        ]),
    ];

    /// <summary>
    /// The chords <paramref name="command"/> answers to, taking whatever the user has
    /// changed into account.
    /// </summary>
    /// <remarks>
    /// A command the settings say nothing about keeps its defaults. An entry that is
    /// present and empty is not silence — it is the user having turned the command off,
    /// and it must not be given back.
    /// </remarks>
    public static IReadOnlyList<HotkeyBinding> ChordsFor(
        EditorCommandShortcut command,
        IReadOnlyDictionary<string, string>? chosen) =>
        chosen is not null && chosen.TryGetValue(command.Id, out var text)
            ? HotkeyBinding.ParseList(text)
            : command.Defaults;

    /// <summary>
    /// What <paramref name="pressed"/> runs, or null for a chord that means nothing here.
    /// </summary>
    /// <remarks>
    /// The modifiers have to match exactly, so Ctrl+Alt+Z is not Undo — the user who
    /// bound Ctrl+Alt+Z to something else would otherwise find Undo eating it. A chord
    /// with no modifier at all is refused outright by <see cref="HotkeyBinding.IsValid"/>:
    /// the overlay is full of single-key tool shortcuts, and a bare Z here would take the
    /// key away from whichever tool stands on it.
    /// </remarks>
    public static EditorCommandShortcut? Find(
        HotkeyBinding pressed,
        IReadOnlyDictionary<string, string>? chosen)
    {
        if (!pressed.IsValid)
        {
            return null;
        }

        foreach (var command in All)
        {
            if (ChordsFor(command, chosen).Contains(pressed))
            {
                return command;
            }
        }

        return null;
    }

    /// <summary>
    /// Gives <paramref name="command"/> <paramref name="pressed"/> and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A chord can only resolve to one command, so it is taken off the other one on the
    /// way — including when the other one only held it as a default, which is what makes
    /// binding Ctrl+Y to Undo stop it also meaning Redo. The other command is then stored
    /// explicitly, because "one of the defaults, minus one" is not a state absence can
    /// express.
    /// </para>
    /// <para>
    /// A chord with no modifier is refused rather than stored: nothing would ever match
    /// it, and it would sit in the settings window looking assigned.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Bind(
        IReadOnlyDictionary<string, string>? chosen,
        EditorCommandShortcut command,
        HotkeyBinding pressed)
    {
        var next = Copy(chosen);
        if (!pressed.IsValid)
        {
            return next;
        }

        foreach (var other in All)
        {
            if (string.Equals(other.Id, command.Id, StringComparison.Ordinal))
            {
                continue;
            }

            var held = ChordsFor(other, chosen);
            var kept = held.Where(chord => chord != pressed).ToList();
            if (kept.Count != held.Count)
            {
                next[other.Id] = HotkeyBinding.Format(kept);
            }
        }

        next[command.Id] = pressed.ToString();
        return next;
    }

    /// <summary>Turns <paramref name="command"/> off: stored, and empty.</summary>
    /// <remarks>
    /// Distinct from <see cref="Reset"/>, which removes the entry so the defaults come
    /// back. Without the difference a command could never be turned off — the next read
    /// would hand back the chords the user had just taken away.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Disable(
        IReadOnlyDictionary<string, string>? chosen,
        EditorCommandShortcut command)
    {
        var next = Copy(chosen);
        next[command.Id] = string.Empty;
        return next;
    }

    /// <summary>Puts <paramref name="command"/> back the way it shipped.</summary>
    public static IReadOnlyDictionary<string, string> Reset(
        IReadOnlyDictionary<string, string>? chosen,
        EditorCommandShortcut command)
    {
        var next = Copy(chosen);
        next.Remove(command.Id);
        return next;
    }

    /// <summary>
    /// The entries worth writing down: the ones that are not what this build ships.
    /// </summary>
    /// <remarks>
    /// Storing only the differences is what lets a later version change a default and
    /// have it reach everyone who never touched that row — and an entry that is present
    /// and empty still says "the user turned this off", because an empty list differs
    /// from a default that is a chord.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Chosen(
        IReadOnlyDictionary<string, string>? chosen)
    {
        var kept = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var command in All)
        {
            if (chosen is null || !chosen.TryGetValue(command.Id, out var text))
            {
                continue;
            }

            if (!HotkeyBinding.ParseList(text).SequenceEqual(command.Defaults))
            {
                kept[command.Id] = text;
            }
        }

        return kept;
    }

    private static Dictionary<string, string> Copy(IReadOnlyDictionary<string, string>? chosen)
    {
        var next = new Dictionary<string, string>(StringComparer.Ordinal);
        if (chosen is null)
        {
            return next;
        }

        foreach (var (id, text) in chosen)
        {
            next[id] = text;
        }

        return next;
    }
}
