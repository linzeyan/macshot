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

    /// <summary>Leave it on top of everything as a floating window.</summary>
    Pin,

    /// <summary>Recognize the text in it.</summary>
    ReadText,

    /// <summary>Cover the personal details in it.</summary>
    Redact,
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

        // macOS has one censor tool with three modes behind a right-click. Here they
        // are three tools, kept together in the place the one tool sits there.
        AnnotationTool.Pixelate,
        AnnotationTool.Blur,
        AnnotationTool.FilledRectangle,

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
    public static IReadOnlyList<ToolbarItem> Tools(
        AnnotationTool selected,
        IReadOnlyCollection<AnnotationTool>? enabled = null)
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
    public static IReadOnlyList<ToolbarItem> Actions(bool editorMode)
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
        items.Add(new ToolbarItem(ToolbarCommand.Pin, "Pin on top"));
        items.Add(new ToolbarItem(ToolbarCommand.ReadText, "Read the text in it"));

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
        AnnotationTool.Pixelate => "Pixelate",
        AnnotationTool.Blur => "Blur",
        AnnotationTool.FilledRectangle => "Cover",
        AnnotationTool.Highlight => "Highlight",
        AnnotationTool.Loupe => "Magnify",
        AnnotationTool.Stamp => "Stamp",
        AnnotationTool.ColorSampler => "Take a colour from the screen",
        AnnotationTool.Measure => "Measure",
        AnnotationTool.Select => "Select",
        _ => tool.ToString(),
    };
}
