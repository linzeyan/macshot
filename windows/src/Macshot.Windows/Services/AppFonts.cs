using Macshot.Windows.Core.Localization;
using Microsoft.UI.Text;
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
/// machines is 微軟正黑體 — but only where DirectWrite happens to reach for it, which is not
/// every control and not every window.
/// </para>
/// <para>
/// So both are named: Segoe UI Variable Text for the Latin, 微軟正黑體 UI behind it for
/// everything Segoe cannot draw — the UI cut rather than the plain one, which is the
/// narrower of the two and the one Windows sets its own interface in. A comma-separated <see cref="FontFamily"/> is resolved in
/// order per glyph, so a label reading "64px" beside one reading "大小" is set in the two
/// faces at once without either being asked for by name at the call site.
/// </para>
/// <para>
/// The tracking is the other half. A CJK face at 10 points on a dark background sets
/// solid — the strokes of adjacent glyphs run together — and a hair of tracking is what
/// opens it back up. It is applied only where the interface is actually Chinese: the same
/// tracking on Latin text at this size reads as a spacing bug rather than as air, and
/// WinUI has no way to ask for it per script.
/// </para>
/// <para>
/// The weight — <see cref="Heavier"/> — is decided per string rather than per interface,
/// which is the finer rule of the two and is deliberate. Tracking a Latin word that sits
/// in an otherwise Chinese row is a hair nobody sees; setting it bold is not, and a window
/// that put "System Default" and every format name in the Chinese weight was the version
/// of this that had to be taken back. Where the string is at hand it is passed in; where
/// it is not, <see cref="Weigh"/> reads it off the control.
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
        new("Segoe UI Variable Text, Microsoft JhengHei UI, Segoe UI");

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
    /// Bold for Chinese, and <paramref name="normally"/> for everything else.
    /// </summary>
    /// <remarks>
    /// Asked for as "微軟正黑體 UI Bold", which is a weight inside that family rather than a
    /// family of its own — DirectWrite will not resolve it from a name, so it is set here.
    /// Per string and not per interface: setting it for the whole window because the
    /// language was Chinese put "System Default" and every other English string in the
    /// Chinese weight, which is not what a mixed row is meant to look like.
    /// </remarks>
    public static global::Windows.UI.Text.FontWeight Heavier(
        string? text,
        global::Windows.UI.Text.FontWeight normally) =>
        ChineseText.Contains(text) ? FontWeights.Bold : normally;

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
    /// A tooltip in macshot's face, for <c>ToolTipService.SetToolTip</c> to be handed
    /// instead of a bare string.
    /// </summary>
    /// <remarks>
    /// Handed a string, WinUI wraps it in a <see cref="ToolTip"/> of its own, and that
    /// tooltip is parented to the popup root rather than to the button it belongs to — so
    /// it inherits from the window and never sees what <see cref="Adopt"/> put on the
    /// toolbar. Which is why every tooltip in the app was still at the default weight after
    /// the rest of the interface had changed. Building it here is the only place the
    /// setting can be made.
    /// </remarks>
    public static ToolTip Tip(object? content) => new()
    {
        Content = content,
        FontFamily = Family,
        CharacterSpacing = Spacing,
        FontWeight = Heavier(content as string, FontWeights.Normal),
    };

    /// <summary>
    /// Sets <paramref name="node"/> in the Chinese weight when what it says is Chinese.
    /// </summary>
    /// <remarks>
    /// The catch-all for a page's controls, which is where the coverage kept falling short:
    /// a button, a tick box or a combo takes its face from a theme resource, and WinUI has
    /// no theme resource for a weight. Nothing is done to anything that is not
    /// Chinese, so a label deliberately set semibold keeps it, and an English string in a
    /// Chinese window is left in the face and weight Segoe draws it in.
    /// </remarks>
    public static void Weigh(DependencyObject? node)
    {
        switch (node)
        {
        case TextBlock label when ChineseText.Contains(label.Text):
            label.FontWeight = FontWeights.Bold;
            break;

        case ContentControl control when control.Content is string content && ChineseText.Contains(content):
            control.FontWeight = FontWeights.Bold;
            break;
        }
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
