namespace Macshot.Windows.Core.Capture;

/// <summary>
/// The dark pill that shows what was just typed, for a recording that is teaching
/// somebody something.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>KeystrokeOverlay</c> numbers. It sits at the bottom middle of the recorded
/// region, holds for a second and a half, then fades — long enough to read, short enough
/// that the next key is not queued behind it.
/// </para>
/// <para>
/// Rasterized here rather than drawn by the platform for the same reason the click ring is:
/// what goes on screen is a premultiplied buffer handed to <c>UpdateLayeredWindow</c>,
/// which is the only route on Windows to a shape whose every pixel carries its own alpha.
/// The one thing this cannot do is measure and draw the text — that is a font engine's
/// job, so the caller passes the finished glyphs in as a coverage mask.
/// </para>
/// </remarks>
public static class KeystrokePill
{
    /// <summary>Medium weight, in points at the 96-DPI baseline.</summary>
    public const double FontSize = 28;

    public const double PaddingHorizontal = 24;
    public const double PaddingVertical = 14;
    public const double CornerRadius = 14;

    /// <summary>How far the pill's foot sits above the recorded region's bottom edge.</summary>
    public const double BottomInset = 40;

    /// <summary>How long a keystroke stands at full strength before it starts to go.</summary>
    public const double HoldSeconds = 1.5;

    /// <summary>What each tick of the fade takes off, at <see cref="FadeIntervalMilliseconds"/>.</summary>
    public const double FadeStep = 0.05;

    public const int FadeIntervalMilliseconds = 33;

    private const double FillAlpha = 0.65;
    private const double BorderAlpha = 0.15;

    /// <summary>
    /// The buffer the pill is drawn into, which is fixed while the layered window behind
    /// it lives. Wide enough for a chord nobody would press by accident and a key name to
    /// go with it; anything longer is cut rather than allowed to resize the window on
    /// every keystroke.
    /// </summary>
    public const int BufferWidth = 900;

    /// <summary>Room for a 28-point line and its padding, with a pixel to spare.</summary>
    public const int BufferHeight = 96;

    public static int BufferWidthAt(double scale) => (int)Math.Ceiling(BufferWidth * Math.Max(scale, 0.1));

    public static int BufferHeightAt(double scale) => (int)Math.Ceiling(BufferHeight * Math.Max(scale, 0.1));

    /// <summary>The pill around text that measured this big.</summary>
    public static (int Width, int Height) SizeFor(int textWidth, int textHeight, double scale) =>
        ((int)Math.Ceiling(textWidth + (PaddingHorizontal * 2 * scale)),
            (int)Math.Ceiling(textHeight + (PaddingVertical * 2 * scale)));

    /// <summary>
    /// Draws the pill and the glyphs into <paramref name="bgra"/>, premultiplied, and
    /// answers how tall the pill came out — which is what the caller needs to place the
    /// window, since the pill sits on the buffer's bottom edge.
    /// </summary>
    /// <param name="textMask">
    /// Glyph coverage, one byte per pixel, row-major: 0 where the paper shows through and
    /// 255 in the middle of a stroke. What a font engine gives back when it draws white
    /// text on black.
    /// </param>
    /// <param name="opacity">The whole pill's strength, 1 while it is held and falling after.</param>
    public static int Rasterize(
        byte[] textMask,
        int textWidth,
        int textHeight,
        double opacity,
        double scale,
        byte[] bgra,
        int bufferWidth,
        int bufferHeight)
    {
        ArgumentNullException.ThrowIfNull(textMask);
        ArgumentNullException.ThrowIfNull(bgra);

        Array.Clear(bgra);

        if (opacity <= 0 || textWidth <= 0 || textHeight <= 0)
        {
            return 0;
        }

        var (pillWidth, pillHeight) = SizeFor(textWidth, textHeight, scale);
        pillWidth = Math.Min(pillWidth, bufferWidth);
        pillHeight = Math.Min(pillHeight, bufferHeight);

        var left = (bufferWidth - pillWidth) / 2.0;

        // Bottom-aligned in the buffer, so the caller places the window by the one edge
        // whose distance from the region is fixed.
        var top = (double)(bufferHeight - pillHeight);
        var radius = CornerRadius * scale;
        var centreX = left + (pillWidth / 2.0);
        var centreY = top + (pillHeight / 2.0);
        var insetX = Math.Max((pillWidth / 2.0) - radius, 0);
        var insetY = Math.Max((pillHeight / 2.0) - radius, 0);

        var textLeft = (int)Math.Round(left + (PaddingHorizontal * scale));
        var textTop = (int)Math.Round(top + (PaddingVertical * scale));

        for (var row = (int)top; row < bufferHeight; row++)
        {
            for (var column = 0; column < bufferWidth; column++)
            {
                // Distance to the rounded rectangle's edge: negative inside, and equal to
                // the distance from the corner arc once both axes are past the straight run.
                var dx = Math.Max(Math.Abs((column + 0.5) - centreX) - insetX, 0);
                var dy = Math.Max(Math.Abs((row + 0.5) - centreY) - insetY, 0);
                var distance = Math.Sqrt((dx * dx) + (dy * dy)) - radius;

                var fill = Math.Clamp(0.5 - distance, 0, 1);
                if (fill <= 0)
                {
                    continue;
                }

                // The border's centreline is half a pixel inside the edge, which is where
                // macshot insets its stroke to.
                var border = Math.Clamp(1 - Math.Abs(distance + 0.5), 0, 1);

                // Black first, then the white rim over it, then the glyphs over both.
                // Kept premultiplied throughout: every layer here is pure black or pure
                // white, so the running white is the premultiplied colour already.
                var alpha = FillAlpha * opacity * fill;
                var white = 0.0;

                var rim = BorderAlpha * opacity * border;
                white = rim + (white * (1 - rim));
                alpha = rim + (alpha * (1 - rim));

                var maskColumn = column - textLeft;
                var maskRow = row - textTop;
                if (maskColumn >= 0 && maskColumn < textWidth && maskRow >= 0 && maskRow < textHeight)
                {
                    var glyph = textMask[(maskRow * textWidth) + maskColumn] / 255.0 * opacity;
                    white = glyph + (white * (1 - glyph));
                    alpha = glyph + (alpha * (1 - glyph));
                }

                var offset = ((row * bufferWidth) + column) * 4;
                var channel = (byte)Math.Round(white * 255);
                bgra[offset] = channel;
                bgra[offset + 1] = channel;
                bgra[offset + 2] = channel;
                bgra[offset + 3] = (byte)Math.Round(alpha * 255);
            }
        }

        return pillHeight;
    }
}
