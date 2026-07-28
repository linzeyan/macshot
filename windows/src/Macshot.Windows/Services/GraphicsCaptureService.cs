using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;
using Microsoft.UI;
using WinRT;

// Imported rather than written out at each use site: inside namespace Macshot.Windows
// the name "Windows" binds to Macshot.Windows, so a qualified Windows.Graphics.Capture
// resolves to Macshot.Windows.Graphics.Capture and does not compile.
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;

// There are two DisplayId types with the same shape and no conversion between them:
// the Windows App SDK's Microsoft.UI.DisplayId, which Win32Interop hands back, and
// the system's Windows.Graphics.DisplayId, which the capture API takes. Aliased so
// the difference is visible where it is bridged rather than looking like a typo.
using GraphicsDisplayId = Windows.Graphics.DisplayId;
using GraphicsWindowId = Windows.Graphics.WindowId;

namespace Macshot.Windows.Services;

/// <summary>
/// Captures the desktop through <c>Windows.Graphics.Capture</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the backend <c>BitBlt</c> cannot be: it sees hardware-overlay and
/// protected content, and it is the same API window capture and screen recording
/// need, so those features become possible rather than blocked. See
/// <c>docs/windows-port/architecture.md</c>, decision D5.
/// </para>
/// <para>
/// It works one item per display, while everything downstream expects a single
/// virtual-desktop frame, so the displays are reassembled by
/// <see cref="FrameComposer"/>.
/// </para>
/// <para>
/// The interop below — a D3D device for the frame pool, and the WinRT wrapper around
/// it — is the part continuous integration can only compile. It is used behind
/// <see cref="ScreenCaptureService"/>, which falls back to BitBlt when anything here
/// throws, so being wrong costs the newer backend rather than the ability to take a
/// screenshot at all.
/// </para>
/// </remarks>
public sealed class GraphicsCaptureService : IDisposable
{
    /// <summary>
    /// How long one display is given to produce its first frame. Long enough for a
    /// compositor that is busy, short enough that a display which will never deliver
    /// falls back instead of hanging the hotkey.
    /// </summary>
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(2);

    private const uint DriverTypeHardware = 1;
    private const uint DriverTypeWarp = 5;

    /// <summary>D3D11_CREATE_DEVICE_BGRA_SUPPORT, which the WinRT interop requires.</summary>
    private const uint BgraSupport = 0x20;

    private const uint SdkVersion = 7;

    private static readonly Guid DxgiDeviceId = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

    private IDirect3DDevice? _device;
    private bool _disposed;

    /// <summary>
    /// Whether this build of Windows offers the API at all. Older Windows 10
    /// releases do not, which is why BitBlt stays in the tree.
    /// </summary>
    public static bool IsSupported => GraphicsCaptureSession.IsSupported();

    public async Task<CapturedFrame> CaptureVirtualDesktopAsync(DisplaySet displays)
    {
        ArgumentNullException.ThrowIfNull(displays);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // One device for every display and every capture: creating one per frame
        // would spend more time initializing D3D than capturing.
        var device = _device ??= CreateDevice();
        var composer = new FrameComposer(displays.Layout);

        foreach (var monitor in displays.Layout.Monitors)
        {
            if (!displays.Handles.TryGetValue(monitor.DeviceName, out var handle))
            {
                throw new InvalidOperationException($"No display handle for '{monitor.DeviceName}'.");
            }

            var (width, height, pixels) = await CaptureDisplayAsync(device, handle);
            composer.Draw(monitor, width, height, pixels);
        }

        return new CapturedFrame(
            composer.VirtualX,
            composer.VirtualY,
            composer.Width,
            composer.Height,
            composer.ToImage());
    }

    /// <summary>
    /// Captures one window as its own item, rather than cropping it out of the
    /// desktop frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what a crop cannot do: the compositor renders the window's own tree,
    /// so anything sitting in front of it — a dialog, another app, macshot's own
    /// overlay — is simply not there. The delivered image is the window, not the
    /// window as it happened to be buried.
    /// </para>
    /// <para>
    /// The frame arrives as the window manager holds it, borders and all, so
    /// <see cref="WindowFrameCrop"/> takes the invisible resize border back off.
    /// </para>
    /// </remarks>
    public async Task<CapturedFrame> CaptureWindowAsync(long windowId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!WindowEnumerator.TryGetBounds(windowId, out var windowRect, out var visibleBounds))
        {
            throw new InvalidOperationException("Windows no longer reports bounds for that window.");
        }

        var device = _device ??= CreateDevice();

        // Two WindowId types with the same shape and no conversion, exactly as with
        // DisplayId: Microsoft.UI.WindowId is the App SDK's, Windows.Graphics.WindowId
        // is what the capture API takes. Here the value comes straight from an HWND,
        // so there is only the one crossing to make.
        var item = GraphicsCaptureItem.TryCreateFromWindowId(new GraphicsWindowId { Value = (ulong)windowId })
            ?? throw new InvalidOperationException("Windows would not open a capture item for the window.");

        var (width, height, pixels) = await CaptureItemAsync(device, item);
        var crop = WindowFrameCrop.Resolve(windowRect, visibleBounds, width, height);

        var frame = new CapturedFrame((int)windowRect.X, (int)windowRect.Y, width, height, pixels);
        return NativeScreenCaptureService.Crop(frame, crop);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _device?.Dispose();
        _device = null;
    }

    private static Task<(int Width, int Height, byte[] Pixels)> CaptureDisplayAsync(
        IDirect3DDevice device,
        nint monitorHandle)
    {
        var displayId = new GraphicsDisplayId
        {
            Value = Win32Interop.GetDisplayIdFromMonitor(monitorHandle).Value,
        };

        var item = GraphicsCaptureItem.TryCreateFromDisplayId(displayId)
            ?? throw new InvalidOperationException("Windows would not open a capture item for the display.");

        return CaptureItemAsync(device, item);
    }

    /// <summary>
    /// Runs one capture item long enough to take a single frame off it. A display
    /// and a window differ only in how the item is opened, so everything from the
    /// frame pool down is shared.
    /// </summary>
    private static async Task<(int Width, int Height, byte[] Pixels)> CaptureItemAsync(
        IDirect3DDevice device,
        GraphicsCaptureItem item)
    {
        var arrival = new TaskCompletionSource<Direct3D11CaptureFrame>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // Free threaded: frames arrive on a pool thread rather than needing a
        // dispatcher, and a capture triggered by the global hotkey may not have one.
        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            1,
            item.Size);

        pool.FrameArrived += (sender, _) =>
        {
            if (sender.TryGetNextFrame() is { } frame)
            {
                arrival.TrySetResult(frame);
            }
        };

        using var session = pool.CreateCaptureSession(item);

        // A screenshot of the pointer is almost never what was wanted, and the BitBlt
        // path does not include one either.
        session.IsCursorCaptureEnabled = false;
        session.StartCapture();

        using var captured = await arrival.Task.WaitAsync(FrameTimeout);
        using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(captured.Surface);

        var pixels = new byte[checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)];
        bitmap.CopyToBuffer(pixels.AsBuffer());
        return (bitmap.PixelWidth, bitmap.PixelHeight, pixels);
    }

    /// <summary>
    /// Builds the <c>IDirect3DDevice</c> the frame pool needs.
    /// </summary>
    /// <remarks>
    /// There is no managed way to get one: D3D creates an <c>ID3D11Device</c>, and
    /// <c>CreateDirect3D11DeviceFromDXGIDevice</c> wraps its DXGI face in the WinRT
    /// type. Win2D supplies one ready made, but it is a renderer, and taking a whole
    /// renderer as a dependency to obtain one object is a worse trade than this much
    /// interop.
    /// </remarks>
    private static IDirect3DDevice CreateDevice()
    {
        var result = D3D11CreateDevice(
            IntPtr.Zero,
            DriverTypeHardware,
            IntPtr.Zero,
            BgraSupport,
            IntPtr.Zero,
            0,
            SdkVersion,
            out var d3dDevice,
            out _,
            out var context);

        if (result < 0)
        {
            // A machine with no usable GPU — a VM, a remote session — still has to be
            // able to take a screenshot, and WARP renders in software.
            result = D3D11CreateDevice(
                IntPtr.Zero,
                DriverTypeWarp,
                IntPtr.Zero,
                BgraSupport,
                IntPtr.Zero,
                0,
                SdkVersion,
                out d3dDevice,
                out _,
                out context);
        }

        Marshal.ThrowExceptionForHR(result);

        try
        {
            var dxgiId = DxgiDeviceId;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3dDevice, in dxgiId, out var dxgiDevice));
            try
            {
                Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out var wrapped));
                try
                {
                    return MarshalInspectable<IDirect3DDevice>.FromAbi(wrapped);
                }
                finally
                {
                    Marshal.Release(wrapped);
                }
            }
            finally
            {
                Marshal.Release(dxgiDevice);
            }
        }
        finally
        {
            if (context != IntPtr.Zero)
            {
                Marshal.Release(context);
            }

            if (d3dDevice != IntPtr.Zero)
            {
                Marshal.Release(d3dDevice);
            }
        }
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(
        IntPtr adapter,
        uint driverType,
        IntPtr software,
        uint flags,
        IntPtr featureLevels,
        uint featureLevelCount,
        uint sdkVersion,
        out IntPtr device,
        out uint featureLevel,
        out IntPtr immediateContext);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);
}
