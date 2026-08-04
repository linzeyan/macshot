namespace Macshot.Windows.Core.Capture;

/// <summary>
/// The rectangle of a followed window's frames that a recording keeps, decided once and
/// then held for the whole recording.
/// </summary>
/// <remarks>
/// <para>
/// Recording a window is not recording a moving rectangle of the desktop. The window's
/// own capture item follows it, so a window dragged across two displays needs no tracking
/// at all: the frames keep arriving and keep holding the window. What the compositor does
/// <em>not</em> absorb is a resize, because the encoder is told a width and a height once
/// and a sample of any other size is a corrupt frame.
/// </para>
/// <para>
/// So the size is pinned instead of followed, and every frame is fitted into it. A window
/// that grows is recorded at the size it started — the new pixels are outside the
/// rectangle. A window that shrinks leaves the vacated part transparent black rather than
/// the pixels that were last there, because a frozen strip of a window that has moved on
/// reads as a rendering fault, while an empty band reads as the window having got smaller.
/// </para>
/// <para>
/// Rounding is <see cref="RecordingPlan"/>'s, and for its reason: H.264 stores colour at
/// half resolution in each direction, so an odd dimension has no whole chroma sample to
/// go in and the encoder refuses the profile.
/// </para>
/// </remarks>
/// <param name="Left">Where the kept rectangle starts in the frame, in whole pixels.</param>
public readonly record struct WindowRecordingArea(int Left, int Top, int Width, int Height)
{
    /// <summary>The kept rectangle, for the callers that measure in <see cref="CaptureRegion"/>.</summary>
    public CaptureRegion AsRegion => new(Left, Top, Width, Height);

    /// <summary>
    /// Works out what to keep of a window whose frames arrive at
    /// <paramref name="frameWidth"/> by <paramref name="frameHeight"/>.
    /// </summary>
    /// <remarks>
    /// The invisible resize border comes off first, through <see cref="WindowFrameCrop"/>,
    /// so a recording of a window is the window rather than the window with a transparent
    /// band down three sides. Everything after that is the rounding H.264 needs, and a
    /// clamp: rounding a rectangle at the edge of the frame outward would push it past the
    /// buffer, so the corner gives way rather than the size — the size is what the encoder
    /// was told.
    /// </remarks>
    /// <param name="windowRect">The window's outer rectangle, borders included.</param>
    /// <param name="visibleBounds">The window as drawn, in the same space.</param>
    public static WindowRecordingArea Resolve(
        CaptureRegion windowRect,
        CaptureRegion visibleBounds,
        int frameWidth,
        int frameHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameHeight);

        var visible = WindowFrameCrop.Resolve(windowRect, visibleBounds, frameWidth, frameHeight);

        var left = (int)Math.Floor(visible.X);
        var top = (int)Math.Floor(visible.Y);
        var width = ToEven((int)Math.Floor(visible.Right) - left);
        var height = ToEven((int)Math.Floor(visible.Bottom) - top);

        left = Math.Max(0, Math.Min(left, frameWidth - width));
        top = Math.Max(0, Math.Min(top, frameHeight - height));

        return new WindowRecordingArea(left, top, width, height);
    }

    /// <summary>
    /// Cuts the kept rectangle out of one arrived frame, into a buffer of exactly the size
    /// the encoder was promised.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="contentWidth"/> and <paramref name="contentHeight"/> are how much of
    /// the buffer is this frame rather than the last one's. They differ from the buffer's
    /// own size whenever the window has been resized, because the frame pool is not rebuilt
    /// underneath a running recording: the pool was made at the size the window started at,
    /// which is the largest the pinned rectangle can ever need, and a shrunken window is
    /// delivered into the top-left of it with the remainder left as it was. Reading that
    /// remainder would record the moment before the resize forever.
    /// </para>
    /// <para>
    /// A window that is minimized delivers nothing, or delivers nothing of the rectangle —
    /// either way the sample comes back the right size and empty, so the recording carries
    /// on with a blank picture rather than stopping. Stopping is what a window being
    /// <em>closed</em> means, and that is the capture item's to say, not this.
    /// </para>
    /// </remarks>
    public byte[] Fit(
        int frameWidth,
        int frameHeight,
        ReadOnlySpan<byte> bgraPixels,
        int contentWidth,
        int contentHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameHeight);
        ArgumentOutOfRangeException.ThrowIfNegative(Left);
        ArgumentOutOfRangeException.ThrowIfNegative(Top);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Height);

        if (bgraPixels.Length != checked(frameWidth * frameHeight * 4))
        {
            throw new ArgumentException(
                "The pixel buffer does not match the frame dimensions.",
                nameof(bgraPixels));
        }

        var output = new byte[checked(Width * Height * 4)];

        var right = Math.Min(Left + Width, Math.Min(contentWidth, frameWidth));
        var bottom = Math.Min(Top + Height, Math.Min(contentHeight, frameHeight));
        var kept = right - Left;
        if (kept <= 0 || bottom <= Top)
        {
            return output;
        }

        for (var row = Top; row < bottom; row++)
        {
            var from = ((row * frameWidth) + Left) * 4;
            bgraPixels.Slice(from, kept * 4).CopyTo(output.AsSpan((row - Top) * Width * 4));
        }

        return output;
    }

    /// <summary>
    /// The next even number at or below <paramref name="value"/>, but never zero: a window
    /// two pixels wide is absurd rather than impossible, and it still has to produce a
    /// profile the encoder will accept.
    /// </summary>
    private static int ToEven(int value) => Math.Max(2, value - (value % 2));
}
