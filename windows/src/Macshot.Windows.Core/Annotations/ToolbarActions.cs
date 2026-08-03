using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Core.Annotations;

/// <summary>What a toolbar button does.</summary>
public enum ToolbarCommand
{
    /// <summary>Take the tool named by <see cref="ToolbarItem.Tool"/>.</summary>
    PickTool,

    /// <summary>Open the colour picker. The button is the current colour.</summary>
    PickColor,

    Undo,
    Redo,

    /// <summary>Throw the capture away.</summary>
    Cancel,

    /// <summary>Drag the chosen region without redrawing it.</summary>
    MoveSelection,

    /// <summary>Carry on with this capture in the editor window.</summary>
    OpenEditor,

    Copy,
    Save,

    /// <summary>
    /// Ask where to put it instead of using the folder the preferences name. Never on a
    /// strip: it is what the right-click on Save offers, the way macshot offers it.
    /// </summary>
    SaveAs,

    /// <summary>Hand it to another program through the system's share pane.</summary>
    Share,

    /// <summary>Send it to the destination the preferences name, and copy the link.</summary>
    Upload,

    /// <summary>Leave it on top of everything as a floating window.</summary>
    Pin,

    /// <summary>Recognize the text in it.</summary>
    ReadText,

    /// <summary>Cover the personal details in it.</summary>
    Redact,

    /// <summary>
    /// Cover every line of text found in the region, rather than only what looks like a
    /// secret.
    /// </summary>
    /// <remarks>
    /// Its own command rather than a mode of <see cref="Redact"/>, because the two answer
    /// different questions. One asks the machine to decide what is sensitive; this is what
    /// is used when the answer is already known to be "all of it" — a whole panel of
    /// somebody else's data, where naming the kinds would be work the user should not have
    /// to do, and where a pattern that missed one is a leak.
    /// </remarks>
    RedactAllText,

    /// <summary>
    /// Cover every face found in the region.
    /// </summary>
    /// <remarks>
    /// Its own command beside the two that read words, because a face is not text and no
    /// amount of pattern-matching over a transcript finds one. It is the redaction asked
    /// for most often on a screenshot of a meeting, and the one the text passes cannot do
    /// at all.
    /// </remarks>
    RedactFaces,

    /// <summary>Cover every person found in the region, and not only their face.</summary>
    /// <remarks>
    /// Not a wider <see cref="RedactFaces"/>: someone is identifiable from a uniform, a
    /// badge or a tattoo with their face already covered, and that is the case this exists
    /// for. macshot answers it with a human-rectangles pass; Windows has no such thing in
    /// the box, so this leans on the same subject model the Remove Background button does.
    /// </remarks>
    RedactPeople,

    /// <summary>Lay a translation over the text in it.</summary>
    Translate,

    /// <summary>Turn every colour in it to its opposite.</summary>
    InvertColors,

    /// <summary>Open the brightness, contrast, saturation and sharpness controls.</summary>
    Adjust,

    /// <summary>Put it on a gradient background.</summary>
    Beautify,

    /// <summary>Cut whatever is in front out of what is behind it.</summary>
    RemoveBackground,

    /// <summary>Scroll what is behind the region and stitch the whole of it.</summary>
    ScrollCapture,

    /// <summary>Record the region as video rather than taking a still of it.</summary>
    Record,

    /// <summary>Begin recording the region that has been chosen.</summary>
    StartRecording,

    /// <summary>Leave recording setup without having recorded anything.</summary>
    CancelRecording,

    /// <summary>Ring every click while the recording runs.</summary>
    MouseHighlight,

    /// <summary>Show what is being typed while the recording runs.</summary>
    ShowKeystrokes,

    /// <summary>Take what the machine is playing into the recording.</summary>
    SystemAudio,

    /// <summary>Take what the microphone hears into the recording.</summary>
    MicAudio,

    /// <summary>Put the camera in a corner of the recording.</summary>
    Webcam,

    /// <summary>Open the recording preferences.</summary>
    RecordingSettings,
}

/// <summary>One button on a toolbar strip.</summary>
/// <param name="Tool">
/// Which tool the button takes, and null for every button that is not a tool. Nullable
/// rather than defaulted to one of them, because a default would make every action
/// button claim to be that tool.
/// </param>
/// <param name="Shortcut">
/// The key this button also answers to, ready to read — or empty for none, and for the
/// user who has turned the hint off. Carried on the item rather than looked up when the
/// tooltip is built, so that the strips stay the one place that knows about settings.
/// </param>
public readonly record struct ToolbarItem(
    ToolbarCommand Command,
    string Tooltip,
    AnnotationTool? Tool = null,
    bool IsSelected = false,
    string Shortcut = "");

/// <summary>
/// Which buttons each strip carries, and in what order.
/// </summary>
/// <remarks>
/// <para>
/// The order is macshot's own, not a fresh reading of what looks tidy: someone who
/// knows where the arrow is on macOS must find it in the same place here. It is data
/// rather than markup so that both the overlay and the editor build their strips from
/// one list, and so the order can be asserted in a test rather than compared by eye
/// against a screenshot.
/// </para>
/// <para>
/// Tools come from <see cref="AnnotationRasterizer.SupportedTools"/> filtered through
/// this order, so a tool the renderer cannot draw can never reach the strip, and a tool
/// added to the renderer without being placed here is caught by a test rather than
/// appearing at the end of the row.
/// </para>
/// </remarks>
public static class ToolbarActions
{
    /// <summary>
    /// The tools, in the order macshot shows them. The pointer tool is deliberately
    /// absent: a click grabs a mark whatever tool is in hand, so a button for it would
    /// be a button for something that already happens.
    /// </summary>
    public static IReadOnlyList<AnnotationTool> ToolOrder { get; } =
    [
        AnnotationTool.Pencil,
        AnnotationTool.Line,
        AnnotationTool.Arrow,
        AnnotationTool.Rectangle,
        AnnotationTool.Ellipse,
        AnnotationTool.Marker,
        AnnotationTool.Text,
        AnnotationTool.Number,

        AnnotationTool.Censor,
        AnnotationTool.Highlight,
        AnnotationTool.Loupe,
        AnnotationTool.Stamp,
        AnnotationTool.ColorSampler,
        AnnotationTool.Measure,
    ];

    /// <summary>
    /// The tool strip: every drawable tool, then the colour, then undo and redo.
    /// </summary>
    /// <param name="selected">The tool in hand, drawn in the accent colour.</param>
    /// <param name="enabled">
    /// Which tools the user has kept, or null for all of them.
    /// </param>
    /// <param name="beautified">
    /// Whether the capture is already set to be framed, which lights the Beautify button
    /// the way macshot tints its own.
    /// </param>
    /// <param name="inverted">
    /// Whether the capture's colours are already turned, which lights that button for
    /// the same reason: both are switches, and a switch that does not show its state is
    /// a button that appears to do nothing the second time it is pressed.
    /// </param>
    /// <param name="adjusted">
    /// Whether the Adjust controls are asking for anything. Lit for the same reason
    /// again: the popover is closed most of the time, and this is the only sign that
    /// what is on show has been altered.
    /// </param>
    /// <param name="hiddenActions">
    /// The identifiers of the buttons after the tools that the user has taken off — see
    /// <see cref="ToolbarCustomActions"/>. Null for none.
    /// </param>
    public static IReadOnlyList<ToolbarItem> Tools(
        AnnotationTool selected,
        IReadOnlyCollection<AnnotationTool>? enabled = null,
        bool beautified = false,
        bool inverted = false,
        bool adjusted = false,
        IReadOnlyCollection<string>? hiddenActions = null)
    {
        var items = new List<ToolbarItem>(ToolOrder.Count + 3);

        foreach (var tool in ToolOrder)
        {
            if (!AnnotationRasterizer.SupportedTools.Contains(tool) && tool != AnnotationTool.ColorSampler)
            {
                continue;
            }

            if (enabled is not null && !enabled.Contains(tool))
            {
                continue;
            }

            items.Add(new ToolbarItem(ToolbarCommand.PickTool, Tooltip(tool), tool, tool == selected));
        }

        items.Add(new ToolbarItem(ToolbarCommand.PickColor, "Color"));
        items.Add(new ToolbarItem(ToolbarCommand.Undo, "Undo"));
        items.Add(new ToolbarItem(ToolbarCommand.Redo, "Redo"));

        // Then the actions that change the picture rather than draw on it, in macshot's
        // order: invert, adjust, beautify, remove background. Redact is not among them —
        // macshot keeps it on the action strip, and it has moved there.
        Offer(new ToolbarItem(ToolbarCommand.InvertColors, "Invert Colors", IsSelected: inverted));
        Offer(new ToolbarItem(ToolbarCommand.Adjust, "Adjust", IsSelected: adjusted));
        Offer(new ToolbarItem(ToolbarCommand.Beautify, "Beautify", IsSelected: beautified));
        Offer(new ToolbarItem(ToolbarCommand.RemoveBackground, "Remove Background"));

        return items;

        void Offer(ToolbarItem item)
        {
            if (ToolbarCustomActions.IsShown(item.Command, hiddenActions))
            {
                items.Add(item);
            }
        }
    }

    /// <summary>
    /// The action strip: what to do with the capture, in the order macshot lists them.
    /// </summary>
    /// <param name="editorMode">
    /// True in the editor window, where there is no region to cancel or move and the
    /// capture is already open.
    /// </param>
    /// <param name="translation">
    /// False in the offline build, which contains no translator at all. A button for a
    /// feature compiled out of the binary would be a button that does nothing.
    /// </param>
    /// <param name="upload">
    /// False in the offline build, for the same reason and with more force: the whole
    /// point of that variant is that nothing can leave the machine, so the button that
    /// would send it must not be drawn at all.
    /// </param>
    /// <param name="hiddenActions">
    /// The identifiers of the buttons the user has taken off — see
    /// <see cref="ToolbarCustomActions"/>. Null for none. Cancel, Copy and Save are not in
    /// that list and so are always here: a strip that can lose Copy is one a user can
    /// break.
    /// </param>
    public static IReadOnlyList<ToolbarItem> Actions(
        bool editorMode,
        bool translation = true,
        IReadOnlyCollection<string>? hiddenActions = null,
        bool upload = true)
    {
        var items = new List<ToolbarItem>(8);

        if (!editorMode)
        {
            items.Add(new ToolbarItem(ToolbarCommand.Cancel, "Cancel"));
            items.Add(new ToolbarItem(ToolbarCommand.MoveSelection, "Move Selection"));
            items.Add(new ToolbarItem(ToolbarCommand.OpenEditor, "Open in Editor Window"));
        }

        items.Add(new ToolbarItem(ToolbarCommand.Copy, "Copy"));
        items.Add(new ToolbarItem(ToolbarCommand.Save, "Save"));

        // macshot's rightToolbarActions, in its order (ToolbarDefinitions.swift:90–97):
        // share, upload, pin, ocr, translate, scrollCapture, record. This strip was built
        // from rightSettingsActions (:103) instead, which is the list the *preferences*
        // page enumerates and is in another order — so Share sat at the end where macshot
        // has it second, and recording came before scroll capture.
        Offer(new ToolbarItem(ToolbarCommand.Share, "Share"));

        if (upload)
        {
            Offer(new ToolbarItem(ToolbarCommand.Upload, "Upload"));
        }

        Offer(new ToolbarItem(ToolbarCommand.Pin, "Pin"));
        Offer(new ToolbarItem(ToolbarCommand.ReadText, "OCR & QR"));

        // No button for the automatic redactions, because macshot draws none: its
        // makeToolbarButton answers nil for autoRedact (ToolbarDefinitions.swift:165-166)
        // and rightToolbarActions does not list it (:90-97). They are reached from the
        // censor tool's own options row, which is where both products put them — a strip
        // button this one had and that one did not was the port inventing a control.
        if (translation)
        {
            Offer(new ToolbarItem(ToolbarCommand.Translate, "Translate"));
        }

        if (!editorMode)
        {
            // Both aim at a live screen: there is no window behind an image in the editor
            // to scroll, and nothing there to record.
            Offer(new ToolbarItem(ToolbarCommand.ScrollCapture, "Scroll Capture"));
            Offer(new ToolbarItem(ToolbarCommand.Record, "Record"));
        }

        return items;

        void Offer(ToolbarItem item)
        {
            if (ToolbarCustomActions.IsShown(item.Command, hiddenActions))
            {
                items.Add(item);
            }
        }
    }

    /// <summary>
    /// The strip shown once a region has been chosen to record, before anything is
    /// being recorded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// macshot's <c>rightButtons(isRecording:)</c>, which returns early and replaces the
    /// whole action strip: Start and Cancel, then the five switches that decide what ends
    /// up in the file, then the preferences and the handle for nudging the region. The
    /// tool strip is empty here — macshot's <c>bottomButtons</c> returns nothing while
    /// recording is being set up, because there is nothing to draw on yet.
    /// </para>
    /// <para>
    /// A setup step rather than an immediate start, because every one of those five
    /// switches has to be decided <em>before</em> the recording begins and cannot be
    /// changed after: whether the microphone was on is not something a recording can be
    /// asked afterwards.
    /// </para>
    /// <para>
    /// The tooltips are macshot's own English, which is what its translations are keyed
    /// by — a paraphrase would resolve against nothing and ship untranslated.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ToolbarItem> Recording(
        bool mouseHighlight,
        bool keystrokes,
        bool systemAudio,
        bool micAudio,
        bool webcam) =>
    [
        new ToolbarItem(ToolbarCommand.StartRecording, "Start Recording"),
        new ToolbarItem(ToolbarCommand.CancelRecording, "Cancel Recording"),
        new ToolbarItem(ToolbarCommand.MouseHighlight, "Highlight Mouse Clicks", IsSelected: mouseHighlight),
        new ToolbarItem(ToolbarCommand.ShowKeystrokes, "Show Keystrokes", IsSelected: keystrokes),
        new ToolbarItem(ToolbarCommand.SystemAudio, "Record System Audio", IsSelected: systemAudio),
        new ToolbarItem(ToolbarCommand.MicAudio, "Record Microphone", IsSelected: micAudio),
        new ToolbarItem(ToolbarCommand.Webcam, "Webcam Overlay", IsSelected: webcam),
        new ToolbarItem(ToolbarCommand.RecordingSettings, "Recording Settings"),
        new ToolbarItem(ToolbarCommand.MoveSelection, "Move Selection"),
    ];

    /// <summary>The name shown when the pointer rests on a tool's button.</summary>
    /// <remarks>
    /// Word for word the names macshot's own settings window lists
    /// (<c>SettingsWindowController.swift:1465–1470</c>), because these strings are the
    /// keys its translations are filed under. A name written afresh here — "Magnify" for
    /// "Magnify (Loupe)", or a sentence where macshot has two words — matches nothing in
    /// the forty translated files and shows English to everyone who is not reading in it.
    /// </remarks>
    public static string Tooltip(AnnotationTool tool) => tool switch
    {
        AnnotationTool.Pencil => "Pencil",
        AnnotationTool.Line => "Line",
        AnnotationTool.Arrow => "Arrow",
        AnnotationTool.Rectangle => "Rectangle",
        AnnotationTool.Ellipse => "Ellipse",
        AnnotationTool.Marker => "Marker",
        AnnotationTool.Text => "Text",
        AnnotationTool.Number => "Number / Counter",
        AnnotationTool.Censor => "Censor",
        AnnotationTool.Highlight => "Highlight (Spotlight)",
        AnnotationTool.Loupe => "Magnify (Loupe)",
        AnnotationTool.Stamp => "Stamp / Emoji",
        AnnotationTool.ColorSampler => "Color Picker",
        AnnotationTool.Measure => "Measure",
        AnnotationTool.Select => "Select",
        _ => tool.ToString(),
    };
}
