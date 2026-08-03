using System.Runtime.InteropServices.WindowsRuntime;
using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Macshot.Windows.Rendering;

/// <summary>
/// The element an emoji stamp's sprite is rasterized from, plus the emoji offered.
/// </summary>
/// <remarks>
/// This is the tool that decided D7. <c>RenderTargetBitmap</c> goes through
/// DirectWrite, so the colour glyph is rasterized in colour; GDI+ predates colour
/// font formats and would have produced a monochrome outline, which is not a stamp
/// anybody wants.
/// </remarks>
internal static class StampGlyph
{
    /// <summary>The ones laid straight on the options row.</summary>
    public static IReadOnlyList<string> Quick => StampChoices.Quick;

    /// <summary>Everything the picker behind the row offers.</summary>
    public static IReadOnlyList<string> Choices => StampChoices.All;

    public static string Default => StampChoices.Default;

    public static FrameworkElement Build(string emoji, AnnotationStyle style, double rasterizationScale)
    {
        ArgumentException.ThrowIfNullOrEmpty(emoji);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rasterizationScale);

        // Frame pixels first, then divided by the scale, for the same reason the badge
        // and the text do it: the sprite is composited one to one into the capture.
        var size = style.StampSize / rasterizationScale;

        return new TextBlock
        {
            Text = emoji,
            FontSize = size,

            // Named explicitly rather than left to fallback, so the colour glyph is
            // what gets rasterized whatever the ambient font happens to be. The
            // annotation colour is deliberately not applied: an emoji brings its own.
            FontFamily = new FontFamily("Segoe UI Emoji"),
        };
    }

    /// <summary>
    /// The element a picture stamp's sprite is rasterized from — macshot's Load Image
    /// (<c>ToolOptionsRowView.swift:1210-1221</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same road out as the emoji: an element, rasterized once by
    /// <c>GlyphSpriteFactory</c>, composited by the Core rasterizer like every other
    /// sprite. So a picture stamp needed nothing added to the annotation model, nothing
    /// added to the file format, and nothing added to the renderer — a logo dropped onto a
    /// screenshot is a stamp, and the tool already knew how to place one of those.
    /// </para>
    /// <para>
    /// The size slider governs the longer edge and the shorter one follows, rather than
    /// both being set to it. A picture squared off to fit a slider is the one thing
    /// nobody would ask for, and macshot preserves the aspect for the same reason
    /// (<c>:1490-1495</c>).
    /// </para>
    /// </remarks>
    public static FrameworkElement BuildPicture(
        CapturedFrame picture,
        AnnotationStyle style,
        double rasterizationScale)
    {
        ArgumentNullException.ThrowIfNull(picture);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rasterizationScale);

        if (picture.Width <= 0 || picture.Height <= 0)
        {
            throw new ArgumentException("A stamp cannot be made from an empty picture.", nameof(picture));
        }

        var bitmap = new WriteableBitmap(picture.Width, picture.Height);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(picture.BgraPixels, 0, picture.BgraPixels.Length);
        }

        var longest = style.StampSize / rasterizationScale;
        var scale = longest / Math.Max(picture.Width, picture.Height);

        return new Image
        {
            Source = bitmap,
            Width = picture.Width * scale,
            Height = picture.Height * scale,

            // The element is sized exactly, so there is nothing for a stretch mode to
            // decide — and Uniform would letterbox it inside its own bounds if a rounding
            // ever put the two a fraction apart.
            Stretch = Stretch.Fill,
        };
    }
}
