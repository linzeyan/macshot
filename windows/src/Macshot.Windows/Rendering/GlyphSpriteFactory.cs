using System.Runtime.InteropServices.WindowsRuntime;
using Macshot.Windows.Core.Annotations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

// Imported rather than written out at each use site: inside namespace Macshot.Windows
// the name "Windows" binds to Macshot.Windows, so a qualified Windows.UI.Color
// resolves to Macshot.Windows.UI.Color and does not compile.
using Windows.UI;

namespace Macshot.Windows.Rendering;

/// <summary>
/// Rasterizes an off-screen XAML element into an <see cref="AnnotationSprite"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the UI half of decision D7. The tempting shortcut — leave a
/// <c>TextBlock</c> sitting on the preview and rasterize it only at delivery —
/// would reintroduce the second draw path D3 removed, under the one tool where a
/// mismatch between preview and export is most visible. Instead the glyphs become
/// pixels once, at commit time, and the Core rasterizer composites them like
/// everything else.
/// </para>
/// <para>
/// <c>RenderTargetBitmap</c> is DirectWrite, so font fallback is correct and colour
/// emoji come out in colour, which is the entire point of the stamp tool.
/// <c>System.Drawing.Common</c> would have been synchronous and simpler, but GDI+
/// predates colour font formats and would render emoji monochrome.
/// </para>
/// </remarks>
internal static class GlyphSpriteFactory
{
    /// <summary>
    /// Far enough outside any display that the element is laid out but never seen.
    /// <c>Visibility.Collapsed</c> is not an option: a collapsed element is not
    /// arranged, and <c>RenderTargetBitmap</c> captures nothing.
    /// </summary>
    private const double OffScreenOffset = -20000;

    /// <summary>
    /// Renders <paramref name="content"/> by parenting it into
    /// <paramref name="host"/> for the length of the call. Async, and the element has
    /// to be in the visual tree, which is affordable because a sprite is produced
    /// once when an annotation is committed rather than on every pointer move.
    /// </summary>
    public static async Task<AnnotationSprite> RenderAsync(Canvas host, FrameworkElement content)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(content);

        Canvas.SetLeft(content, OffScreenOffset);
        Canvas.SetTop(content, OffScreenOffset);
        host.Children.Add(content);
        try
        {
            // The element was parented a moment ago, so a layout pass has to run
            // before there is anything with a size to capture.
            host.UpdateLayout();

            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(content);
            var pixels = await bitmap.GetPixelsAsync();

            // The pixel size, not the layout size, is what the sprite is:
            // RenderTargetBitmap rasterizes at the window's scale, which is how a
            // sprite ends up in capture pixels rather than in layout units.
            return new AnnotationSprite(bitmap.PixelWidth, bitmap.PixelHeight, pixels.ToArray());
        }
        finally
        {
            // Leaving it parented would keep one dead element per placed annotation
            // alive for the length of the capture.
            host.Children.Remove(content);
        }
    }

    /// <summary>
    /// The annotation's colour with its opacity folded in. A sprite carries its whole
    /// appearance, because the rasterizer composites it without consulting the style
    /// again; anything left out here is lost, and anything applied twice is doubled.
    /// </summary>
    public static Color ToBrushColor(AnnotationStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);

        var alpha = (byte)Math.Clamp(Math.Round(style.Color.Alpha * style.Opacity), 0, byte.MaxValue);
        return Color.FromArgb(alpha, style.Color.Red, style.Color.Green, style.Color.Blue);
    }
}
