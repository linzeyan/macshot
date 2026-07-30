using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Annotations;

/// <summary>
/// The ring of colours a right-click opens under the pointer.
/// </summary>
/// <remarks>
/// <para>
/// Changing colour is the most frequent thing anyone does while marking a capture up, and
/// the colour button is wherever the toolbar happens to be — which is a round trip across
/// the screen for every arrow that needs to be a different colour from the last one. The
/// ring opens where the pointer already is, so the trip is a flick of the wrist.
/// </para>
/// <para>
/// Twelve hues and four neutrals on one ring, evenly spaced, starting at the bottom and
/// going anticlockwise — the same wheel the macOS app draws, so muscle memory carries.
/// Which swatch a point lands on is answered by angle alone: the wheel is a gesture, and
/// having to also stop at the right distance would make it a target.
/// </para>
/// </remarks>
public static class ColorWheel
{
    /// <summary>How far the swatch centres sit from the middle.</summary>
    public const double Radius = 72;

    /// <summary>How big each swatch is.</summary>
    public const double SwatchRadius = 12;

    /// <summary>
    /// The hole in the middle, where nothing is chosen — so a right-click that opens the
    /// wheel and goes nowhere leaves the colour alone.
    /// </summary>
    public const double DeadZone = Radius * 0.25;

    public static IReadOnlyList<AnnotationColor> Colors { get; } =
    [
        .. Enumerable.Range(0, 12).Select(step => FromHue(step / 12.0)),
        new AnnotationColor(255, 255, 255),
        new AnnotationColor(179, 179, 179),
        new AnnotationColor(102, 102, 102),
        new AnnotationColor(0, 0, 0),
    ];

    /// <summary>Where swatch <paramref name="index"/> sits around <paramref name="center"/>.</summary>
    /// <remarks>
    /// Top-left origin, so the sine is subtracted rather than added: the first swatch is
    /// the one directly below the pointer, as it is on macOS.
    /// </remarks>
    public static CapturePoint SwatchAt(CapturePoint center, int index)
    {
        var angle = Angle(index);
        return new CapturePoint(
            center.X + (Radius * Math.Cos(angle)),
            center.Y - (Radius * Math.Sin(angle)));
    }

    /// <summary>
    /// Which swatch a point picks, or -1 for none. Distance only decides whether anything
    /// is picked at all; past the dead zone it is the angle that answers.
    /// </summary>
    public static int IndexAt(CapturePoint center, CapturePoint point)
    {
        var dx = point.X - center.X;

        // Back into the same orientation the arithmetic was written in, where up is
        // positive — the wheel is a ring of angles, and it is easier to keep one flip here
        // than to mirror every step of it.
        var dy = -(point.Y - center.Y);

        if (Math.Sqrt((dx * dx) + (dy * dy)) < DeadZone)
        {
            return -1;
        }

        var step = 2 * Math.PI / Colors.Count;
        var angle = Math.Atan2(dy, dx) + (Math.PI / 2);
        if (angle < 0)
        {
            angle += 2 * Math.PI;
        }

        return (int)((angle + (step / 2)) / step) % Colors.Count;
    }

    /// <summary>The colour at an index, or null when nothing is picked.</summary>
    public static AnnotationColor? ColorAt(int index) =>
        index >= 0 && index < Colors.Count ? Colors[index] : null;

    private static double Angle(int index) => (-Math.PI / 2) + (index * 2 * Math.PI / Colors.Count);

    /// <summary>
    /// A hue at the saturation and brightness the wheel uses. Full brightness and a little
    /// off full saturation: pure hues are hard to read against a photograph, and a wheel
    /// of them all at once is harder still.
    /// </summary>
    private static AnnotationColor FromHue(double hue)
    {
        const double saturation = 0.85;
        const double value = 1.0;

        var sector = hue * 6;
        var offset = sector - Math.Floor(sector);
        var p = value * (1 - saturation);
        var q = value * (1 - (saturation * offset));
        var t = value * (1 - (saturation * (1 - offset)));

        var (red, green, blue) = ((int)Math.Floor(sector) % 6) switch
        {
            0 => (value, t, p),
            1 => (q, value, p),
            2 => (p, value, t),
            3 => (p, q, value),
            4 => (t, p, value),
            _ => (value, p, q),
        };

        return new AnnotationColor(Channel(red), Channel(green), Channel(blue));
    }

    private static byte Channel(double component) => (byte)Math.Clamp(Math.Round(component * 255), 0, 255);
}
