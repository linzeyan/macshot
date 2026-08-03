using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Macshot.Windows.Services;

/// <summary>
/// The face macshot's own chrome is set in, and how tightly.
/// </summary>
/// <remarks>
/// <para>
/// Windows has two defaults for this and WinUI picks neither well on its own. Left to
/// <c>XamlAutoFontFamily</c>, Latin text lands in plain Segoe UI — the older, tighter face
/// — while Chinese falls through to whatever DirectWrite reaches for, which on most
/// machines is 微軟正黑體 at its regular weight. Against macshot's dark strip that regular
/// weight is heavy enough that a row of Chinese labels reads as bold beside the Latin next
/// to it, which is the opposite of what the two are meant to look like.
/// </para>
/// <para>
/// So both are named: Segoe UI Variable Text for the Latin, 微軟正黑體 Light behind it for
/// everything Segoe cannot draw. A comma-separated <see cref="FontFamily"/> is resolved in
/// order per glyph, so a label reading "64px" beside one reading "大小" is set in the two
/// faces at once without either being asked for by name at the call site.
/// </para>
/// <para>
/// The tracking is the other half. A Light CJK face at 10 points on a dark background sets
/// solid — the strokes of adjacent glyphs run together — and a hair of tracking is what
/// opens it back up. It is applied only where the interface is actually Chinese: the same
/// tracking on Latin text at this size reads as a spacing bug rather than as air, and
/// WinUI has no way to ask for it per script.
/// </para>
/// </remarks>
internal static class AppFonts
{
    /// <summary>
    /// The tracking used for Chinese, in thousandths of an em — WinUI's unit for
    /// <c>CharacterSpacing</c>. The middle of the 0.02em–0.05em the design asks for.
    /// </summary>
    private const int ChineseTracking = 30;

    /// <summary>
    /// The fallback list, most specific first.
    /// </summary>
    /// <remarks>
    /// "Segoe UI Variable Text" rather than the bare "Segoe UI Variable": the family ships
    /// as three optical sizes — Small, Text and Display — and the bare name resolves to
    /// none of them on a machine that has all three. Text is the one drawn for running
    /// interface text, which is all of this. Plain Segoe UI closes the list for Windows 10,
    /// where the variable family does not exist.
    /// </remarks>
    public static FontFamily Family { get; } =
        new("Segoe UI Variable Text, Microsoft JhengHei Light, Segoe UI");

    /// <summary>
    /// The tracking for the language in use, in thousandths of an em: a hair for Chinese
    /// and none for anything else.
    /// </summary>
    /// <remarks>
    /// Read rather than cached, because the language can change while macshot is running
    /// and the next window built should be set the way the setting now says.
    /// </remarks>
    public static int Spacing =>
        Localization.Language.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? ChineseTracking
            : 0;

    /// <summary>
    /// Makes macshot's face the app's, for everything built after this returns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two mechanisms, because WinUI keeps interface text in two places that do not share
    /// a default. Controls take theirs from <c>ContentControlThemeFontFamily</c> and
    /// <c>TextControlThemeFontFamily</c>, so those keys are shadowed in the application's
    /// own dictionary — a lookup finds them before it reaches the merged Fluent
    /// dictionary. A bare <see cref="TextBlock"/> reads neither, so it gets an implicit
    /// style instead, which is also where the tracking goes: nearly every label macshot
    /// draws is one of these.
    /// </para>
    /// <para>
    /// Called after the language has been chosen and before the first window is built.
    /// The order matters in one direction only — a window built first would keep WinUI's
    /// defaults — and there is nothing to repair afterwards, so this is not something to
    /// re-run when the language changes. macshot rebuilds its windows on that anyway.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The key a XAML-declared <see cref="TextBlock"/> style names to keep this face.
    /// </summary>
    /// <remarks>
    /// A named style replaces the implicit one outright rather than adding to it, so every
    /// <c>Style x:Key="…" TargetType="TextBlock"</c> in the app is a hole in the coverage
    /// below — which is how the whole preferences window kept WinUI's default face while
    /// the toolbar had macshot's. Those styles say <c>BasedOn="{StaticResource
    /// MacshotTextStyle}"</c> and the hole closes; a lookup reaches this because it walks
    /// out to the application's dictionary.
    /// </remarks>
    public const string TextStyleKey = "MacshotTextStyle";

    public static void Install(ResourceDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        resources["ContentControlThemeFontFamily"] = Family;
        resources["TextControlThemeFontFamily"] = Family;

        resources[typeof(TextBlock)] = TextStyle();

        // A second instance rather than the one above: applying a style seals it, and a
        // sealed style is a fine thing to derive from but a confusing thing to share.
        resources[TextStyleKey] = TextStyle();
    }

    private static Style TextStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.FontFamilyProperty, Family));
        style.Setters.Add(new Setter(TextBlock.CharacterSpacingProperty, Spacing));
        return style;
    }

    /// <summary>
    /// Sets one control and everything under it in macshot's face.
    /// </summary>
    /// <remarks>
    /// For the roots the implicit style cannot reach on its own: WinUI inherits both of
    /// these down the tree, but a <see cref="Panel"/> declares neither and so cannot pass
    /// on what it was never given. Setting them on the nearest enclosing control does it
    /// for every label inside, popovers included — a flyout's content is parented to the
    /// popup root rather than to whatever opened it, so it inherits from the window and
    /// not from the toolbar it belongs to.
    /// </remarks>
    public static void Adopt(Control? control)
    {
        if (control is null)
        {
            return;
        }

        control.FontFamily = Family;
        control.CharacterSpacing = Spacing;
    }
}
