namespace Macshot.Windows.Core.Capture;

/// <summary>
/// The ring that blooms out of a click while a recording is running.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>MouseHighlightOverlay</c>, number for number: a disc that starts at 18
/// and grows by 60 a second, filled at 0.35 and stroked at 0.6 under a fade that empties
/// it in 0.3 s. It exists because a recording of someone clicking shows nothing at all —
/// the pointer moves, something happens, and the viewer has to infer the press.
/// </para>
/// <para>
/// Rasterized here rather than drawn by the UI framework because the ring has to reach the
/// screen through <c>UpdateLayeredWindow</c>, which takes premultiplied BGRA and nothing
/// else: a WinUI window has no per-pixel transparency, so a disc at 0.35 over the desktop
/// cannot be a WinUI window at all. Being pixels rather than XAML also makes the shape
/// testable, which is the half of this that is worth pinning — the alphas and the growth
/// rate are what make it read as macshot's rather than as a generic click ripple.
/// </para>
/// </remarks>
public static class ClickHighlightRing
{
    /// <summary>How long a ring lasts, in seconds — <c>MouseHighlightOverlay.swift:71</c>.</summary>
    public const double Lifetime = 0.3;

    /// <summary>The radius a ring starts at.</summary>
    public const double StartRadius = 18;

    /// <summary>How fast it grows, in points a second.</summary>
    public const double Growth = 60;

    /// <summary>The disc's alpha at full opacity.</summary>
    public const double FillAlpha = 0.35;

    /// <summary>The outline's alpha at full opacity.</summary>
    public const double StrokeAlpha = 0.6;

    /// <summary>How wide the outline is drawn.</summary>
    public const double StrokeWidth = 2;

    /// <summary>How far inside the disc the outline is centred — macshot's <c>insetBy(2)</c>.</summary>
    public const double StrokeInset = 2;

    /// <summary>
    /// The side of the buffer a ring is drawn into at 100%, big enough for the ring at
    /// its largest so the window can be made once and only its content redrawn.
    /// </summary>
    public static int Extent { get; } = ExtentAt(1);

    /// <summary>
    /// The side of the buffer at <paramref name="scale"/>, in that display's own pixels.
    /// </summary>
    /// <remarks>
    /// macshot's 18 and 60 are points. Drawn as pixels on a 200% display the ring would
    /// come out half the size it is meant to be — small enough on a dense screen to be
    /// missed, which defeats the whole point of drawing it.
    /// </remarks>
    public static int ExtentAt(double scale) =>
        (int)Math.Ceiling((StartRadius + (Growth * Lifetime)) * 2 * Math.Max(scale, 0.1)) + 2;

    /// <summary>macOS <c>systemYellow</c>, which is what macshot draws the ring in.</summary>
    private const byte Red = 255;
    private const byte Green = 204;
    private const byte Blue = 0;

    /// <summary>The radius at <paramref name="age"/> seconds after the click.</summary>
    public static double RadiusAt(double age) => StartRadius + (Growth * age);

    /// <summary>
    /// What is left of the ring at <paramref name="age"/> seconds — 1 at the click,
    /// 0 once it is over.
    /// </summary>
    public static double FadeAt(double age) => Math.Clamp(1 - (age / Lifetime), 0, 1);

    /// <summary>Whether a ring this old is still worth drawing.</summary>
    public static bool IsAlive(double age) => age >= 0 && age < Lifetime;

    /// <summary>
    /// Draws the ring at <paramref name="age"/> into an <see cref="ExtentAt"/>-square
    /// buffer of premultiplied BGRA, centred, and returns the number of pixels that
    /// carry any colour at all.
    /// </summary>
    /// <remarks>
    /// The caller owns the buffer and passes the same one every tick: a ring is redrawn
    /// thirty times a second and allocating 20 KB each time, per ring, would make the
    /// highlight the most expensive thing on screen during a recording.
    /// </remarks>
    /// <returns>How many pixels were written to. Zero means nothing is left to show.</returns>
    public static int Rasterize(double age, double scale, byte[] bgra)
    {
        ArgumentNullException.ThrowIfNull(bgra);

        var extent = ExtentAt(scale);
        ArgumentOutOfRangeException.ThrowIfLessThan(bgra.Length, extent * extent * 4);

        Array.Clear(bgra, 0, extent * extent * 4);

        var fade = FadeAt(age);
        if (!IsAlive(age) || fade <= 0)
        {
            return 0;
        }

        var radius = RadiusAt(age) * scale;
        var centre = extent / 2.0;

        // The outline sits on the inset oval's own edge, so it spans a band one half of
        // its width either side of that radius.
        var band = radius - (StrokeInset * scale);
        var inner = band - (StrokeWidth * scale / 2);
        var outer = band + (StrokeWidth * scale / 2);

        var filled = 0;

        for (var y = 0; y < extent; y++)
        {
            var dy = y + 0.5 - centre;

            for (var x = 0; x < extent; x++)
            {
                var dx = x + 0.5 - centre;
                var distance = Math.Sqrt((dx * dx) + (dy * dy));

                // Coverage from distance rather than from supersampling: a circle's edge
                // crosses a pixel almost straight at these radii, so the error is under a
                // hundredth of a level and the cost is one subtraction instead of sixteen
                // distance tests.
                var disc = Math.Clamp(radius + 0.5 - distance, 0, 1);
                var ring = Math.Clamp(outer + 0.5 - distance, 0, 1)
                    * Math.Clamp(distance - inner + 0.5, 0, 1);

                var alpha = (FillAlpha * fade * disc) + (StrokeAlpha * fade * ring * (1 - (FillAlpha * fade * disc)));
                if (alpha <= 0)
                {
                    continue;
                }

                var scaled = Math.Clamp(alpha, 0, 1);
                var offset = ((y * extent) + x) * 4;

                // Premultiplied, because that is the only form UpdateLayeredWindow reads.
                bgra[offset] = (byte)Math.Round(Blue * scaled);
                bgra[offset + 1] = (byte)Math.Round(Green * scaled);
                bgra[offset + 2] = (byte)Math.Round(Red * scaled);
                bgra[offset + 3] = (byte)Math.Round(255 * scaled);
                filled++;
            }
        }

        return filled;
    }
}
