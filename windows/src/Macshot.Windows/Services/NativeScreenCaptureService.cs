using System.Runtime.InteropServices;
using Macshot.Windows.Core.Capture;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Macshot.Windows.Services;

public sealed class NativeScreenCaptureService
{
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const uint DibRgbColors = 0;
    private const uint BiRgb = 0;
    private const uint Srccopy = 0x00CC0020;
    private const uint CaptureBlt = 0x40000000;

    public CapturedFrame CaptureVirtualDesktop()
    {
        var virtualX = GetSystemMetrics(SmXVirtualScreen);
        var virtualY = GetSystemMetrics(SmYVirtualScreen);
        var width = GetSystemMetrics(SmCxVirtualScreen);
        var height = GetSystemMetrics(SmCyVirtualScreen);
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("Windows did not report an available display.");
        }

        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            throw new InvalidOperationException("Unable to access the screen device context.");
        }

        IntPtr memoryDc = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;
        IntPtr previousObject = IntPtr.Zero;
        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            if (memoryDc == IntPtr.Zero)
            {
                throw new InvalidOperationException("Unable to create an in-memory device context.");
            }

            var bitmapInfo = BitmapInfo.CreateTopDown32Bit(width, height);
            bitmap = CreateDIBSection(screenDc, ref bitmapInfo, DibRgbColors, out var pixels, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || pixels == IntPtr.Zero)
            {
                throw new InvalidOperationException("Unable to create the screen capture bitmap.");
            }

            previousObject = SelectObject(memoryDc, bitmap);
            if (previousObject == IntPtr.Zero || previousObject == new IntPtr(-1))
            {
                throw new InvalidOperationException("Unable to select the screen capture bitmap.");
            }

            if (!BitBlt(memoryDc, 0, 0, width, height, screenDc, virtualX, virtualY, Srccopy | CaptureBlt))
            {
                throw new InvalidOperationException("Windows rejected the screen capture request.");
            }

            var bytes = new byte[checked(width * height * 4)];
            Marshal.Copy(pixels, bytes, 0, bytes.Length);
            return new CapturedFrame(virtualX, virtualY, width, height, bytes);
        }
        finally
        {
            if (previousObject != IntPtr.Zero && memoryDc != IntPtr.Zero)
            {
                SelectObject(memoryDc, previousObject);
            }

            if (bitmap != IntPtr.Zero)
            {
                DeleteObject(bitmap);
            }

            if (memoryDc != IntPtr.Zero)
            {
                DeleteDC(memoryDc);
            }

            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    public async Task<string> SavePngAsync(CapturedFrame frame, CaptureRegion? selection)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var output = selection is { IsEmpty: false }
            ? Crop(frame, selection.Value)
            : frame;
        var outputDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "Macshot");
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, $"Macshot-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.png");

        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            // BitBlt produces BGRX pixels. The alpha byte is undefined and must not
            // make otherwise opaque screenshots transparent during encoding.
            BitmapAlphaMode.Ignore,
            (uint)output.Width,
            (uint)output.Height,
            96,
            96,
            output.BgraPixels);
        await encoder.FlushAsync();

        stream.Seek(0);
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        var length = await reader.LoadAsync((uint)stream.Size);
        var bytes = new byte[length];
        reader.ReadBytes(bytes);
        await File.WriteAllBytesAsync(outputPath, bytes);
        return outputPath;
    }

    private static CapturedFrame Crop(CapturedFrame frame, CaptureRegion region)
    {
        var left = Math.Clamp((int)Math.Floor(region.X), 0, frame.Width);
        var top = Math.Clamp((int)Math.Floor(region.Y), 0, frame.Height);
        var right = Math.Clamp((int)Math.Ceiling(region.X + region.Width), left, frame.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(region.Y + region.Height), top, frame.Height);
        var width = right - left;
        var height = bottom - top;
        if (width == 0 || height == 0)
        {
            throw new InvalidOperationException("Select a non-empty capture region before saving.");
        }

        var pixels = new byte[checked(width * height * 4)];
        for (var row = 0; row < height; row++)
        {
            Buffer.BlockCopy(
                frame.BgraPixels,
                ((top + row) * frame.Width + left) * 4,
                pixels,
                row * width * 4,
                width * 4);
        }

        return new CapturedFrame(frame.VirtualX + left, frame.VirtualY + top, width, height, pixels);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetSystemMetrics(int systemMetric);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr windowHandle, IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(
        IntPtr deviceContext,
        ref BitmapInfo bitmapInfo,
        uint usage,
        out IntPtr bits,
        IntPtr section,
        uint offset);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr graphicsObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(
        IntPtr destination,
        int destinationX,
        int destinationY,
        int width,
        int height,
        IntPtr source,
        int sourceX,
        int sourceY,
        uint rasterOperation);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr graphicsObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint ImageSize;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RgbQuad
    {
        public byte Blue;
        public byte Green;
        public byte Red;
        public byte Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public RgbQuad Colors;

        public static BitmapInfo CreateTopDown32Bit(int width, int height)
        {
            return new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = BiRgb,
                },
            };
        }
    }
}
