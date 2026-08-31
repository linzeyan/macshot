using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Macshot.Windows.Services;

/// <summary>
/// Translates the strings already written into a XAML page, in place — and sets what it
/// walks in macshot's face while it is there.
/// </summary>
/// <remarks>
/// <para>
/// macshot keys every string by its English text. The port's XAML is already written in
/// English, so the pages **are** the key list — nothing has to be extracted, no element
/// has to be given a name, and no markup changes at all. One call after
/// <c>InitializeComponent</c> walks what was built and replaces each string with its
/// translation, or leaves the English where there is none.
/// </para>
/// <para>
/// This is why the port does not use <c>x:Uid</c>. That would mean a resource file per
/// language forked away from the Mac app, an identifier on every element, and — the part
/// that decides it — a control that renders **empty** when its resource is missing. Here
/// the worst case is English, which is also the source text, so a page cannot lose its
/// labels to a mistake in this file.
/// </para>
/// <para>
/// The walk is over the object graph rather than the visual tree, so it can run in a
/// constructor: <c>VisualTreeHelper</c> answers for realized visuals, and a page's
/// templates are not applied until it loads. Only the containers this project's XAML
/// actually uses are followed; a type not listed here is simply not descended into, and
/// adding one is a line.
/// </para>
/// </remarks>
public static class LocalizedTree
{
    /// <summary>Translates every string under <paramref name="root"/>.</summary>
    public static void Localize(this DependencyObject? root) => Walk(root, 0);

    /// <summary>Translates a window's title and everything in it.</summary>
    public static void Localize(this Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        window.Title = Localization.L(window.Title);
        window.Content.Localize();
    }

    /// <param name="depth">
    /// Guards against a graph that refers back to itself. Nothing in this project's XAML
    /// nests anywhere near this deep, and a stack overflow while a window is opening
    /// would be a crash with no message in it.
    /// </param>
    private static void Walk(DependencyObject? node, int depth)
    {
        const int MaxDepth = 64;

        if (node is null || depth > MaxDepth)
        {
            return;
        }

        if (ToolTipService.GetToolTip(node) is string tip)
        {
            ToolTipService.SetToolTip(node, AppFonts.Tip(Localization.L(tip)));
        }

        // An accessible name is a string the user is read, so it is translated like every
        // other one. It is attached rather than a property of the control, which is why it
        // needs a line of its own here — and it is only ever set where the control cannot
        // name itself, so an empty one means there is nothing to translate rather than
        // that a name was lost.
        if (AutomationProperties.GetName(node) is { Length: > 0 } name)
        {
            AutomationProperties.SetName(node, Localization.L(name));
        }

        // A context menu hangs off the element rather than sitting inside it, exactly as a
        // button's flyout does, so nothing in the switch below reaches it. Without this the
        // thumbnail and pin panels came up translated with an English right-click menu —
        // and only for whoever opened one.
        if (node is UIElement { ContextFlyout: MenuFlyout menu })
        {
            foreach (var entry in menu.Items)
            {
                Walk(entry, depth + 1);
            }
        }

        switch (node)
        {
        case TextBlock text:
            // Only a plain string. A TextBlock built from Runs is composed of parts no
            // translator was given, and rewriting one would drop its formatting.
            if (text.Inlines.Count <= 1)
            {
                text.Text = Localization.L(text.Text);
            }

            break;

        case TextBox box:
            box.PlaceholderText = Localization.L(box.PlaceholderText);
            if (box.Header is string boxHeader)
            {
                box.Header = Localization.L(boxHeader);
            }

            break;

        case MenuFlyoutItem item:
            // Its label is Text, not Content: MenuFlyoutItemBase is not a ContentControl,
            // so the case below never sees one.
            item.Text = Localization.L(item.Text);
            break;

        case UserControl user:
            // A UserControl is a Control, not a ContentControl, so the case below never
            // saw one and the walk simply stopped at every composite this project builds.
            // That is why the settings page's three colour wells showed English labels in
            // all forty languages, and why HotkeyBox's own Localize() call did nothing.
            Walk(user.Content, depth + 1);
            break;

        case ContentControl control:
            // Buttons, checkboxes and the rest: the label is the content when it is a
            // string, and a child to descend into when it is not.
            if (control.Content is string label)
            {
                control.Content = Localization.L(label);
            }
            else
            {
                Walk(control.Content as DependencyObject, depth + 1);
            }

            // A flyout hangs off the button rather than sitting inside it, so the walk
            // has to be told about it by name. Without this a page comes up translated
            // with one panel in it still in English — and only for whoever opens it.
            if (control is Button { Flyout: Flyout flyout })
            {
                Walk(flyout.Content, depth + 1);
            }

            break;

        case ItemsControl items:
            if (items is ComboBox { Header: string comboHeader } combo)
            {
                combo.Header = Localization.L(comboHeader);
            }

            // Items only. ItemsSource is data the window set deliberately — the language
            // list, the format names — and is translated where it is built, if at all.
            for (var index = 0; index < items.Items.Count; index++)
            {
                if (items.Items[index] is string item)
                {
                    items.Items[index] = Localization.L(item);
                }
                else
                {
                    Walk(items.Items[index] as DependencyObject, depth + 1);
                }
            }

            break;

        case Panel panel:
            foreach (var child in panel.Children)
            {
                Walk(child, depth + 1);
            }

            break;

        case Border border:
            Walk(border.Child, depth + 1);
            break;

        case Viewbox viewbox:
            Walk(viewbox.Child, depth + 1);
            break;
        }

        // ScrollViewer is a ContentControl, so its content is reached by the case above.

        // After the translation and not before it: the weight follows the text, and the
        // text this page was written with is English. The same walk does it because it
        // reaches every control on the page and nothing else does — a control takes its
        // face from a theme resource, but there is no theme resource for a weight.
        AppFonts.Weigh(node);
    }
}
