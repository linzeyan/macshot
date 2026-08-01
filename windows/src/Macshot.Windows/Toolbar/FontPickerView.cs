using Macshot.Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Macshot.Windows.Toolbar;

/// <summary>
/// The face a label is set in: the popular ones, a rule, then everything installed.
/// </summary>
/// <remarks>
/// macshot's <c>FontPickerView</c> — each name drawn in its own face, which is the only
/// way a list of font names is a list of fonts. The rule is what makes the top of the
/// list useful: nine picks in ten are one of ten faces, and a plain alphabetical list of
/// three hundred puts them behind a scroll.
/// </remarks>
internal sealed class FontPickerView : ListView
{
    /// <summary>The name that stands for "whatever this machine's interface font is".</summary>
    public const string SystemFace = "System";

    public FontPickerView()
    {
        SelectionMode = ListViewSelectionMode.Single;
        RequestedTheme = ElementTheme.Dark;
        MaxHeight = 320;
        Width = 220;

        Items.Add(Row(SystemFace, FontFamily.XamlAutoFontFamily));
        foreach (var family in InstalledFonts.Popular)
        {
            Items.Add(Row(family, new FontFamily(family)));
        }

        // A disabled container rather than a bare Border: anything added to a ListView
        // becomes a row that can be clicked, and a rule that can be picked as a font is
        // a rule that sets the label in nothing.
        Items.Add(new ListViewItem
        {
            IsEnabled = false,
            MinHeight = 0,
            Padding = new Thickness(0),
            Content = new Border
            {
                Height = 1,
                Margin = new Thickness(0, 4, 0, 4),
                Background = ToolbarPalette.IconBrush(0.2),
            },
        });

        foreach (var family in InstalledFonts.Families())
        {
            if (!InstalledFonts.Popular.Contains(family))
            {
                Items.Add(Row(family, new FontFamily(family)));
            }
        }
    }

    /// <summary>
    /// The family the picked row names, or empty for the system font — which is what
    /// <c>AnnotationStyle.FontFamily</c> stores for "no particular face".
    /// </summary>
    public static string FamilyOf(object? item) => item is TextBlock { Text: { } name } && name != SystemFace
        ? name
        : string.Empty;

    /// <summary>Shows <paramref name="family"/> as the picked row, without raising a change.</summary>
    public void Show(string family)
    {
        var wanted = string.IsNullOrWhiteSpace(family) ? SystemFace : family;
        foreach (var item in Items)
        {
            if (item is TextBlock row && row.Text == wanted)
            {
                SelectedItem = item;
                return;
            }
        }

        SelectedItem = null;
    }

    private static TextBlock Row(string name, FontFamily face) => new()
    {
        Text = name,
        FontFamily = face,
        FontSize = 14,
    };
}
