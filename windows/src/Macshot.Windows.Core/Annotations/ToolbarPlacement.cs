using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Annotations;

/// <summary>
/// How big each piece of toolbar furniture is, in layout units.
/// </summary>
/// <param name="Tools">The strip of tool buttons.</param>
/// <param name="Actions">The strip of what-to-do-with-it buttons.</param>
/// <param name="OptionsRow">
/// The row of settings for the tool in hand, or an empty size when the tool has none.
/// </param>
public readonly record struct ToolbarSizes(
    CaptureRegion Tools,
    CaptureRegion Actions,
    CaptureRegion OptionsRow);

/// <summary>Where each piece ended up. Sizes are the ones that were asked for.</summary>
public readonly record struct ToolbarLayout(
    CaptureRegion Tools,
    CaptureRegion Actions,
    CaptureRegion OptionsRow);

/// <summary>
/// Puts the toolbars beside the region being captured rather than at a fixed place on
/// the screen.
/// </summary>
/// <remarks>
/// <para>
/// A bar pinned to the bottom of the display is a long way from a selection in the top
/// corner, and the eye has to leave the thing being annotated to reach it. macOS anchors
/// the tools under the selection and the actions down its right-hand edge, so both are a
/// short move from where the pointer already is. This is that arrangement, in the
/// top-left coordinates the port uses — every "below" and "above" here is the reverse of
/// the sign in the AppKit original.
/// </para>
/// <para>
/// The whole thing is arithmetic on rectangles, so it lives in Core where it can be
/// tested. Getting it wrong is not subtle — a strip half off the screen, or two strips
/// on top of each other — but it is invisible until someone selects a region in exactly
/// the wrong corner, which is not something a person reliably remembers to try.
/// </para>
/// </remarks>
public static class ToolbarPlacement
{
    /// <summary>The gap between a strip and the selection, and between two strips.</summary>
    public const double Gap = 6;

    /// <summary>How close to the screen edge a strip may be placed.</summary>
    public const double ScreenMargin = 4;

    /// <summary>
    /// The gap between the tool strip and the options row under it. macshot reserves 38
    /// for the pair — a 34 row and 4 of gap — so this is that 4.
    /// </summary>
    public const double RowGap = 4;

    /// <summary>
    /// How much room the actions strip needs beside the selection before it is put
    /// there. Less than this and it would be jammed against the screen edge.
    /// </summary>
    private const double SideRoom = 50;

    /// <summary>
    /// Lays the strips out around <paramref name="selection"/>, inside
    /// <paramref name="screen"/>.
    /// </summary>
    /// <param name="avoid">
    /// Something already on screen that the actions strip must not cover — the size box.
    /// Empty when there is nothing to dodge.
    /// </param>
    public static ToolbarLayout For(
        CaptureRegion selection,
        CaptureRegion screen,
        ToolbarSizes sizes,
        CaptureRegion avoid = default)
    {
        var toolsSize = sizes.Tools;
        var actionsSize = sizes.Actions;
        var rowSize = sizes.OptionsRow;

        // The tool strip and its options row move as one: the row is meaningless on its
        // own, and deciding where each goes separately is what lets them end up on
        // opposite sides of the selection.
        var clusterHeight = toolsSize.Height + (rowSize.Height > 0 ? rowSize.Height + RowGap : 0);

        // Clamped before anything is measured against it, not after everything has
        // moved out of its way: a strip pushed back on screen at the end lands on
        // whatever had already dodged the place it used to be.
        var actions = OnScreen(PlaceActions(selection, screen, actionsSize), actionsSize, screen);
        var clusterTop = PlaceCluster(selection, screen, toolsSize, clusterHeight, actions);

        var toolsX = Clamp(
            selection.X + ((selection.Width - toolsSize.Width) / 2),
            screen.X + ScreenMargin,
            screen.Right - toolsSize.Width - ScreenMargin);

        var tools = new CaptureRegion(toolsX, clusterTop, toolsSize.Width, toolsSize.Height);

        // Both dodges only ever move it somewhere that is already on screen, so there
        // is nothing left to clamp afterwards.
        actions = Dodge(actions, new CaptureRegion(tools.X, tools.Y, tools.Width, clusterHeight), screen);
        actions = Dodge(actions, avoid, screen);

        var optionsRow = CaptureRegion.FromPoints(0, 0, 0, 0);
        if (rowSize.Height > 0)
        {
            // As wide as the wider of the two, centred under the tools: a row narrower
            // than the strip above it reads as a detached fragment.
            var rowWidth = Math.Max(rowSize.Width, toolsSize.Width);
            var rowX = Clamp(
                tools.X + ((toolsSize.Width - rowWidth) / 2),
                screen.X + ScreenMargin,
                screen.Right - rowWidth - ScreenMargin);

            optionsRow = new CaptureRegion(
                rowX,
                tools.Bottom + RowGap,
                rowWidth,
                rowSize.Height);
        }

        return new ToolbarLayout(tools, actions, optionsRow);
    }

    /// <summary>
    /// The actions strip: down the right-hand edge of the selection, top-aligned with
    /// it, on its left when that edge has no room, and just inside it when neither side
    /// does.
    /// </summary>
    /// <remarks>
    /// Inside is a last resort — it covers part of what is being captured — but a
    /// selection that reaches both edges of the display has no outside left, and the
    /// dodges afterwards still keep it off the tools.
    /// </remarks>
    private static CaptureRegion PlaceActions(
        CaptureRegion selection,
        CaptureRegion screen,
        CaptureRegion size)
    {
        var x = selection.Right < screen.Right - SideRoom
            ? selection.Right + Gap
            : selection.X > screen.X + SideRoom
                ? selection.X - size.Width - Gap
                : selection.Right - size.Width - Gap;

        return new CaptureRegion(x, selection.Y, size.Width, size.Height);
    }

    /// <summary>
    /// The top of the tools-and-options cluster: under the selection when it fits,
    /// above it when it does not, and inside it when neither has room.
    /// </summary>
    private static double PlaceCluster(
        CaptureRegion selection,
        CaptureRegion screen,
        CaptureRegion toolsSize,
        double clusterHeight,
        CaptureRegion actions)
    {
        var below = selection.Bottom + Gap;
        var belowFits = below + clusterHeight <= screen.Bottom - ScreenMargin;

        var above = selection.Y - Gap - clusterHeight;
        var aboveFits = above >= screen.Y + ScreenMargin;

        // Preferring the side that does not collide costs nothing here, and saves the
        // actions strip from being shoved out of the way further down.
        var centredX = Clamp(
            selection.X + ((selection.Width - toolsSize.Width) / 2),
            screen.X + ScreenMargin,
            screen.Right - toolsSize.Width - ScreenMargin);

        bool Collides(double top) => Overlaps(
            new CaptureRegion(centredX, top, toolsSize.Width, clusterHeight),
            actions);

        if (belowFits && !Collides(below))
        {
            return below;
        }

        if (aboveFits && !Collides(above))
        {
            return above;
        }

        if (belowFits)
        {
            return below;
        }

        if (aboveFits)
        {
            return above;
        }

        // The selection covers the display top to bottom. Inside it is the only place
        // left, and the bottom of it is where a toolbar is looked for.
        return Clamp(
            selection.Bottom - clusterHeight - Gap,
            screen.Y + ScreenMargin,
            screen.Bottom - clusterHeight - ScreenMargin);
    }

    /// <summary>
    /// Moves a strip clear of <paramref name="obstacle"/>: right, then left, then
    /// below, then above, taking the first that fits on screen. Sideways first, so the
    /// tools stay centred under the selection where they were aimed for.
    /// </summary>
    private static CaptureRegion Dodge(
        CaptureRegion strip,
        CaptureRegion obstacle,
        CaptureRegion screen)
    {
        if (obstacle.IsEmpty || !Overlaps(strip, obstacle))
        {
            return strip;
        }

        var right = obstacle.Right + Gap;
        if (right + strip.Width <= screen.Right - ScreenMargin)
        {
            return strip with { X = right };
        }

        var left = obstacle.X - strip.Width - Gap;
        if (left >= screen.X + ScreenMargin)
        {
            return strip with { X = left };
        }

        var below = obstacle.Bottom + Gap;
        if (below + strip.Height <= screen.Bottom - ScreenMargin)
        {
            return strip with { Y = below };
        }

        var above = obstacle.Y - strip.Height - Gap;
        if (above >= screen.Y + ScreenMargin)
        {
            return strip with { Y = above };
        }

        // Nowhere to go. Overlapping is worse than not, but a strip pushed off the
        // screen cannot be clicked at all.
        return strip;
    }

    private static CaptureRegion OnScreen(CaptureRegion strip, CaptureRegion size, CaptureRegion screen) =>
        new(
            Clamp(strip.X, screen.X + ScreenMargin, screen.Right - size.Width - ScreenMargin),
            Clamp(strip.Y, screen.Y + ScreenMargin, screen.Bottom - size.Height - ScreenMargin),
            size.Width,
            size.Height);

    private static bool Overlaps(CaptureRegion left, CaptureRegion right) =>
        !left.IsEmpty
        && !right.IsEmpty
        && left.X < right.Right
        && right.X < left.Right
        && left.Y < right.Bottom
        && right.Y < left.Bottom;

    private static double Clamp(double value, double min, double max) =>
        max < min ? min : Math.Clamp(value, min, max);
}
