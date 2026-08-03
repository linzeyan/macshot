using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Imaging;

/// <summary>
/// Where the opaque part of a cut-out actually is.
/// </summary>
/// <remarks>
/// <para>
/// A subject lifted out of a capture comes back the size of the capture, with everything
/// that was not the subject made transparent. To cover the person rather than the whole
/// region, something has to read back which pixels survived — which is this, and it is
/// arithmetic over a buffer rather than anything to do with a model, so it lives in Core
/// where it can be held to a test.
/// </para>
/// <para>
/// One box rather than one per person, which is where this falls short of macshot's
/// human-rectangles pass. The model behind it lifts a subject, not a list of them, so two
/// people standing apart come back as one mask and are covered by one rectangle spanning
/// both. For a redaction that errs the safe way: it covers more than it was asked to,
/// never less.
/// </para>
/// </remarks>
public static class SubjectBounds
{
    /// <summary>
    /// How opaque a pixel has to be to count as part of the subject.
    /// </summary>
    /// <remarks>
    /// Well above zero. A matte has a soft edge, and a threshold of 1 would take in the
    /// faint halo the model leaves around hair — which on a busy screenshot reaches most
    /// of the way to the frame and gives back a box covering everything.
    /// </remarks>
    public const byte DefaultOpacity = 128;

    /// <summary>
    /// The smallest rectangle holding every pixel of <paramref name="bgra"/> at least
    /// <paramref name="opacity"/> opaque, in pixels from the top-left of the buffer.
    /// </summary>
    /// <returns>Null when nothing in the buffer is that opaque — nothing was lifted.</returns>
    public static CaptureRegion? Of(
        ReadOnlySpan<byte> bgra,
        int width,
        int height,
        byte opacity = DefaultOpacity)
    {
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        if (bgra.Length < checked(width * height * 4))
        {
            throw new ArgumentException(
                "The pixel buffer is smaller than the frame it is said to hold.",
                nameof(bgra));
        }

        var left = width;
        var top = height;
        var right = -1;
        var bottom = -1;

        for (var y = 0; y < height; y++)
        {
            var row = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                if (bgra[row + (x * 4) + 3] < opacity)
                {
                    continue;
                }

                if (x < left)
                {
                    left = x;
                }

                if (x > right)
                {
                    right = x;
                }

                if (y < top)
                {
                    top = y;
                }

                bottom = y;
            }
        }

        return right < 0
            ? null

            // Both edges inclusive, so a subject one pixel across is one pixel wide rather
            // than none — an empty region reads as "nothing found" everywhere else.
            : new CaptureRegion(left, top, right - left + 1, bottom - top + 1);
    }
}
