using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

// Imported rather than written out at each use site: inside namespace Macshot.Windows
// the name "Windows" binds to Macshot.Windows, so a qualified Color or Point resolves to
// Macshot.Color and does not compile.
using Windows.Foundation;
using Windows.UI;

namespace Macshot.Windows.Toolbar;

/// <summary>
/// macshot's own colour picker: presets, saveable slots, a hue/saturation square, a
/// brightness bar, an opacity bar and the hex.
/// </summary>
/// <remarks>
/// <para>
/// Written rather than restyled. WinUI's <c>ColorPicker</c> carries hue, brightness,
/// alpha and hex, so nothing was unreachable with it — but it has no preset swatches and
/// no saveable slots, and the twelve-swatch grid is how a colour is chosen nine times in
/// ten. A picker where the common case takes a drag across a gradient is a slower tool
/// than the one on the other platform, for the thing people do most.
/// </para>
/// <para>
/// Every metric is macshot's — <c>ColorPickerView.swift:36–49</c> — so the popover is the
/// same size and shape on both machines.
/// </para>
/// <para>
/// The square is three layers of gradient rather than a bitmap: a rainbow across it, white
/// down it at the saturation's complement, and black over the pair at the brightness's.
/// Composited that is exactly HSB — <c>b × ((1 − s) + s × hue)</c> — with no per-pixel
/// loop, and moving the brightness bar recolours it in one property change rather than
/// rebuilding 140 × 140 pixels on the UI thread.
/// </para>
/// </remarks>
internal sealed class ColorPickerView : UserControl
{
    private const double Padding = 6;
    private const double SwatchSize = 24;
    private const int Columns = 6;
    private const double CustomSlotSize = 20;
    private const double CustomSlotSpacing = 6;
    private const double OpacityBarHeight = 12;
    private const double GradientSize = 140;
    private const double BrightnessBarHeight = 16;
    private const double HexRowHeight = 22;

    private const double PickerWidth = (Columns * (SwatchSize + Padding)) + Padding;
    private const double ContentWidth = PickerWidth - (Padding * 2);

    /// <summary>
    /// The twelve macshot offers, in its order — <c>ColorPickerView.swift:37–40</c>. The
    /// six system hues, then pink, then white through black.
    /// </summary>
    private static readonly Color[] Presets =
    [
        Color.FromArgb(255, 255, 59, 48),
        Color.FromArgb(255, 255, 149, 0),
        Color.FromArgb(255, 255, 204, 0),
        Color.FromArgb(255, 52, 199, 89),
        Color.FromArgb(255, 0, 122, 255),
        Color.FromArgb(255, 175, 82, 222),
        Color.FromArgb(255, 255, 45, 85),
        Color.FromArgb(255, 255, 255, 255),
        Color.FromArgb(255, 170, 170, 170),
        Color.FromArgb(255, 128, 128, 128),
        Color.FromArgb(255, 85, 85, 85),
        Color.FromArgb(255, 0, 0, 0),
    ];

    private readonly List<Border> _presetSwatches = [];
    private readonly List<Border> _customSlots = [];
    private readonly Color?[] _custom = new Color?[CustomSlotCount];

    private readonly Canvas _square = new() { Width = ContentWidth, Height = GradientSize };
    private readonly Ellipse _squareRing = new();
    private readonly Ellipse _squareDot = new();
    private readonly Rectangle _saturation = new();
    private readonly Rectangle _value = new();

    private readonly Canvas _brightness = new() { Width = ContentWidth, Height = BrightnessBarHeight };
    private readonly Rectangle _brightnessTrack = new();
    private readonly Rectangle _brightnessThumb = NewThumb(BrightnessBarHeight);

    private readonly Canvas _opacity = new() { Width = ContentWidth, Height = OpacityBarHeight };
    private readonly Rectangle _opacityTrack = new();
    private readonly Rectangle _opacityThumb = NewThumb(OpacityBarHeight);
    private readonly TextBlock _opacityLabel = new();

    private readonly Ellipse _hexPreview = new() { Width = 12, Height = 12 };
    private readonly TextBlock _hexText = new();

    private double _hue;
    private double _saturationValue = 1;
    private double _brightnessValue = 1;
    private double _alpha = 1;
    private int _selectedSlot = -1;
    private bool _quiet;

    public ColorPickerView()
    {
        var stack = new StackPanel
        {
            Width = PickerWidth,
            Padding = new Thickness(Padding),
            Spacing = Padding,
            RequestedTheme = ElementTheme.Dark,
            Background = ToolbarPalette.BackgroundBrush,
        };

        stack.Children.Add(BuildPresets());
        stack.Children.Add(BuildCustomSlots());
        stack.Children.Add(BuildOpacityBar());
        stack.Children.Add(BuildSquare());
        stack.Children.Add(BuildBrightnessBar());
        stack.Children.Add(BuildHexRow());

        Content = stack;
        Repaint();
    }

    /// <summary>How many colours of their own the user can keep. macshot's seven.</summary>
    public const int CustomSlotCount = 7;

    /// <summary>Raised whenever the colour changes by the user's hand, never by code.</summary>
    public event EventHandler<Color>? ColorChanged;

    /// <summary>Raised when a slot is filled or cleared, so the host can write them down.</summary>
    public event EventHandler<IReadOnlyList<string>>? CustomColorsChanged;

    /// <summary>The colour in hand, opacity included.</summary>
    public Color Color
    {
        get
        {
            var (red, green, blue) = ToRgb(_hue, _saturationValue, _brightnessValue);
            return Color.FromArgb((byte)Math.Round(_alpha * 255), red, green, blue);
        }

        set
        {
            _quiet = true;
            try
            {
                _alpha = value.A / 255d;
                (_hue, _saturationValue, _brightnessValue) = ToHsb(value);
                Repaint();
            }
            finally
            {
                _quiet = false;
            }
        }
    }

    /// <summary>Fills the saved slots from the settings file, in its order.</summary>
    public void LoadCustomColors(IReadOnlyList<string> hex)
    {
        ArgumentNullException.ThrowIfNull(hex);

        for (var index = 0; index < _custom.Length; index++)
        {
            _custom[index] = index < hex.Count
                && Core.Annotations.AnnotationColor.TryParseHex(hex[index], out var parsed)
                    ? Color.FromArgb(parsed.Alpha, parsed.Red, parsed.Green, parsed.Blue)
                    : null;
        }

        RepaintCustomSlots();
    }

    private FrameworkElement BuildPresets()
    {
        var rows = new StackPanel { Spacing = Padding };

        for (var row = 0; row * Columns < Presets.Length; row++)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Padding };
            for (var column = 0; column < Columns; column++)
            {
                var index = (row * Columns) + column;
                if (index >= Presets.Length)
                {
                    break;
                }

                var preset = Presets[index];
                var swatch = new Border
                {
                    Width = SwatchSize,
                    Height = SwatchSize,
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(preset),
                    BorderThickness = new Thickness(2),
                    BorderBrush = ToolbarPalette.TransparentBrush,
                };

                swatch.PointerPressed += (_, args) =>
                {
                    args.Handled = true;
                    Pick(preset, fromSlot: -1);
                };

                _presetSwatches.Add(swatch);
                line.Children.Add(swatch);
            }

            rows.Children.Add(line);
        }

        return rows;
    }

    private FrameworkElement BuildCustomSlots()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = CustomSlotSpacing,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        for (var index = 0; index < CustomSlotCount; index++)
        {
            var slot = new Border
            {
                Width = CustomSlotSize,
                Height = CustomSlotSize,
                CornerRadius = new CornerRadius(CustomSlotSize / 2),
                BorderThickness = new Thickness(1),
            };

            var which = index;
            ToolTipService.SetToolTip(slot, "Click to use, right-click to save the colour in hand");

            slot.PointerPressed += (_, args) =>
            {
                args.Handled = true;

                // A press on a filled slot uses it; a press on an empty one, or a
                // right-click on any, saves what is in hand. Saving on the alternate
                // button is what keeps a slot from being overwritten by the click that
                // was meant to select it.
                var alternate = args.GetCurrentPoint(slot).Properties.IsRightButtonPressed;
                if (!alternate && _custom[which] is { } saved)
                {
                    Pick(saved, which);
                    return;
                }

                _custom[which] = Color;
                _selectedSlot = which;
                RepaintCustomSlots();
                CustomColorsChanged?.Invoke(this, SavedColors());
            };

            _customSlots.Add(slot);
            row.Children.Add(slot);
        }

        return row;
    }

    private FrameworkElement BuildSquare()
    {
        // The rainbow: the six pure hues and back to red, so the right edge meets the
        // left one and no hue is missing at the seam.
        var hues = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 0) };
        for (var step = 0; step <= 6; step++)
        {
            var (red, green, blue) = ToRgb(step / 6d, 1, 1);
            hues.GradientStops.Add(new GradientStop
            {
                Offset = step / 6d,
                Color = Color.FromArgb(255, red, green, blue),
            });
        }

        var rainbow = new Rectangle { Width = ContentWidth, Height = GradientSize, Fill = hues };

        _saturation.Width = ContentWidth;
        _saturation.Height = GradientSize;
        _saturation.Fill = new LinearGradientBrush
        {
            StartPoint = new(0, 0),
            EndPoint = new(0, 1),
            GradientStops =
            {
                new GradientStop { Offset = 0, Color = Color.FromArgb(0, 255, 255, 255) },
                new GradientStop { Offset = 1, Color = Color.FromArgb(255, 255, 255, 255) },
            },
        };

        _value.Width = ContentWidth;
        _value.Height = GradientSize;
        _value.Fill = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));

        // The crosshair macshot draws: a dark ring under a white one, so it reads on a
        // pale corner of the square as well as on a saturated one.
        _squareRing.Width = 10;
        _squareRing.Height = 10;
        _squareRing.Stroke = new SolidColorBrush(Color.FromArgb(153, 0, 0, 0));
        _squareRing.StrokeThickness = 1;
        _squareDot.Width = 8;
        _squareDot.Height = 8;
        _squareDot.Stroke = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
        _squareDot.StrokeThickness = 1.5;

        _square.Children.Add(rainbow);
        _square.Children.Add(_saturation);
        _square.Children.Add(_value);
        _square.Children.Add(_squareRing);
        _square.Children.Add(_squareDot);

        Track(_square, point =>
        {
            _hue = Math.Clamp(point.X / ContentWidth, 0, 1);
            _saturationValue = Math.Clamp(1 - (point.Y / GradientSize), 0, 1);
            Changed();
        });

        return Clip(_square, bordered: false);
    }

    private FrameworkElement BuildBrightnessBar()
    {
        _brightnessTrack.Width = ContentWidth;
        _brightnessTrack.Height = BrightnessBarHeight;
        _brightness.Children.Add(_brightnessTrack);
        _brightness.Children.Add(_brightnessThumb);

        Track(_brightness, point =>
        {
            _brightnessValue = Math.Clamp(point.X / ContentWidth, 0, 1);
            Changed();
        });

        return _brightness;
    }

    private FrameworkElement BuildOpacityBar()
    {
        // The checkerboard behind it, so a colour at a tenth reads as translucent rather
        // than as a slightly different shade of the popover.
        var check = OpacityBarHeight / 2;
        for (var column = 0; column * check < ContentWidth; column++)
        {
            for (var row = 0; row < 2; row++)
            {
                var square = new Rectangle
                {
                    Width = Math.Min(check, ContentWidth - (column * check)),
                    Height = check,
                    Fill = new SolidColorBrush((column + row) % 2 == 0
                        ? Color.FromArgb(255, 128, 128, 128)
                        : Color.FromArgb(255, 179, 179, 179)),
                };

                Canvas.SetLeft(square, column * check);
                Canvas.SetTop(square, row * check);
                _opacity.Children.Add(square);
            }
        }

        _opacityTrack.Width = ContentWidth;
        _opacityTrack.Height = OpacityBarHeight;
        _opacity.Children.Add(_opacityTrack);

        _opacityLabel.FontSize = 8;
        _opacityLabel.FontWeight = FontWeights.Medium;
        _opacityLabel.Foreground = ToolbarPalette.IconBrush(0.8);
        _opacityLabel.TextAlignment = TextAlignment.Right;
        _opacityLabel.Width = ContentWidth - 2;
        Typography.SetNumeralAlignment(_opacityLabel, FontNumeralAlignment.Tabular);
        Canvas.SetTop(_opacityLabel, (OpacityBarHeight - 11) / 2);
        _opacity.Children.Add(_opacityLabel);
        _opacity.Children.Add(_opacityThumb);

        Track(_opacity, point =>
        {
            _alpha = Math.Clamp(point.X / ContentWidth, 0, 1);
            Changed();
        });

        return Clip(_opacity);
    }

    private FrameworkElement BuildHexRow()
    {
        _hexPreview.Stroke = ToolbarPalette.IconBrush(0.3);
        _hexPreview.StrokeThickness = 1;
        _hexPreview.VerticalAlignment = VerticalAlignment.Center;

        _hexText.FontFamily = new FontFamily("Consolas");
        _hexText.FontSize = 11;
        _hexText.Foreground = ToolbarPalette.IconBrush(0.9);
        _hexText.VerticalAlignment = VerticalAlignment.Center;

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Padding = new Thickness(6, 0, 6, 0),
            Children = { _hexPreview, _hexText },
        };

        return new Border
        {
            Width = ContentWidth,
            Height = HexRowHeight,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(204, 51, 51, 51)),
            Child = row,
        };
    }

    /// <summary>
    /// Rounds a canvas's corners the way macshot clips its bars and square. The opacity
    /// bar is the one with a hairline round it — the square is edge to edge.
    /// </summary>
    private static Border Clip(FrameworkElement content, bool bordered = true) => new()
    {
        CornerRadius = new CornerRadius(4),
        BorderThickness = new Thickness(bordered ? 0.5 : 0),
        BorderBrush = ToolbarPalette.IconBrush(0.3),
        Child = content,
    };

    private static Rectangle NewThumb(double barHeight) => new()
    {
        Width = 8,
        Height = barHeight + 4,
        RadiusX = 3,
        RadiusY = 3,
        Fill = ToolbarPalette.IconBrush(),
        Stroke = new SolidColorBrush(Color.FromArgb(77, 0, 0, 0)),
        StrokeThickness = 1,
    };

    /// <summary>
    /// Wires a press and any drag after it into <paramref name="onPoint"/>, in the
    /// element's own coordinates.
    /// </summary>
    /// <remarks>
    /// The pointer is captured on the press, so a drag that leaves the square — which is
    /// what picking a fully saturated colour looks like — keeps updating instead of
    /// stopping at the edge.
    /// </remarks>
    private static void Track(UIElement element, Action<Point> onPoint)
    {
        var dragging = false;

        element.PointerPressed += (sender, args) =>
        {
            args.Handled = true;
            dragging = element.CapturePointer(args.Pointer);
            onPoint(args.GetCurrentPoint(element).Position);
        };

        element.PointerMoved += (sender, args) =>
        {
            if (dragging)
            {
                args.Handled = true;
                onPoint(args.GetCurrentPoint(element).Position);
            }
        };

        element.PointerReleased += (sender, args) =>
        {
            if (!dragging)
            {
                return;
            }

            dragging = false;
            element.ReleasePointerCapture(args.Pointer);
        };

        element.PointerCaptureLost += (sender, args) => dragging = false;
    }

    private void Pick(Color color, int fromSlot)
    {
        _alpha = color.A / 255d;
        (_hue, _saturationValue, _brightnessValue) = ToHsb(color);
        _selectedSlot = fromSlot;
        Changed();
    }

    private void Changed()
    {
        Repaint();
        if (!_quiet)
        {
            ColorChanged?.Invoke(this, Color);
        }
    }

    private void Repaint()
    {
        var (red, green, blue) = ToRgb(_hue, _saturationValue, _brightnessValue);
        var opaque = Color.FromArgb(255, red, green, blue);

        // The square's third layer: black at the brightness's complement, which is what
        // makes the whole square dim with the bar rather than only the chosen colour.
        _value.Fill = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round((1 - _brightnessValue) * 255),
            0,
            0,
            0));

        Canvas.SetLeft(_squareRing, (_hue * ContentWidth) - 5);
        Canvas.SetTop(_squareRing, ((1 - _saturationValue) * GradientSize) - 5);
        Canvas.SetLeft(_squareDot, (_hue * ContentWidth) - 4);
        Canvas.SetTop(_squareDot, ((1 - _saturationValue) * GradientSize) - 4);

        var (fullRed, fullGreen, fullBlue) = ToRgb(_hue, _saturationValue, 1);
        _brightnessTrack.Fill = new LinearGradientBrush
        {
            StartPoint = new(0, 0),
            EndPoint = new(1, 0),
            GradientStops =
            {
                new GradientStop { Offset = 0, Color = Color.FromArgb(255, 0, 0, 0) },
                new GradientStop { Offset = 1, Color = Color.FromArgb(255, fullRed, fullGreen, fullBlue) },
            },
        };

        Canvas.SetLeft(_brightnessThumb, (_brightnessValue * ContentWidth) - 4);
        Canvas.SetTop(_brightnessThumb, -2);

        _opacityTrack.Fill = new LinearGradientBrush
        {
            StartPoint = new(0, 0),
            EndPoint = new(1, 0),
            GradientStops =
            {
                new GradientStop { Offset = 0, Color = Color.FromArgb(0, red, green, blue) },
                new GradientStop { Offset = 1, Color = opaque },
            },
        };

        Canvas.SetLeft(_opacityThumb, (_alpha * ContentWidth) - 4);
        Canvas.SetTop(_opacityThumb, -2);
        _opacityLabel.Text = $"{(int)Math.Round(_alpha * 100)}%";

        _hexPreview.Fill = new SolidColorBrush(Color);
        _hexText.Text = $"#{red:X2}{green:X2}{blue:X2}";

        for (var index = 0; index < _presetSwatches.Count; index++)
        {
            // Ringed when it is the colour in hand, which is the only way a grid of
            // squares can say which one was clicked.
            _presetSwatches[index].BorderBrush = Presets[index] == opaque
                ? ToolbarPalette.IconBrush()
                : ToolbarPalette.TransparentBrush;
        }

        RepaintCustomSlots();
    }

    private void RepaintCustomSlots()
    {
        for (var index = 0; index < _customSlots.Count; index++)
        {
            var slot = _customSlots[index];
            if (_custom[index] is { } saved)
            {
                slot.Background = new SolidColorBrush(saved);
                slot.BorderThickness = new Thickness(_selectedSlot == index ? 2.5 : 0);
                slot.BorderBrush = ToolbarPalette.IconBrush();
            }
            else
            {
                // An empty slot is an outline rather than a hole: it has to read as a
                // place a colour can go, not as a gap in the row.
                slot.Background = ToolbarPalette.TransparentBrush;
                slot.BorderThickness = new Thickness(_selectedSlot == index ? 2 : 1);
                slot.BorderBrush = ToolbarPalette.IconBrush(_selectedSlot == index ? 0.5 : 0.2);
            }
        }
    }

    private IReadOnlyList<string> SavedColors() =>
    [
        .. _custom.Select(color => color is { } saved
            ? new Core.Annotations.AnnotationColor(saved.R, saved.G, saved.B, saved.A).ToHex()
            : string.Empty),
    ];

    /// <summary>HSB to RGB, on the same 0–1 axes macshot's picker works in.</summary>
    private static (byte Red, byte Green, byte Blue) ToRgb(double hue, double saturation, double brightness)
    {
        var sector = (hue % 1) * 6;
        var index = (int)Math.Floor(sector) % 6;
        var fraction = sector - Math.Floor(sector);

        var peak = brightness;
        var trough = brightness * (1 - saturation);
        var rising = brightness * (1 - (saturation * (1 - fraction)));
        var falling = brightness * (1 - (saturation * fraction));

        var (red, green, blue) = index switch
        {
            0 => (peak, rising, trough),
            1 => (falling, peak, trough),
            2 => (trough, peak, rising),
            3 => (trough, falling, peak),
            4 => (rising, trough, peak),
            _ => (peak, trough, falling),
        };

        return (Byte(red), Byte(green), Byte(blue));
    }

    private static (double Hue, double Saturation, double Brightness) ToHsb(Color color)
    {
        var red = color.R / 255d;
        var green = color.G / 255d;
        var blue = color.B / 255d;

        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var span = max - min;

        double hue;
        if (span <= 0)
        {
            hue = 0;
        }
        else if (max == red)
        {
            hue = ((green - blue) / span % 6) / 6;
        }
        else if (max == green)
        {
            hue = (((blue - red) / span) + 2) / 6;
        }
        else
        {
            hue = (((red - green) / span) + 4) / 6;
        }

        if (hue < 0)
        {
            hue += 1;
        }

        return (hue, max <= 0 ? 0 : span / max, max);
    }

    private static byte Byte(double channel) => (byte)Math.Clamp(Math.Round(channel * 255), 0, 255);
}
