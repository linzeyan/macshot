using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Services;

/// <summary>
/// What the user asked to happen to a finished capture.
/// </summary>
/// <remarks>
/// The toolbar's copy, save and pin buttons name one destination each, while Enter means
/// "whatever the preferences say". Without this distinction a press on Save would have to
/// change the preferences to be obeyed, and pressing Copy with auto-save switched on would
/// quietly write a file the user never asked for.
/// </remarks>
public enum CaptureOutcome
{
    /// <summary>Everything the preferences ask for: clipboard, auto-save, thumbnail.</summary>
    Deliver,

    /// <summary>The clipboard, and nothing else.</summary>
    Copy,

    /// <summary>The save folder, and nothing else.</summary>
    Save,

    /// <summary>A floating window on top of everything.</summary>
    Pin,
}

/// <summary>
/// A finished capture and where its pixels were asked to go.
/// </summary>
public sealed record CaptureCompletion(CapturedFrame Frame, CaptureOutcome Outcome);
