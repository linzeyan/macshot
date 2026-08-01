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

    /// <summary>Leave it on top of everything as a floating window.</summary>
    Pin,

    /// <summary>Recognize the text in it.</summary>
    ReadText,

    /// <summary>Cover the personal details in it.</summary>
    Redact,

    /// <summary>Lay a translation over the text in it.</summary>
    Translate,

    /// <summary>Turn every colour in it to its opposite.</summary>
    InvertColors,

    /// <summary>Open the brightness, contrast, saturation and sharpness controls.</summary>
    Adjust,

    /// <summary>Put it on a gradient background.</summary>
    Beautify,

    /// <summary>Scroll what is behind the region and stitch the whole of it.</summary>
    ScrollCapture,

    /// <summary>Record the region as video rather than taking a still of it.</summary>
    Record,
}

/// <summary>One button on a toolbar strip.</summary>
/// <param name="Tool">
/// Which tool the button takes, and null for every button that is not a tool. Nullable
/// rather than defaulted to one of them, because a default would make every action
/// button claim to be that tool.
/// </param>
public readonly record struct ToolbarItem(
    ToolbarCommand Command,
    string Tooltip,
    AnnotationTool? Tool = null,
    bool IsSelected = false);

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
    public static IReadOnlyList<ToolbarItem> Tools(
        AnnotationTool selected,
        IReadOnlyCollection<AnnotationTool>? enabled = null,
        bool beautified = false,
        bool inverted = false,
        bool adjusted = false)
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

        items.Add(new ToolbarItem(ToolbarCommand.PickColor, "Colour"));
        items.Add(new ToolbarItem(ToolbarCommand.Undo, "Undo"));
        items.Add(new ToolbarItem(ToolbarCommand.Redo, "Redo"));

        // Then the actions that change the picture rather than draw on it, in macshot's
        // order: invert, adjust, beautify, remove background. Remove background is not
        // here yet; the three that are keep the places they hold there, so filling the
        // gap later moves nothing. The port's own redact button follows the block rather
        // than sitting inside it.
        items.Add(new ToolbarItem(ToolbarCommand.InvertColors, "Invert the colours", IsSelected: inverted));
        items.Add(new ToolbarItem(ToolbarCommand.Adjust, "Adjust", IsSelected: adjusted));
        items.Add(new ToolbarItem(ToolbarCommand.Beautify, "Beautify", IsSelected: beautified));
        items.Add(new ToolbarItem(ToolbarCommand.Redact, "Cover personal details"));

        return items;
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
    public static IReadOnlyList<ToolbarItem> Actions(bool editorMode, bool translation = true)
    {
        var items = new List<ToolbarItem>(8);

        if (!editorMode)
        {
            items.Add(new ToolbarItem(ToolbarCommand.Cancel, "Cancel"));
            items.Add(new ToolbarItem(ToolbarCommand.MoveSelection, "Move the region"));
            items.Add(new ToolbarItem(ToolbarCommand.OpenEditor, "Open in the editor"));
        }

        items.Add(new ToolbarItem(ToolbarCommand.Copy, "Copy"));
        items.Add(new ToolbarItem(ToolbarCommand.Save, "Save"));
        items.Add(new ToolbarItem(ToolbarCommand.Share, "Share"));
        items.Add(new ToolbarItem(ToolbarCommand.Pin, "Pin on top"));
        items.Add(new ToolbarItem(ToolbarCommand.ReadText, "Read the text in it"));
        if (translation)
        {
            items.Add(new ToolbarItem(ToolbarCommand.Translate, "Translate the text in it"));
        }

        if (!editorMode)
        {
            // Last, in macshot's order. Both aim at a live screen: there is no window
            // behind an image in the editor to scroll, and nothing there to record.
            items.Add(new ToolbarItem(ToolbarCommand.ScrollCapture, "Scroll the window behind it"));
            items.Add(new ToolbarItem(ToolbarCommand.Record, "Record the region"));
        }

        return items;
    }

    /// <summary>The name shown when the pointer rests on a tool's button.</summary>
    public static string Tooltip(AnnotationTool tool) => tool switch
    {
        AnnotationTool.Pencil => "Pencil",
        AnnotationTool.Line => "Line",
        AnnotationTool.Arrow => "Arrow",
        AnnotationTool.Rectangle => "Rectangle",
        AnnotationTool.Ellipse => "Ellipse",
        AnnotationTool.Marker => "Marker",
        AnnotationTool.Text => "Text",
        AnnotationTool.Number => "Number",
        AnnotationTool.Censor => "Censor (pixelate, blur, solid, erase)",
        AnnotationTool.Highlight => "Highlight",
        AnnotationTool.Loupe => "Magnify",
        AnnotationTool.Stamp => "Stamp",
        AnnotationTool.ColorSampler => "Take a colour from the screen",
        AnnotationTool.Measure => "Measure",
        AnnotationTool.Select => "Select",
        _ => tool.ToString(),
    };
}
