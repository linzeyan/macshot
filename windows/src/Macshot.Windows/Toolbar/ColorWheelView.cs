using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

using Windows.UI;

namespace Macshot.Windows.Toolbar;

/// <summary>
/// The ring of colours a right-click opens under the pointer.
/// </summary>
/// <remarks>
/// Drawn as sixteen circles on a canvas rather than as a control with a list, because it
/// is not a menu: what it is for is being flicked at, and the whole thing has to be
/// readable and hittable without the pointer ever stopping. Where each swatch sits and
/// which one a point picks is <see cref="ColorWheel"/>'s arithmetic.
/// </remarks>
internal sealed partial class ColorWheelView : Canvas
{
    private readonly Ellipse _backdrop = new()
    {
        Fill = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)),
        IsHitTestVisible = false,
    };

    private readonly List<Ellipse> _swatches = [];

    private CapturePoint _center;

    public ColorWheelView()
    {
        // Nothing here is clicked: the wheel is driven by the overlay's own pointer
        // handling, so every part of it has to let the press through to the canvas below.
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;

        Children.Add(_backdrop);

        for (var index = 0; index < ColorWheel.Colors.Count; index++)
        {
            var swatch = new Ellipse
            {
                Fill = new SolidColorBrush(ToUiColor(ColorWheel.Colors[index])),
                StrokeThickness = 1,
                Stroke = new SolidColorBrush(Color.FromArgb(128, 255, 255, 255)),
                IsHitTestVisible = false,
            };

            _swatches.Add(swatch);
            Children.Add(swatch);
        }
    }

    /// <summary>Which swatch the pointer is over, or -1.</summary>
    public int HoveredIndex { get; private set; } = -1;

    /// <summary>The colour under the pointer, or null when it is in the dead zone.</summary>
    public AnnotationColor? HoveredColor => ColorWheel.ColorAt(HoveredIndex);

    /// <summary>
    /// True once the wheel has been left open to be clicked at rather than flicked
    /// through. A right-click that opens it and lets go without moving means the user
    /// wants to look, not to pick.
    /// </summary>
    public bool IsSticky { get; set; }

    public bool IsShown => Visibility == Visibility.Visible;

    /// <summary>Opens the wheel around a point, in this window's layout units.</summary>
    public void Show(CapturePoint center)
    {
        _center = center;
        HoveredIndex = -1;
        IsSticky = false;
        Visibility = Visibility.Visible;

        var reach = ColorWheel.Radius + ColorWheel.SwatchRadius + 8;
        Place(_backdrop, center, reach);

        for (var index = 0; index < _swatches.Count; index++)
        {
            Place(_swatches[index], ColorWheel.SwatchAt(center, index), ColorWheel.SwatchRadius);
        }
    }

    /// <summary>Follows the pointer, growing whichever swatch it is aimed at.</summary>
    public void Hover(CapturePoint point)
    {
        var index = ColorWheel.IndexAt(_center, point);
        if (index == HoveredIndex)
        {
            return;
        }

        HoveredIndex = index;

        for (var swatch = 0; swatch < _swatches.Count; swatch++)
        {
            var aimed = swatch == index;
            var radius = aimed ? ColorWheel.SwatchRadius + 3 : ColorWheel.SwatchRadius;

            Place(_swatches[swatch], ColorWheel.SwatchAt(_center, swatch), radius);
            _swatches[swatch].StrokeThickness = aimed ? 2.5 : 1;
            _swatches[swatch].Stroke = new SolidColorBrush(
                aimed ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(128, 255, 255, 255));
        }
    }

    public void Dismiss()
    {
        Visibility = Visibility.Collapsed;
        HoveredIndex = -1;
        IsSticky = false;
    }

    private static void Place(Ellipse shape, CapturePoint center, double radius)
    {
        shape.Width = radius * 2;
        shape.Height = radius * 2;
        SetLeft(shape, center.X - radius);
        SetTop(shape, center.Y - radius);
    }

    private static Color ToUiColor(AnnotationColor color) =>
        Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);
}
