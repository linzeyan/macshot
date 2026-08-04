using System.Globalization;

namespace Macshot.Windows.Core.Capture;

/// <summary>
/// What each kind of pill says on the effects band, and what its menu offers.
/// </summary>
/// <remarks>
/// macshot's <c>formatZoom</c>, <c>formatSpeedLabel</c>, <c>formatFreezeLabel</c> and
/// <c>formatCutLabel</c>. Here rather than beside the band because a pill's text is
/// arithmetic with edges — a trailing zero on every whole number is two characters of
/// noise on a pill that may be twenty pixels wide — and none of it needs a window.
/// </remarks>
public static class VideoEffectLabels
{
    /// <summary>
    /// A zoom level: <c>2x</c> for a whole one and <c>1.5x</c> otherwise.
    /// </summary>
    /// <remarks>
    /// macshot's rule. A pill on a two-minute recording can be a dozen pixels wide, so
    /// <c>2.0x</c> where <c>2x</c> would do is a quarter of the label spent on nothing.
    /// </remarks>
    public static string Zoom(double level) => Trimmed(level) + "x";

    /// <summary>A speed factor, with the multiplication sign macshot uses.</summary>
    public static string Speed(double factor) => Trimmed(factor) + "×";

    /// <summary>How long a freeze holds.</summary>
    public static string Freeze(double seconds) => Trimmed(seconds) + "s";

    /// <summary>
    /// How much a cut removes.
    /// </summary>
    /// <remarks>
    /// Always one decimal, unlike the other three. macshot's choice, and the reason is
    /// that a cut's length is dragged rather than picked from a menu, so a whole number
    /// is a coincidence rather than a setting — <c>2s</c> would read as an exact figure
    /// the user chose.
    /// </remarks>
    public static string Cut(double seconds) =>
        seconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";

    /// <summary>The English key for what a pill of this kind is called, for <c>L(...)</c>.</summary>
    /// <remarks>
    /// The words are macshot's menu titles, which is what makes them already translated
    /// in the strings this port vendors from the Mac app.
    /// </remarks>
    public static string AddKey(VideoEffectKind kind) => kind switch
    {
        VideoEffectKind.Zoom => "Add Zoom",
        VideoEffectKind.Censor => "Add Censor",
        VideoEffectKind.Cut => "Add Cut",
        VideoEffectKind.Speed => "Add Speed",
        VideoEffectKind.Freeze => "Add Freeze",
        _ => "Add Text",
    };

    public static string DeleteKey(VideoEffectKind kind) => kind switch
    {
        VideoEffectKind.Zoom => "Delete Zoom",
        VideoEffectKind.Censor => "Delete Censor",
        VideoEffectKind.Cut => "Delete Cut",
        VideoEffectKind.Speed => "Delete Speed",
        VideoEffectKind.Freeze => "Delete Freeze",
        _ => "Delete Text",
    };

    public static string StyleKey(VideoCensorStyle style) => style switch
    {
        VideoCensorStyle.Solid => "Solid",
        VideoCensorStyle.Pixelate => "Pixelate",
        _ => "Blur",
    };

    /// <remarks>
    /// Rounded to a hundredth before the comparison, so a factor that arrived as
    /// 1.9999999 from a slider reads as 2 rather than as 2.00.
    /// </remarks>
    private static string Trimmed(double value)
    {
        var rounded = Math.Round(value, 2);

        return Math.Abs(rounded - Math.Round(rounded)) < 0.01
            ? Math.Round(rounded).ToString("0", CultureInfo.InvariantCulture)
            : rounded.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
