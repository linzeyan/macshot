using Windows.Storage;
using Windows.System;

namespace Macshot.Windows.Services;

/// <summary>
/// Hands a capture to another program to open — Paint, Photoshop, whatever the machine
/// has.
/// </summary>
/// <remarks>
/// macshot's Open With is a submenu it fills itself, from
/// <c>LSCopyApplicationURLsForURL</c> (<c>FloatingThumbnailController.swift:410</c>).
/// Windows answers the same question with a dialog rather than a menu, and it is the
/// shell's own — so this offers one item that opens it instead of a submenu built from
/// the registry. A list macshot maintained here would be a second opinion about which
/// programs open a PNG, and the shell's is the one the rest of the desktop uses.
/// </remarks>
internal static class OpenWith
{
    /// <summary>Opens Windows' "How do you want to open this file?" picker.</summary>
    public static async Task ShowAsync(StorageFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        await Launcher.LaunchFileAsync(file, new LauncherOptions { DisplayApplicationPicker = true });
    }
}
