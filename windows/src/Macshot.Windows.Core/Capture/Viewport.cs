namespace Macshot.Windows.Core.Capture;

/// <summary>
/// How much of the capture is on show and where: a scale and an offset.
/// </summary>
/// <remarks>
/// <para>
/// The overlay covers the display at 1:1, which is the wrong magnification for choosing a
/// region a few pixels across — a button's border, a line of small text. Zooming in makes
/// the same drag land where the user means it to.
/// </para>
/// <para>
/// Never smaller than the display it is over. There is no point showing less than the
/// screen when the screen is exactly what is being captured, and letting the content
/// shrink away from the edges would leave the overlay with a border of nothing.
/// </para>
/// </remarks>
public readonly record struct Viewport(double Scale, double OffsetX, double OffsetY)
{
    /// <summary>The whole capture, unmagnified: what every overlay starts at.</summary>
    public static Viewport Identity { get; } = new(1, 0, 0);

    /// <summary>As far in as the overlay will go.</summary>
    public const double MaxScale = 8;

    /// <summary>
    /// As far out as it will go, which is all the way out: the display is the capture.
    /// </summary>
    public const double MinScale = 1;

    public bool IsIdentity => Scale == 1 && OffsetX == 0 && OffsetY == 0;

    /// <summary>Where a point on the capture appears on screen.</summary>
    public CapturePoint ToView(CapturePoint content) =>
        new((content.X * Scale) + OffsetX, (content.Y * Scale) + OffsetY);

    /// <summary>Where a point on screen falls on the capture.</summary>
    public CapturePoint ToContent(CapturePoint view) =>
        new((view.X - OffsetX) / Scale, (view.Y - OffsetY) / Scale);

    /// <summary>Where a region of the capture appears on screen.</summary>
    public CaptureRegion ToView(CaptureRegion content) => new(
        (content.X * Scale) + OffsetX,
        (content.Y * Scale) + OffsetY,
        content.Width * Scale,
        content.Height * Scale);

    /// <summary>
    /// Magnified by <paramref name="factor"/> about a point on screen, so whatever is
    /// under the pointer stays under it.
    /// </summary>
    /// <remarks>
    /// About the pointer rather than about the middle. Zooming is aimed at something, and
    /// a zoom that pulled that something off to one side would have to be followed by a
    /// pan every single time.
    /// </remarks>
    public Viewport ZoomedAt(double factor, CapturePoint anchor, CaptureRegion view)
    {
        var scale = Math.Clamp(Scale * factor, MinScale, MaxScale);
        if (scale == Scale)
        {
            return this;
        }

        var held = ToContent(anchor);

        return new Viewport(scale, anchor.X - (held.X * scale), anchor.Y - (held.Y * scale)).Clamped(view);
    }

    /// <summary>The same view moved by a drag, kept over the display.</summary>
    public Viewport PannedBy(double deltaX, double deltaY, CaptureRegion view) =>
        new Viewport(Scale, OffsetX + deltaX, OffsetY + deltaY).Clamped(view);

    /// <summary>
    /// Pulled back so the capture still covers the whole overlay. An offset that let the
    /// edge in would show a strip of empty window where the screen ought to be.
    /// </summary>
    private Viewport Clamped(CaptureRegion view)
    {
        var slackX = Math.Max(0, (view.Width * Scale) - view.Width);
        var slackY = Math.Max(0, (view.Height * Scale) - view.Height);

        return this with
        {
            OffsetX = Math.Clamp(OffsetX, -slackX, 0),
            OffsetY = Math.Clamp(OffsetY, -slackY, 0),
        };
    }
}
