namespace Macshot.Windows.Core.Capture;

/// <summary>
/// One thing a <c>macshot://</c> URL can ask macshot to do.
/// </summary>
/// <remarks>
/// macshot's <c>handleURLSchemeAction</c> — AppDelegate.swift:2205 — case for case. The
/// point of the scheme is that something else drives macshot: a launcher, a stream deck,
/// a shortcut on the desktop, a line in a script. Everything here is therefore a command
/// the notification-area menu or a global shortcut already offers, reached from outside.
/// </remarks>
public enum UrlSchemeAction
{
    /// <summary>Put the overlays up and wait for a region.</summary>
    Capture,

    /// <summary>Take the whole desktop without asking.</summary>
    CaptureFullScreen,

    /// <summary>Take the region the last capture used.</summary>
    CaptureLastArea,

    /// <summary>Take a region and deliver it unmarked.</summary>
    QuickCapture,

    /// <summary>Take a region and read the text and codes in it.</summary>
    Ocr,

    /// <summary>Take a region, translate its text, and lay the translation over it.</summary>
    OcrTranslate,

    /// <summary>Choose a region to record.</summary>
    Record,

    /// <summary>Start recording the display the pointer is on.</summary>
    RecordFullScreen,

    /// <summary>Stop the recording that is running.</summary>
    StopRecording,

    /// <summary>Take a region and scroll the window behind it.</summary>
    ScrollCapture,

    /// <summary>Show the history panel.</summary>
    History,

    /// <summary>Show the settings window.</summary>
    Settings,

    /// <summary>Open an image file in the editor.</summary>
    Open,

    /// <summary>Open a past capture in the editor.</summary>
    Edit,
}

/// <summary>
/// A command as it is written down: what to type, and what it does.
/// </summary>
/// <param name="Host">
/// The word after <c>macshot://</c>, which is what a URL is matched on.
/// </param>
/// <param name="Example">
/// The query part of the sample, or null for a command that takes nothing. Its name is
/// also the parameter <see cref="UrlSchemeCommands.Parse"/> reads, so the sample cannot
/// show something the parser ignores.
/// </param>
/// <param name="Description">
/// What it does, in macshot's own words — its settings window lists these, and its
/// translations are keyed on the English it ships.
/// </param>
public sealed record UrlSchemeCommandInfo(
    UrlSchemeAction Action,
    string Host,
    string? Example,
    string Description)
{
    /// <summary>The parameter this command reads, or null when it takes none.</summary>
    public string? Parameter => Example is null
        ? null
        : Example[..Example.IndexOf('=', StringComparison.Ordinal)];

    /// <summary>The whole thing as it is shown and as it can be typed.</summary>
    public string Text => Example is null
        ? $"{UrlSchemeCommands.Scheme}://{Host}"
        : $"{UrlSchemeCommands.Scheme}://{Host}?{Example}";
}

/// <summary>
/// What macshot answers to when something else opens a <c>macshot://</c> URL.
/// </summary>
/// <remarks>
/// <para>
/// One table, read by both the parser and the list of commands the settings window
/// shows. Written twice they would be free to disagree, and the way that disagreement
/// shows up is a command documented in the settings window that does nothing when it is
/// used — which reads as macshot being broken rather than as the list being wrong.
/// </para>
/// <para>
/// Matching is on the host, as macshot's is: <c>macshot://capture</c> and nothing
/// shorter. A URL naming something not in the table is not answered at all, which is
/// what a version that has since gained a command needs — the older build ignores it
/// rather than guessing.
/// </para>
/// </remarks>
public static class UrlSchemeCommands
{
    /// <summary>The scheme itself, which is the same word on both products.</summary>
    public const string Scheme = "macshot";

    /// <summary>
    /// Every command, in the order macshot's settings window lists them
    /// (SettingsWindowController.swift:2968).
    /// </summary>
    /// <remarks>
    /// The sample for <see cref="UrlSchemeAction.Open"/> is a Windows path where
    /// macshot's is a POSIX one. It is an example rather than a string to match, and one
    /// beginning with a slash would be an example nobody here can follow.
    /// </remarks>
    public static IReadOnlyList<UrlSchemeCommandInfo> All { get; } =
    [
        new(UrlSchemeAction.Capture, "capture", null, "Start area capture"),
        new(UrlSchemeAction.CaptureFullScreen, "capture-fullscreen", null, "Capture the full screen"),
        new(UrlSchemeAction.CaptureLastArea, "capture-last", null, "Re-capture the last selected area"),
        new(UrlSchemeAction.QuickCapture, "quick-capture", null, "Quick capture (uses your Enter action)"),
        new(UrlSchemeAction.Ocr, "ocr", null, "Capture area and read text/QR codes"),
        new(
            UrlSchemeAction.OcrTranslate,
            "ocr-translate",
            "target=zh-CN",
            "Capture, translate, and overlay the text on the image"),
        new(UrlSchemeAction.Record, "record", null, "Start area recording"),
        new(UrlSchemeAction.RecordFullScreen, "record-fullscreen", null, "Start full-screen recording"),
        new(UrlSchemeAction.StopRecording, "stop-recording", null, "Stop the current recording"),
        new(UrlSchemeAction.ScrollCapture, "scroll-capture", null, "Start scroll capture"),
        new(UrlSchemeAction.History, "history", null, "Open the recent captures overlay"),
        new(UrlSchemeAction.Settings, "settings", null, "Open this settings window"),
        new(UrlSchemeAction.Open, "open", @"file=C:\path.png", "Open an image file in the editor"),
        new(UrlSchemeAction.Edit, "edit", "id=<id>", "Open a history entry in the editor (keeps annotations editable)"),
    ];

    /// <summary>
    /// Whether <paramref name="argument"/> is one of these URLs rather than a file the
    /// shell is handing over.
    /// </summary>
    /// <remarks>
    /// The scheme and nothing else, because this decides whether a launching process is
    /// macshot or a messenger for the macshot already running. Answering yes to a path
    /// would end that process with the file unopened.
    /// </remarks>
    public static bool IsCommandUrl(string? argument) =>
        argument is not null
        && argument.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What <paramref name="url"/> asks for, or null when it asks for nothing this build
    /// knows how to do.
    /// </summary>
    public static UrlSchemeCommand? Parse(string? url)
    {
        if (!IsCommandUrl(url) || !Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return null;
        }

        var command = All.FirstOrDefault(
            entry => string.Equals(entry.Host, parsed.Host, StringComparison.OrdinalIgnoreCase));

        return command is null
            ? null
            : new UrlSchemeCommand(command.Action, ArgumentOf(parsed, command.Parameter));
    }

    /// <summary>
    /// The one query value a command reads, or null when it takes none or was given
    /// none.
    /// </summary>
    /// <remarks>
    /// Read by hand rather than through a query parser: the value is a Windows path often
    /// enough that it will contain characters a parser is entitled to treat as separators,
    /// and there is only ever one parameter to find.
    /// </remarks>
    private static string? ArgumentOf(Uri url, string? parameter)
    {
        if (parameter is null)
        {
            return null;
        }

        foreach (var pair in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = pair.IndexOf('=', StringComparison.Ordinal);
            if (split > 0 && pair[..split].Equals(parameter, StringComparison.OrdinalIgnoreCase))
            {
                var value = Uri.UnescapeDataString(pair[(split + 1)..]);
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }
}

/// <summary>What one <c>macshot://</c> URL turned out to be asking for.</summary>
/// <param name="Argument">
/// The file, the history entry or the language it named, or null when it named none —
/// which for <see cref="UrlSchemeAction.OcrTranslate"/> means the saved default language,
/// and for the two that open something means there is nothing to open.
/// </param>
public sealed record UrlSchemeCommand(UrlSchemeAction Action, string? Argument);
