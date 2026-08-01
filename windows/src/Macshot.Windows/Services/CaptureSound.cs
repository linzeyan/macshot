using System.Runtime.InteropServices;

namespace Macshot.Windows.Services;

/// <summary>
/// The short sound a finished capture makes, which is the only sign a capture taken by
/// hotkey and copied straight to the clipboard leaves behind.
/// </summary>
/// <remarks>
/// <para>
/// A system event rather than a sound of macshot's own. macshot plays the one macOS
/// ships for a screen capture — <c>AppDelegate.swift:231</c> — and the Windows
/// counterpart of that is the Asterisk event, which is the sound Windows itself uses to
/// say that something happened. It follows whatever the user has chosen in Sound
/// settings, including choosing nothing: <c>SND_NODEFAULT</c> means silence, rather than
/// a stock beep, for someone who has turned that event off.
/// </para>
/// <para>
/// Asynchronous, and without <c>SND_NOSTOP</c>, so three captures in a row are three
/// sounds rather than one that swallows the next two — macshot stops the sound before
/// playing it again for the same reason.
/// </para>
/// </remarks>
internal static class CaptureSound
{
    private const string AsteriskAlias = "SystemAsterisk";

    private const uint Async = 0x0001;
    private const uint NoDefault = 0x0002;
    private const uint Alias = 0x00010000;

    /// <summary>
    /// Plays it, if the setting asks for it.
    /// </summary>
    /// <remarks>
    /// Silent on failure in both senses. A machine with no audio device, or a driver
    /// that refuses, must not turn a delivered capture into an error report.
    /// </remarks>
    public static void Play(bool enabled)
    {
        if (!enabled)
        {
            return;
        }

        try
        {
            PlaySound(AsteriskAlias, IntPtr.Zero, Async | NoDefault | Alias);
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write($"Could not play the capture sound: {exception.Message}");
        }
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(string? sound, IntPtr module, uint flags);
}
