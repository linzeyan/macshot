using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Imaging;

/// <summary>
/// A picture used as the frame's background, in place of one of the gradients.
/// </summary>
/// <remarks>
/// <para>
/// macshot's custom beautify background (<c>OverlayView+Popovers.swift:182</c>), and the
/// reason its options row has a Blur slider at all: the slider is only shown while a
/// picture is the background, because there is nothing to soften about a gradient.
/// </para>
/// <para>
/// A class rather than a value, and held by the window that loaded the image rather than
/// rebuilt from the settings each repaint, because of the memo below. macshot caches the
/// blurred image too (<c>BeautifyRenderer.swift:53</c>) and for the same reason: a slider
/// drag asks for a frame sixty times a second, and blurring a screen-sized picture on
/// each is the difference between a preview that follows the thumb and one that does not.
/// </para>
/// </remarks>
public sealed class BeautifyBackdrop
{
    private readonly Lock _gate = new();

    private readonly byte[] _sharp;

    private byte[]? _blurred;

    private double _blurredBy = -1;

    /// <param name="width">The picture's width in pixels.</param>
    /// <param name="height">Its height.</param>
    /// <param name="bgraPixels">Its pixels, BGRA and top-down.</param>
    public BeautifyBackdrop(int width, int height, byte[] bgraPixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(bgraPixels);

        if (bgraPixels.Length != checked(width * height * 4))
        {
            throw new ArgumentException(
                "The pixel buffer does not match the picture's dimensions.",
                nameof(bgraPixels));
        }

        Width = width;
        Height = height;
        _sharp = bgraPixels;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>
    /// The picture softened by <paramref name="radius"/>, which is the same array every
    /// time the radius has not moved.
    /// </summary>
    /// <remarks>
    /// Locked because an export renders on whichever thread asked for the file while the
    /// preview repaints on the UI one, and the two would otherwise read a half-written
    /// buffer — a frame of noise in a file that cannot be taken again.
    /// </remarks>
    public byte[] PixelsBlurredBy(double radius)
    {
        if (radius <= 0)
        {
            return _sharp;
        }

        lock (_gate)
        {
            if (_blurred is { } cached && _blurredBy == radius)
            {
                return cached;
            }

            var softened = (byte[])_sharp.Clone();
            PixelEffects.Blur(softened, Width, Height, new CaptureRegion(0, 0, Width, Height), radius);

            _blurred = softened;
            _blurredBy = radius;
            return softened;
        }
    }
}
