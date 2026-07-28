namespace Macshot.Windows.Services;

public enum CaptureBackend
{
    /// <summary>Nothing has been captured yet, so no backend has had to be chosen.</summary>
    None,

    WindowsGraphicsCapture,

    BitBlt,
}

/// <summary>
/// Takes the desktop capture, preferring <c>Windows.Graphics.Capture</c> and falling
/// back to <c>BitBlt</c>.
/// </summary>
/// <remarks>
/// <para>
/// The fallback is not only for old Windows. <see cref="GraphicsCaptureService"/>
/// rests on D3D and COM interop that continuous integration can compile but cannot
/// run, so a mistake in it would otherwise turn "take a screenshot" into "nothing
/// happens". Degrading to the backend that already works keeps the feature intact
/// while the newer one is proven on real hardware. See
/// <c>docs/windows-port/architecture.md</c>, decision D5.
/// </para>
/// <para>
/// Falling back is remembered rather than swallowed. On a system that has the API, a
/// fallback means a defect, and <see cref="FallbackReason"/> is what lets the caller
/// say so instead of quietly running on the older path forever.
/// </para>
/// </remarks>
public sealed class ScreenCaptureService : IDisposable
{
    private readonly NativeScreenCaptureService _bitBlt = new();
    private readonly GraphicsCaptureService _graphics = new();
    private bool _disposed;

    /// <summary>Which backend produced the most recent capture.</summary>
    public CaptureBackend Backend { get; private set; } = CaptureBackend.None;

    /// <summary>
    /// Why the preferred backend was not used, or null when it was. Set even when the
    /// reason is benign — this build of Windows not offering the API — so the caller
    /// can tell that case apart from a failure.
    /// </summary>
    public string? FallbackReason { get; private set; }

    /// <summary>
    /// True when the preferred backend was available and still failed, which is the
    /// only case worth telling the user about.
    /// </summary>
    public bool FellBackUnexpectedly { get; private set; }

    public async Task<CapturedFrame> CaptureVirtualDesktopAsync(DisplaySet displays)
    {
        ArgumentNullException.ThrowIfNull(displays);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!GraphicsCaptureService.IsSupported)
        {
            FallbackReason = "This build of Windows does not offer Windows.Graphics.Capture.";
            FellBackUnexpectedly = false;
            return UseBitBlt();
        }

        try
        {
            var frame = await _graphics.CaptureVirtualDesktopAsync(displays);
            Backend = CaptureBackend.WindowsGraphicsCapture;
            FallbackReason = null;
            FellBackUnexpectedly = false;
            return frame;
        }
        catch (Exception exception)
        {
            // Deliberately broad. Everything from a missing d3d11 export to a display
            // that never delivers a frame ends up here, and every one of them has the
            // same right answer: take the screenshot the old way.
            FallbackReason = exception.Message;
            FellBackUnexpectedly = true;
            return UseBitBlt();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _graphics.Dispose();
    }

    private CapturedFrame UseBitBlt()
    {
        Backend = CaptureBackend.BitBlt;
        return _bitBlt.CaptureVirtualDesktop();
    }
}
