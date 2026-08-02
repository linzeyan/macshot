using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;

namespace Macshot.Windows.Services;

/// <summary>
/// What the pointer reports about how hard it is being pressed.
/// </summary>
/// <remarks>
/// Shared by the overlay and the editor window because both draw with the same editor,
/// and a pen has to behave identically in the two — a stroke that tapered over a capture
/// and came out even in the editor would look like a bug in whichever the user tried
/// second.
/// </remarks>
internal static class PenInput
{
    /// <summary>
    /// How hard the pen is pressed, from 0 to 1, or 0 for anything that is not a pen.
    /// </summary>
    /// <remarks>
    /// Zero rather than the reported value for a mouse or a finger. A mouse reports a
    /// constant half-press and a touch contact reports its own guess at one: honoured,
    /// either would draw every stroke narrower than the width slider says while varying
    /// nothing, which is a setting that appears to do only harm. Zero is the editor's
    /// signal that this device has nothing to say about pressure.
    /// </remarks>
    public static double Of(PointerRoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        return e.Pointer.PointerDeviceType == PointerDeviceType.Pen
            ? e.GetCurrentPoint(null).Properties.Pressure
            : 0;
    }
}
