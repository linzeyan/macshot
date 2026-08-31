using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;
using WinRT;

// Imported rather than written out at each use site: inside namespace Macshot.Windows
// the name "Windows" binds to Macshot.Windows, so a qualified Windows.Graphics.Capture
// resolves to Macshot.Windows.Graphics.Capture and does not compile.
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;

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

    /// <summary>The runtime class whose activation factory carries the interop interface.</summary>
    private const string CaptureItemClass = "Windows.Graphics.Capture.GraphicsCaptureItem";

    /// <summary>IID of <c>IGraphicsCaptureItemInterop</c>.</summary>
    private static readonly Guid CaptureItemInteropId = new("3628e81b-3cac-4c60-b7f4-23ce0e0c3356");

    /// <summary>IID of <c>IGraphicsCaptureItem</c>, the interface the interop hands back.</summary>
    private static readonly Guid CaptureItemId = new("79c3f95b-31f7-4ec2-a464-632ef5d30760");

    private IDirect3DDevice? _device;
    private bool _disposed;

    /// <summary>
    /// Whether this build of Windows offers the API at all. Older Windows 10
    /// releases do not, which is why BitBlt stays in the tree.
    /// </summary>
    public static bool IsSupported => GraphicsCaptureSession.IsSupported();

    /// <param name="includeCursor">
    /// Whether the pointer is drawn into the frame. The capture session decides it, so
    /// the pointer is composited by Windows at the position it held when the frame was
    /// taken rather than the one it has drifted to by the time this returns.
    /// </param>
    public async Task<CapturedFrame> CaptureVirtualDesktopAsync(DisplaySet displays, bool includeCursor = false)
    {
        ArgumentNullException.ThrowIfNull(displays);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // One device for every display and every capture: creating one per frame
        // would spend more time initializing D3D than capturing.
        var device = _device ??= CreateDirect3DDevice();
        var composer = new FrameComposer(displays.Layout);

        foreach (var monitor in displays.Layout.Monitors)
        {
            if (!displays.Handles.TryGetValue(monitor.DeviceName, out var handle))
            {
                throw new InvalidOperationException($"No display handle for '{monitor.DeviceName}'.");
            }

            var (width, height, pixels) = await CaptureDisplayAsync(device, handle, includeCursor);
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

        var device = _device ??= CreateDirect3DDevice();

        var (width, height, pixels) = await CaptureItemAsync(device, OpenWindow(windowId));
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

    /// <summary>
    /// Opens the capture item for one display.
    /// </summary>
    /// <remarks>
    /// Internal rather than private because screen recording opens the same item and
    /// keeps it running.
    /// </remarks>
    internal static GraphicsCaptureItem OpenDisplay(nint monitorHandle)
    {
        return CreateCaptureItem(monitorHandle, forMonitor: true);
    }

    /// <summary>
    /// Opens the capture item for one window. The id is the <c>HWND</c>.
    /// </summary>
    /// <remarks>
    /// Internal for the same reason <see cref="OpenDisplay"/> is — a recording opens the
    /// same item and keeps it running.
    /// </remarks>
    internal static GraphicsCaptureItem OpenWindow(long windowId)
    {
        return CreateCaptureItem((nint)windowId, forMonitor: false);
    }

    /// <summary>
    /// Opens a capture item for a monitor or a window through the desktop interop
    /// interface on <c>GraphicsCaptureItem</c>'s activation factory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The projected statics — <c>TryCreateFromDisplayId</c> and
    /// <c>TryCreateFromWindowId</c> — look like the obvious way to do this and were what
    /// this used. They are wrong here twice over:
    /// </para>
    /// <para>
    /// They need build 20348, while the app declares a floor of 19041 and
    /// <see cref="IsSupported"/> only asks for 17134. On every consumer Windows 10 from
    /// 20H2 to 22H2 the probe therefore says yes, the static then fails, and
    /// <see cref="ScreenCaptureService"/> reports that the good backend broke — when it
    /// was never reachable. Window capture returned null outright.
    /// </para>
    /// <para>
    /// And they require <c>GraphicsCaptureAccess.RequestAccessAsync(Programmatic)</c>,
    /// which a packaged build may only call with the <c>graphicsCaptureProgrammatic</c>
    /// restricted capability declared. The MSIX declares <c>runFullTrust</c> and nothing
    /// else, which is the whole of why it installs, launches, and cannot capture.
    /// </para>
    /// <para>
    /// This interface has been there since Windows 10 1903 (build 18362) — below the
    /// declared floor — needs no capability, and shows no picker. It is what every other
    /// capture tool on Windows uses.
    /// </para>
    /// <para>
    /// Called through the vtable rather than a <c>[ComImport]</c> interface because
    /// built-in COM interop is unavailable under NativeAOT, which the size work intends
    /// to reach. <c>CreateForWindow</c> is the first method after IUnknown's three and
    /// <c>CreateForMonitor</c> the second, which is their order in
    /// <c>windows.graphics.capture.interop.h</c> — not the alphabetical order the
    /// documentation lists them in.
    /// </para>
    /// </remarks>
    private static GraphicsCaptureItem CreateCaptureItem(nint handle, bool forMonitor)
    {
        var factory = GetCaptureItemInterop();
        try
        {
            var vtable = Marshal.ReadIntPtr(factory);
            var method = Marshal.ReadIntPtr(vtable, (forMonitor ? 4 : 3) * IntPtr.Size);
            var create = Marshal.GetDelegateForFunctionPointer<CreateCaptureItemForHandle>(method);

            var itemId = CaptureItemId;
            Marshal.ThrowExceptionForHR(create(factory, handle, in itemId, out var item));

            try
            {
                return MarshalInspectable<GraphicsCaptureItem>.FromAbi(item);
            }
            finally
            {
                Marshal.Release(item);
            }
        }
        finally
        {
            Marshal.Release(factory);
        }
    }

    /// <summary>
    /// The <c>GraphicsCaptureItem</c> activation factory, already narrowed to the interop
    /// interface. Caller releases.
    /// </summary>
    private static nint GetCaptureItemInterop()
    {
        Marshal.ThrowExceptionForHR(WindowsCreateString(
            CaptureItemClass,
            CaptureItemClass.Length,
            out var className));

        try
        {
            var interopId = CaptureItemInteropId;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(className, in interopId, out var factory));
            return factory;
        }
        finally
        {
            WindowsDeleteString(className);
        }
    }

    private static Task<(int Width, int Height, byte[] Pixels)> CaptureDisplayAsync(
        IDirect3DDevice device,
        nint monitorHandle,
        bool includeCursor)
    {
        return CaptureItemAsync(device, OpenDisplay(monitorHandle), includeCursor);
    }

    /// <summary>
    /// Runs one capture item long enough to take a single frame off it. A display
    /// and a window differ only in how the item is opened, so everything from the
    /// frame pool down is shared.
    /// </summary>
    private static async Task<(int Width, int Height, byte[] Pixels)> CaptureItemAsync(
        IDirect3DDevice device,
        GraphicsCaptureItem item,
        bool includeCursor = false)
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

        // A screenshot of the pointer is almost never what was wanted, so this is off
        // unless the setting asks for it — macshot's captureCursor.
        session.IsCursorCaptureEnabled = includeCursor;
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
    internal static IDirect3DDevice CreateDirect3DDevice()
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

    /// <summary>
    /// Both interop methods, which share a signature: an <c>HMONITOR</c> or an
    /// <c>HWND</c>, the interface asked for, and the object out.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateCaptureItemForHandle(IntPtr self, IntPtr handle, in Guid iid, out IntPtr item);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string source,
        int length,
        out IntPtr result);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(IntPtr value);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(IntPtr classId, in Guid iid, out IntPtr factory);
}
