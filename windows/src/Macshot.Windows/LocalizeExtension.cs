using Macshot.Windows.Services;
using Microsoft.UI.Xaml.Markup;

namespace Macshot.Windows;

/// <summary>
/// Looks a string up from XAML: <c>Text="{local:Localize Key='Save as...'}"</c>.
/// </summary>
/// <remarks>
/// <para>
/// The alternative is <c>x:Uid</c> and a .resw per language, which is the Windows way
/// and the wrong one here. It needs every element named, it needs the strings
/// re-authored as resources — forking forty translated files away from the Mac app that
/// owns them — and a key it cannot find renders as **nothing**, so a typo is a blank
/// label that no build catches. This asks for the English text and answers with the
/// English text when there is no translation, which is macshot's behaviour and cannot
/// produce an empty control.
/// </para>
/// <para>
/// Evaluated once, when the page is loaded. Changing the language therefore reaches
/// windows opened afterwards rather than windows already on screen — the one place this
/// falls short of macshot, which rebuilds its views in code and can swap the bundle
/// live. The preferences window says so where the language is chosen.
/// </para>
/// </remarks>
[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed class LocalizeExtension : MarkupExtension
{
    /// <summary>The English text, which is also the key.</summary>
    public string Key { get; set; } = string.Empty;

    protected override object ProvideValue() => Localization.L(Key);
}
