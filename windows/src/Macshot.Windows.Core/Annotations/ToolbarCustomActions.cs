namespace Macshot.Windows.Core.Annotations;

/// <summary>
/// A toolbar button the user is allowed to take off.
/// </summary>
/// <param name="Id">
/// What the setting is stored under. macshot's own identifier rather than the command's
/// name, so a settings file written by either product hides the same button.
/// </param>
/// <param name="Label">
/// How the settings window names it, which is not always how the toolbar does: the strip
/// says "Pin on top" over a picture, and the list has to say what it is with no picture
/// to help — macshot's <c>settingsLabel</c>.
/// </param>
public readonly record struct ToolbarCustomAction(string Id, string Label, ToolbarCommand Command);

/// <summary>
/// Which of the two strips' buttons can be hidden, and under what name.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>ToolbarCustomAction.bottomSettingsActions</c> and
/// <c>rightSettingsActions</c>. Not everything on a strip is here, and that is macshot's
/// choice rather than an omission: Copy, Save, Undo and the tools themselves are what the
/// toolbar is for, and a toolbar that can lose Copy is one a user can break.
/// </para>
/// <para>
/// Both lists are macshot's, in macshot's order — <c>bottomSettingsActions</c> and
/// <c>rightSettingsActions</c> (<c>ToolbarDefinitions.swift:99</c>). Redact is macshot's
/// <c>autoRedact</c> and belongs to the right strip, between OCR and Translate; this port
/// drew it at the end of the tool row and listed it under the bottom heading to match,
/// which made the two products disagree about where a button lives rather than about where
/// it is listed.
/// </para>
/// <para>
/// Upload is here, and is the one entry whose button the offline build does not draw at
/// all — hiding something already absent costs nothing, and a settings file that crossed
/// between the variants would otherwise mean different things in each. Remove Background
/// is listed on every machine for the same reason, though only some can carry it out:
/// what a machine can do is not something a settings file should disagree about.
/// </para>
/// </remarks>
public static class ToolbarCustomActions
{
    /// <summary>The hideable buttons at the end of the tool row, in that row's order.</summary>
    public static IReadOnlyList<ToolbarCustomAction> Bottom { get; } =
    [
        new("invertColors", "Invert Colors", ToolbarCommand.InvertColors),
        new("effects", "Adjust (Image Effects)", ToolbarCommand.Adjust),
        new("beautify", "Beautify", ToolbarCommand.Beautify),
        new("removeBackground", "Remove Background", ToolbarCommand.RemoveBackground),
    ];

    /// <summary>The hideable buttons on the action strip, in that strip's order.</summary>
    public static IReadOnlyList<ToolbarCustomAction> Right { get; } =
    [
        new("upload", "Upload", ToolbarCommand.Upload),
        new("pin", "Pin (floating window)", ToolbarCommand.Pin),
        new("ocr", "OCR & QR", ToolbarCommand.ReadText),
        new("translate", "Translate", ToolbarCommand.Translate),
        new("record", "Record screen", ToolbarCommand.Record),
        new("scrollCapture", "Scroll Capture", ToolbarCommand.ScrollCapture),
        new("share", "Share", ToolbarCommand.Share),
    ];

    /// <summary>Both lists, bottom first, as the settings page reads them.</summary>
    public static IReadOnlyList<ToolbarCustomAction> All { get; } = [.. Bottom, .. Right];

    /// <summary>
    /// Whether <paramref name="command"/> should be drawn, given what is hidden.
    /// </summary>
    /// <remarks>
    /// A command nobody can hide is always drawn, which is what makes this safe to ask
    /// about every button on a strip rather than only about the ones in these lists.
    /// </remarks>
    public static bool IsShown(ToolbarCommand command, IReadOnlyCollection<string>? hidden)
    {
        if (hidden is null || hidden.Count == 0)
        {
            return true;
        }

        foreach (var action in All)
        {
            if (action.Command == command)
            {
                return !hidden.Contains(action.Id);
            }
        }

        return true;
    }

    /// <summary>The action stored under <paramref name="id"/>, or null for a name nothing knows.</summary>
    public static ToolbarCustomAction? Find(string id)
    {
        foreach (var action in All)
        {
            if (string.Equals(action.Id, id, StringComparison.Ordinal))
            {
                return action;
            }
        }

        return null;
    }
}
