using System.Runtime.InteropServices;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Output;
using Microsoft.UI.Xaml;
using WinRT;
using WinRT.Interop;

using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Macshot.Windows.Services;

/// <summary>
/// Hands a finished capture to whatever the user shares with — Mail, Teams, a phone
/// over Nearby Sharing — through the system's own share pane.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of macshot's Share button, which opens
/// <c>NSSharingServicePicker</c>. The pane is the operating system's, so both products
/// share to whatever the machine can share to rather than to a list macshot maintains.
/// </para>
/// <para>
/// Desktop apps cannot call <c>DataTransferManager.GetForCurrentView</c>: there is no
/// CoreWindow to get it for. The documented way through is
/// <c>IDataTransferManagerInterop</c>, which takes an HWND instead — the same shape as
/// every other WinRT UI type a desktop app has to open.
/// </para>
/// <para>
/// The image goes out as a file as well as a bitmap, because a good number of targets
/// take only files. It is written to the temporary directory rather than the user's
/// pictures: this is a copy handed to another program, not a capture the user asked to
/// keep, and it must not land where the saved ones live.
/// </para>
/// </remarks>
internal static class ShareSheet
{
    /// <summary>The IID of <c>DataTransferManager</c>, which the interop call asks for.</summary>
    private static readonly Guid ManagerId = new("a5caee9b-8708-49d1-8d36-67d25a8da00c");

    /// <summary>
    /// The manager the pane is currently reading from. Held in a field because the
    /// share happens after this call has returned: dropped here, the handlers below
    /// could be collected before the user has picked a target.
    /// </summary>
    private static DataTransferManager? _manager;

    /// <summary>
    /// Opens the share pane over <paramref name="window"/> for these pixels.
    /// </summary>
    /// <param name="shared">
    /// Run once the user picks a target. macshot dismisses the capture at that point,
    /// which is why this is a callback rather than something decided here: the pane
    /// belongs to the window, so closing the window earlier would take the pane with it.
    /// </param>
    public static async Task ShowAsync(
        Window window,
        CapturedFrame frame,
        CaptureSettings settings,
        Action? shared = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(settings);

        var file = await WriteTemporaryCopyAsync(frame, settings);

        var handle = WindowNative.GetWindowHandle(window);
        var interop = DataTransferManager.As<IDataTransferManagerInterop>();
        var id = ManagerId;
        var manager = MarshalInterface<DataTransferManager>.FromAbi(interop.GetForWindow(handle, ref id));
        _manager = manager;

        manager.DataRequested += (_, args) =>
        {
            var package = args.Request.Data;

            // The title is what the target shows as the subject or the file name, so it
            // is the capture's name rather than the product's.
            package.Properties.Title = file.Name;
            package.Properties.ApplicationName = BuildVariant.DisplayName;
            package.SetStorageItems([file]);
            package.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
        };

        if (shared is not null)
        {
            manager.TargetApplicationChosen += (_, _) => shared();
        }

        interop.ShowShareUIForWindow(handle);
    }

    /// <summary>
    /// Writes the capture where another program can read it, always as a PNG: a share
    /// target is a paste target rather than an archive, so it should never be handed the
    /// lossy copy.
    /// </summary>
    private static async Task<StorageFile> WriteTemporaryCopyAsync(CapturedFrame frame, CaptureSettings settings)
    {
        var directory = Path.Combine(Path.GetTempPath(), "macshot");
        Directory.CreateDirectory(directory);

        // The user's own naming, because the name travels with the image: an attachment
        // called "tmp4F2A.png" is one the recipient cannot place.
        var name = FilenameTemplate.ResolveUnique(
            settings.FilenameTemplate,
            DateTimeOffset.Now,
            CaptureImageFormat.Png.FileExtension(),
            candidate => File.Exists(Path.Combine(directory, candidate)));

        var path = Path.Combine(directory, name);
        await File.WriteAllBytesAsync(
            path,
            await ImageDelivery.EncodeAsync(frame, CaptureImageFormat.Png, CaptureSettings.MaxQuality));

        return await StorageFile.GetFileFromPathAsync(path);
    }

    /// <summary>
    /// The desktop way in to <c>DataTransferManager</c>, which is otherwise reachable
    /// only from a CoreWindow this app does not have.
    /// </summary>
    [ComImport]
    [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataTransferManagerInterop
    {
        nint GetForWindow([In] nint appWindow, [In] ref Guid riid);

        void ShowShareUIForWindow([In] nint appWindow);
    }
}
