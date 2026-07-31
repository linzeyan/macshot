using System.Runtime.InteropServices.WindowsRuntime;
using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;
using Macshot.Windows.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Macshot.Windows.Rendering;

/// <summary>
/// The live annotation preview: the selected pixels with the annotations composited
/// in, rendered by the same Core rasterizer that produces the delivered image.
/// </summary>
/// <remarks>
/// <para>
/// This is decision D3 made real. The preview used to draw XAML shapes while the
/// export rasterized on the CPU, so the two agreed on geometry but not on pixels,
/// and any tool the shapes could not express — pixelate, blur — could not be
/// offered at all. Here the preview <em>is</em> the export: what the user sees is
/// the buffer that gets encoded.
/// </para>
/// <para>
/// It covers the selection only, not the whole display. That is the point rather
/// than an optimization: the delivered image is the selection, so previewing
/// exactly that area is what makes the two identical. It also keeps the per-move
/// cost proportional to what was selected instead of to the size of the monitor.
/// </para>
/// </remarks>
public sealed class RasterAnnotationPreview
{
    private readonly CaptureRegion _region;
    private readonly CapturedFrame _baseFrame;
    private readonly byte[] _baseline;
    private readonly byte[] _pixels;
    private readonly WriteableBitmap _bitmap;
    private readonly Canvas _layer;
    private readonly Image _image;

    public RasterAnnotationPreview(
        Canvas layer,
        IFramePlacement placement,
        CapturedFrame selection,
        CaptureRegion region)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(selection);

        _region = region;
        _baseFrame = selection;
        _baseline = MakeOpaque(selection.BgraPixels);
        _pixels = new byte[_baseline.Length];
        _bitmap = new WriteableBitmap(selection.Width, selection.Height);

        _layer = layer;
        var image = new Image
        {
            Source = _bitmap,

            // The bitmap is in capture pixels and the canvas is in layout units, so
            // both corners are converted and the image is stretched between them.
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
        };

        _image = image;
        var topLeft = placement.ToLayout(new CapturePoint(region.X, region.Y));
        var bottomRight = placement.ToLayout(new CapturePoint(region.Right, region.Bottom));
        Canvas.SetLeft(image, topLeft.X);
        Canvas.SetTop(image, topLeft.Y);
        image.Width = Math.Max(0, bottomRight.X - topLeft.X);
        image.Height = Math.Max(0, bottomRight.Y - topLeft.Y);
        layer.Children.Add(image);

        Render([]);
    }

    /// <summary>
    /// The pixels on screen right now, as a frame that can be delivered directly.
    /// Copied because rendering keeps writing into the live buffer.
    /// </summary>
    public CapturedFrame ToFrame() => new(
        _baseFrame.VirtualX,
        _baseFrame.VirtualY,
        _baseFrame.Width,
        _baseFrame.Height,
        (byte[])_pixels.Clone());

    /// <summary>
    /// The same pixels before anything was drawn on them, with the marks alongside in
    /// that image's own coordinates.
    /// </summary>
    /// <remarks>
    /// The shift is the one <see cref="Render"/> does, for the same reason and from the
    /// same place: annotations are held against the whole virtual desktop, and what is
    /// archived is the crop. Doing it here rather than at the call site is what keeps
    /// the two from drifting — a mark archived at desktop coordinates would reopen
    /// somewhere off the edge of the picture.
    /// </remarks>
    public EditableCapture ToEditable(IEnumerable<Annotation> annotations)
    {
        ArgumentNullException.ThrowIfNull(annotations);

        return new EditableCapture(
            _baseFrame,
            [.. annotations.Select(annotation => annotation.Translate(-_region.X, -_region.Y))]);
    }

    /// <summary>
    /// Takes the preview off the overlay.
    /// </summary>
    /// <remarks>
    /// A preview is built around one region: its buffers, its bitmap, and where on the
    /// canvas it sits are all fixed at construction, because that is what keeps the
    /// per-move cost to a rasterize rather than an allocation. Adjusting the selection
    /// therefore means a new preview, and the old image has to go — left behind it would
    /// keep showing the pixels of a region that is no longer selected.
    /// </remarks>
    public void Detach() => _layer.Children.Remove(_image);

    /// <summary>Redraws from the untouched selection, so nothing accumulates between frames.</summary>
    public void Render(IEnumerable<Annotation> annotations)
    {
        ArgumentNullException.ThrowIfNull(annotations);

        // Annotations are stored against the whole virtual desktop; the preview is
        // the crop, so they move with its origin. This is the same shift the export
        // used to do, now done once for both.
        var moved = annotations.Select(annotation => annotation.Translate(-_region.X, -_region.Y));
        AnnotationRasterizer.RenderInto(_baseFrame.Width, _baseFrame.Height, _baseline, _pixels, moved);

        using var stream = _bitmap.PixelBuffer.AsStream();
        stream.Write(_pixels, 0, _pixels.Length);
        _bitmap.Invalidate();
    }

    /// <summary>
    /// BitBlt leaves the alpha byte undefined, and a <see cref="WriteableBitmap"/> is
    /// always premultiplied BGRA — it has no "ignore alpha" mode like the encoder
    /// does. Left alone, a screenshot whose alpha happened to be zero would preview
    /// as nothing at all. Forcing it once on the baseline also gives annotations
    /// blended on top an opaque destination.
    /// </summary>
    private static byte[] MakeOpaque(byte[] bgraPixels)
    {
        var opaque = (byte[])bgraPixels.Clone();
        for (var index = 3; index < opaque.Length; index += 4)
        {
            opaque[index] = byte.MaxValue;
        }

        return opaque;
    }
}
