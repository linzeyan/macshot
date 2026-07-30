using System.Runtime.InteropServices.WindowsRuntime;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;
using Macshot.Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;

using Windows.UI;

namespace Macshot.Windows.Rendering;

/// <summary>
/// The magnified circle that follows the pointer while the colour sampler is armed.
/// </summary>
/// <remarks>
/// <para>
/// A screen pixel is a fifth of a millimetre. Aiming at one by eye — the edge of a
/// button, one stop of a gradient, a character's antialiased rim — is guesswork, and the
/// hex readout in the hint line only says what was hit after the fact. This says it
/// before.
/// </para>
/// <para>
/// Centred on the pointer rather than beside it, with a box drawn around the middle
/// pixel: what is in that box is what a click takes, and a loupe offset to one side
/// makes the user map from one place to another to know it.
/// </para>
/// </remarks>
public sealed class SamplerLoupe
{
    /// <summary>
    /// How wide the circle is, in the frame's own pixels. Odd, so there is a middle
    /// pixel for the box to sit on.
    /// </summary>
    private const int Diameter = 121;

    /// <summary>
    /// How much the view is magnified. Enough that a single pixel is a comfortable
    /// target, while still showing enough of its surroundings to say which pixel it is.
    /// </summary>
    private const double Zoom = 8;

    private readonly Canvas _layer;
    private readonly IFramePlacement _placement;
    private readonly CapturedFrame _frame;
    private readonly WriteableBitmap _bitmap = new(Diameter, Diameter);
    private readonly Image _image;
    private readonly Ellipse _ring;
    private readonly Rectangle _pixelBox;

    public SamplerLoupe(Canvas layer, IFramePlacement placement, CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(frame);

        _layer = layer;
        _placement = placement;
        _frame = frame;

        _image = new Image
        {
            Source = _bitmap,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };

        _ring = new Ellipse
        {
            Stroke = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
            StrokeThickness = 2,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };

        // Black and thin: it has to be readable over whatever colour it lands on
        // without covering the pixel it is pointing out.
        _pixelBox = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromArgb(255, 0, 0, 0)),
            StrokeThickness = 1,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };

        _layer.Children.Add(_image);
        _layer.Children.Add(_ring);
        _layer.Children.Add(_pixelBox);
    }

    /// <summary>Redraws the circle around a frame-space point and shows it.</summary>
    public void Track(CapturePoint point)
    {
        var centerX = (int)Math.Round(point.X);
        var centerY = (int)Math.Round(point.Y);

        var patch = PixelEffects.MagnifiedPatch(
            _frame.BgraPixels,
            _frame.Width,
            _frame.Height,
            centerX,
            centerY,
            Diameter,
            Zoom);

        using (var stream = _bitmap.PixelBuffer.AsStream())
        {
            stream.Write(patch, 0, patch.Length);
        }

        _bitmap.Invalidate();

        // Both corners are converted rather than the size being divided by a scale
        // factor, so the circle lands on the same pixels it was read from whatever the
        // display's scaling is.
        var half = Diameter / 2d;
        var topLeft = _placement.ToLayout(new CapturePoint(point.X - half, point.Y - half));
        var bottomRight = _placement.ToLayout(new CapturePoint(point.X + half, point.Y + half));
        var width = Math.Max(0, bottomRight.X - topLeft.X);
        var height = Math.Max(0, bottomRight.Y - topLeft.Y);

        Place(_image, topLeft.X, topLeft.Y, width, height);
        Place(_ring, topLeft.X, topLeft.Y, width, height);

        var boxWidth = width / Diameter * Zoom;
        var boxHeight = height / Diameter * Zoom;
        Place(
            _pixelBox,
            topLeft.X + ((width - boxWidth) / 2),
            topLeft.Y + ((height - boxHeight) / 2),
            boxWidth,
            boxHeight);
    }

    /// <summary>Takes the circle off the screen, leaving it ready to show again.</summary>
    public void Hide()
    {
        _image.Visibility = Visibility.Collapsed;
        _ring.Visibility = Visibility.Collapsed;
        _pixelBox.Visibility = Visibility.Collapsed;
    }

    /// <summary>Removes it from the canvas for good.</summary>
    public void Detach()
    {
        _layer.Children.Remove(_image);
        _layer.Children.Remove(_ring);
        _layer.Children.Remove(_pixelBox);
    }

    private static void Place(FrameworkElement element, double left, double top, double width, double height)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        element.Width = width;
        element.Height = height;
        element.Visibility = Visibility.Visible;
    }
}
