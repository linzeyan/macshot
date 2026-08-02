using System.Globalization;

namespace Macshot.Windows.Core.Annotations;

/// <summary>
/// What a numbered badge counts in. macshot's <c>NumberFormat</c> — <c>Model/Annotation.swift:80</c> —
/// in macshot's order, because the toolbar picks one by position.
/// </summary>
/// <remarks>
/// Four rather than one because a numbered callout is usually keyed to prose beside the
/// screenshot, and that prose already has a numbering of its own: a figure whose steps
/// are lettered wants letters on the picture, not a second sequence to reconcile.
/// </remarks>
public enum NumberFormat
{
    /// <summary>1, 2, 3.</summary>
    Decimal,

    /// <summary>I, II, III.</summary>
    Roman,

    /// <summary>A, B, C.</summary>
    Alpha,

    /// <summary>a, b, c.</summary>
    AlphaLower,
}

/// <summary>Renders a badge's count in the format the toolbar is set to.</summary>
public static class NumberFormats
{
    /// <summary>
    /// The largest number Roman numerals are written for. Beyond it the notation needs
    /// overbars, which is not something a badge on a screenshot can show — macshot clamps
    /// at the same place.
    /// </summary>
    private const int MaxRoman = 3999;

    private static readonly (int Value, string Numeral)[] RomanDigits =
    [
        (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
        (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
        (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I"),
    ];

    /// <summary>
    /// What the badge reads. Never empty and never negative: a badge is placed by a click
    /// and has to show something, so a number out of range is clamped rather than refused.
    /// </summary>
    public static string Format(this NumberFormat format, int number) => format switch
    {
        NumberFormat.Roman => ToRoman(number),
        NumberFormat.Alpha => ToAlpha(number, uppercase: true),
        NumberFormat.AlphaLower => ToAlpha(number, uppercase: false),
        _ => Math.Max(1, number).ToString(CultureInfo.InvariantCulture),
    };

    private static string ToRoman(int number)
    {
        var remaining = Math.Clamp(number, 1, MaxRoman);
        var text = new System.Text.StringBuilder();
        foreach (var (value, numeral) in RomanDigits)
        {
            while (remaining >= value)
            {
                text.Append(numeral);
                remaining -= value;
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// One letter, wrapping back to A after Z — macshot's own <c>toAlpha</c>. It wraps
    /// rather than carrying into AA because the badge is a circle: two letters make it a
    /// pill, and a lettered sequence that runs past 26 items has outgrown being drawn on
    /// the picture at all.
    /// </summary>
    private static string ToAlpha(int number, bool uppercase) =>
        ((char)((uppercase ? 'A' : 'a') + ((Math.Max(1, number) - 1) % 26))).ToString();
}
