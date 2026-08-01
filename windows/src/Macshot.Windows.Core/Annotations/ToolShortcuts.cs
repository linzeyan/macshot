namespace Macshot.Windows.Core.Annotations;

/// <summary>
/// One thing the overlay and the editor can be told to do with a single keypress.
/// </summary>
/// <param name="Id">
/// What the setting is stored under. macshot's own identifier, so a settings file written
/// by either product means the same thing to the other.
/// </param>
/// <param name="DefaultKey">
/// The key macshot ships it on, lower case, or empty for the ones it ships unbound.
/// </param>
public readonly record struct ToolShortcut(
    string Id,
    string Label,
    string DefaultKey,
    ToolbarCommand Command,
    AnnotationTool? Tool = null);

/// <summary>
/// The single-key shortcuts that work while the overlay or the editor has the keyboard.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>ToolShortcutManager</c>, with its identifiers and its defaults. Single
/// keys rather than combinations on purpose: the overlay is modal and has the keyboard to
/// itself, so there is nothing for a plain letter to collide with — and reaching for a
/// modifier is the difference between a tool you switch to and one you go and click.
/// </para>
/// <para>
/// Two of macshot's are missing here and neither is an oversight: Remove Background has
/// no feature behind it, and Highlight has no renderer, so it is not on the toolbar
/// either. A key bound to something that is not on the strip is a key that appears to do
/// nothing. They come back with the features, and a test fails if one is bound before it
/// is drawable.
/// </para>
/// <para>
/// Upload is here unconditionally, as Translate is, even though the offline build has no
/// uploader. Core is compiled once for both variants, so the list cannot branch on the
/// variant; the settings page leaves the row out of that build instead.
/// </para>
/// </remarks>
public static class ToolShortcuts
{
    /// <summary>What a shortcut set to nothing is stored as, and shown as.</summary>
    public const string Unbound = "";

    /// <summary>
    /// Every shortcut, in macshot's order: the tools first, then what to do with the
    /// capture.
    /// </summary>
    public static IReadOnlyList<ToolShortcut> All { get; } =
    [
        new("pencil", "Pencil", "p", ToolbarCommand.PickTool, AnnotationTool.Pencil),
        new("arrow", "Arrow", "a", ToolbarCommand.PickTool, AnnotationTool.Arrow),
        new("line", "Line", "l", ToolbarCommand.PickTool, AnnotationTool.Line),
        new("rectangle", "Rectangle", "r", ToolbarCommand.PickTool, AnnotationTool.Rectangle),
        new("ellipse", "Ellipse", "o", ToolbarCommand.PickTool, AnnotationTool.Ellipse),
        new("marker", "Marker", "m", ToolbarCommand.PickTool, AnnotationTool.Marker),
        new("text", "Text", "t", ToolbarCommand.PickTool, AnnotationTool.Text),
        new("number", "Number", "n", ToolbarCommand.PickTool, AnnotationTool.Number),
        new("censor", "Censor", "b", ToolbarCommand.PickTool, AnnotationTool.Censor),
        new("colorSampler", "Color Picker", "i", ToolbarCommand.PickTool, AnnotationTool.ColorSampler),
        new("stamp", "Stamp", "g", ToolbarCommand.PickTool, AnnotationTool.Stamp),
        new("measure", "Measure", Unbound, ToolbarCommand.PickTool, AnnotationTool.Measure),
        new("loupe", "Loupe", Unbound, ToolbarCommand.PickTool, AnnotationTool.Loupe),

        new("moveSelection", "Move Selection", " ", ToolbarCommand.MoveSelection),
        new("openInEditor", "Open in Editor", "e", ToolbarCommand.OpenEditor),
        new("pin", "Pin", "f", ToolbarCommand.Pin),
        new("upload", "Upload", "u", ToolbarCommand.Upload),
        new("copy", "Copy", Unbound, ToolbarCommand.Copy),
        new("save", "Save", Unbound, ToolbarCommand.Save),
        new("ocr", "OCR & QR", Unbound, ToolbarCommand.ReadText),
        new("scrollCapture", "Scroll Capture", Unbound, ToolbarCommand.ScrollCapture),
        new("beautify", "Beautify", Unbound, ToolbarCommand.Beautify),
        new("invertColors", "Invert Colors", Unbound, ToolbarCommand.InvertColors),
        new("translate", "Translate", Unbound, ToolbarCommand.Translate),
        new("undo", "Undo", Unbound, ToolbarCommand.Undo),
        new("redo", "Redo", Unbound, ToolbarCommand.Redo),
    ];

    /// <summary>
    /// The key <paramref name="shortcut"/> is on, taking whatever the user has changed
    /// into account.
    /// </summary>
    /// <remarks>
    /// A shortcut the settings say nothing about keeps its default. An entry that is
    /// present and empty is not silence — it is the user having taken the key off, and it
    /// must not be given back.
    /// </remarks>
    public static string KeyFor(ToolShortcut shortcut, IReadOnlyDictionary<string, string>? chosen) =>
        chosen is not null && chosen.TryGetValue(shortcut.Id, out var key) ? Normalize(key) : shortcut.DefaultKey;

    /// <summary>
    /// What <paramref name="character"/> is bound to, or null for a key that means
    /// nothing here.
    /// </summary>
    /// <remarks>
    /// First match wins, in <see cref="All"/>'s order, so two things sharing a key resolve
    /// to the one macshot lists first rather than to whichever the dictionary happened to
    /// enumerate. Two things sharing a key is the user's business to fix, and silently
    /// refusing both would only hide it.
    /// </remarks>
    public static ToolShortcut? Find(string character, IReadOnlyDictionary<string, string>? chosen)
    {
        var wanted = Normalize(character);
        if (wanted.Length == 0)
        {
            return null;
        }

        foreach (var shortcut in All)
        {
            if (KeyFor(shortcut, chosen) == wanted)
            {
                return shortcut;
            }
        }

        return null;
    }

    /// <summary>
    /// How a key reads in the settings window and in a tooltip.
    /// </summary>
    /// <remarks>
    /// Upper case for a letter, because that is how a key is written on a keyboard and in
    /// every menu — and Space spelled out, since a blank between two brackets says
    /// nothing.
    /// </remarks>
    public static string Describe(string key) => Normalize(key) switch
    {
        "" => "None",
        " " => "Space",
        var other => other.ToUpperInvariant(),
    };

    /// <summary>
    /// The form a key is compared and stored in: one character, lower case.
    /// </summary>
    /// <remarks>
    /// Lower case so that Shift+P and p are the same shortcut — a user who happens to
    /// have caps lock on has not chosen a different tool. Anything longer than one
    /// character is not a key this can bind and is taken as unbound rather than stored to
    /// match nothing forever.
    /// </remarks>
    public static string Normalize(string? key) =>
        key is { Length: 1 } single ? single.ToLowerInvariant() : Unbound;
}
