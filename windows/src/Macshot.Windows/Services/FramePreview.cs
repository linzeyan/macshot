using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Macshot.Windows.Services;

/// <summary>
/// Draws a capture into an <see cref="Image"/>, and releases what that costs.
/// </summary>
/// <remarks>
/// <para>
/// Showing a capture allocates twice, and both allocations are native memory whose size
/// the garbage collector cannot see. <c>SetBitmapAsync</c> copies the bitmap it is given,
/// so the caller still owns the original; the source then holds that copy until it is
/// disposed. Neither was disposed at any of the five surfaces that show a capture — the
/// selection overlay, the thumbnail panel and its quarter turns, the pin window, the
/// recognition window — so each one left a screen's worth of pixels behind twice over.
/// Nothing collected them, because a collector that cannot see the size of a leak has no
/// reason to run: ten area captures took the process from 22MB to 452MB, and two minutes
/// of idling gave back three.
/// </para>
/// <para>
/// In one place rather than fixed in five, because the pairing is the point. A sixth
/// surface that shows a capture has to release it too, and one call is harder to get half
/// right than two disposals in two different methods.
/// </para>
/// </remarks>
internal static class FramePreview
{
    /// <summary>
    /// Shows <paramref name="frame"/> in <paramref name="image"/>, releasing whatever it
    /// was showing before.
    /// </summary>
    /// <remarks>
    /// The previous source is released rather than merely replaced: the thumbnail panel
    /// assigns a new one on every quarter turn, and those were accumulating one screen of
    /// pixels each.
    /// </remarks>
    public static async Task ShowAsync(this Image image, CapturedFrame frame)
    {
        var source = new SoftwareBitmapSource();

        // Disposed the moment the copy exists rather than left to a finalizer that has no
        // reason to run.
        using (var bitmap = frame.ToDisplayBitmap())
        {
            await source.SetBitmapAsync(bitmap);
        }

        image.Release();
        image.Source = source;
    }

    /// <summary>
    /// Releases what <paramref name="image"/> is showing. The window that owns it calls
    /// this as it closes.
    /// </summary>
    /// <remarks>
    /// Closing a window is not what frees this. WinUI holds the element tree past the
    /// close, and a <see cref="SoftwareBitmapSource"/> only gives its pixels back when it
    /// is told to.
    /// </remarks>
    public static void Release(this Image image)
    {
        if (image.Source is SoftwareBitmapSource source)
        {
            source.Dispose();
        }

        image.Source = null;
    }
}
