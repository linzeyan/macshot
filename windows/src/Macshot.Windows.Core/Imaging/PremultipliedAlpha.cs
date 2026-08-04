namespace Macshot.Windows.Core.Imaging;

/// <summary>
/// A BGRA buffer whose colour bytes have been scaled by their own alpha.
/// </summary>
/// <remarks>
/// <para>
/// macshot holds transparency straight — a cut-out keeps the subject's own colours and
/// changes only the alpha beside them — because that is what a PNG encoder is told these
/// pixels are. Everything that draws one on screen wants the other convention:
/// <c>SoftwareBitmapSource</c> accepts premultiplied or no alpha and refuses straight,
/// which surfaces as an <c>ArgumentException</c> at the moment a cut-out is shown rather
/// than at the moment it is made.
/// </para>
/// <para>
/// So the conversion happens on the way to the screen and nowhere else. Storing
/// premultiplied would be the other way to fix it and is worse: the encoder would have to
/// undo it, and undoing it cannot recover a colour that was multiplied by a small alpha.
/// </para>
/// </remarks>
public static class PremultipliedAlpha
{
    /// <summary>The same pixels with each colour byte scaled by that pixel's alpha.</summary>
    /// <remarks>
    /// A copy rather than in place: the caller's buffer is the capture itself, and the
    /// screen is not the only thing that reads it. Rounded rather than truncated — the
    /// error is small per pixel and consistently darkening, which shows up as a grey rim
    /// around a cut-out subject where a fringe of partial alpha runs.
    /// </remarks>
    public static byte[] From(ReadOnlySpan<byte> bgra)
    {
        if (bgra.Length % 4 != 0)
        {
            throw new ArgumentException("a BGRA buffer is four bytes to the pixel", nameof(bgra));
        }

        var pixels = new byte[bgra.Length];

        for (var index = 0; index < bgra.Length; index += 4)
        {
            var alpha = bgra[index + 3];

            pixels[index] = Scale(bgra[index], alpha);
            pixels[index + 1] = Scale(bgra[index + 1], alpha);
            pixels[index + 2] = Scale(bgra[index + 2], alpha);
            pixels[index + 3] = alpha;
        }

        return pixels;
    }

    private static byte Scale(byte channel, byte alpha) => (byte)(((channel * alpha) + 127) / 255);
}
