using System.Runtime.InteropServices;
using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Services;

/// <summary>
/// Builds the <see cref="MonitorLayout"/> from the attached displays.
/// </summary>
/// <remarks>
/// The reported bounds are only physical pixels while the process is
/// per-monitor-DPI-v2 aware, which <c>app.manifest</c> declares. Without that
/// manifest entry Windows virtualizes the coordinates and every display looks
/// like it runs at 100%.
/// </remarks>
public static class MonitorEnumerator
{
    private const int MonitorInfoPrimary = 0x1;
    private const int MonitorDpiTypeEffective = 0;
    private const double DefaultDpi = 96;

    public static MonitorLayout Enumerate()
    {
        var monitors = new List<CaptureMonitor>();

        var callback = new MonitorEnumProc((monitorHandle, _, _, _) =>
        {
            if (TryDescribe(monitorHandle, out var monitor))
            {
                monitors.Add(monitor);
            }

            return true;
        });

        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero) || monitors.Count == 0)
        {
            throw new InvalidOperationException("Windows did not report an available display.");
        }

        // Primary first keeps overlay creation order stable, which in turn keeps
        // which overlay ends up focused predictable.
        monitors.Sort((first, second) => second.IsPrimary.CompareTo(first.IsPrimary));
        return new MonitorLayout(monitors);
    }

    private static bool TryDescribe(IntPtr monitorHandle, out CaptureMonitor monitor)
    {
        monitor = null!;

        var info = MonitorInfoEx.Create();
        if (!GetMonitorInfo(monitorHandle, ref info))
        {
            return false;
        }

        var bounds = CaptureRegion.FromPoints(
            info.Monitor.Left,
            info.Monitor.Top,
            info.Monitor.Right,
            info.Monitor.Bottom);
        if (bounds.IsEmpty)
        {
            return false;
        }

        var workArea = CaptureRegion.FromPoints(
            info.Work.Left,
            info.Work.Top,
            info.Work.Right,
            info.Work.Bottom);

        monitor = new CaptureMonitor(
            string.IsNullOrWhiteSpace(info.DeviceName) ? monitorHandle.ToString() : info.DeviceName,
            bounds,
            GetScale(monitorHandle),
            (info.Flags & MonitorInfoPrimary) != 0)
        {
            // A display that reports no work area falls back to its full bounds
            // rather than to nothing, so panel placement stays on screen.
            WorkArea = workArea.IsEmpty ? bounds : workArea,
        };
        return true;
    }

    private static double GetScale(IntPtr monitorHandle)
    {
        // A display that refuses to report its DPI is far better treated as 100%
        // than as a hard failure that blocks capture entirely.
        if (GetDpiForMonitor(monitorHandle, MonitorDpiTypeEffective, out var dpiX, out _) != 0 || dpiX == 0)
        {
            return 1;
        }

        return dpiX / DefaultDpi;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr deviceContext, IntPtr clip, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr deviceContext,
        IntPtr clip,
        MonitorEnumProc callback,
        IntPtr data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public uint Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        public static MonitorInfoEx Create()
        {
            return new MonitorInfoEx
            {
                Size = (uint)Marshal.SizeOf<MonitorInfoEx>(),
                DeviceName = string.Empty,
            };
        }
    }
}
