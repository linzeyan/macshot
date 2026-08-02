namespace Macshot.Windows.Services;

/// <summary>
/// What the region a user is about to draw is for.
/// </summary>
/// <remarks>
/// <para>
/// macshot's four <c>pending…Mode</c> flags on <c>AppDelegate</c> — <c>pendingRecordMode</c>,
/// <c>pendingOCRMode</c>, <c>pendingQuickCaptureMode</c>, <c>pendingScrollCaptureMode</c> —
/// as one value. They are mutually exclusive in every path that sets them, and four
/// independent booleans are four ways to end up in two modes at once.
/// </para>
/// <para>
/// Everything but <see cref="Capture"/> means the toolbar never appears: the menu item
/// already said what this region is for, so what it said happens as the drag ends rather
/// than after a confirm the user has no reason to expect.
/// </para>
/// </remarks>
public enum CaptureIntent
{
    /// <summary>Take a picture of it, with the annotation tools first. The default.</summary>
    Capture,

    /// <summary>Record it — macshot's "Record Area".</summary>
    Record,

    /// <summary>Read the text and the codes in it — macshot's "Capture OCR &amp; QR".</summary>
    Recognize,

    /// <summary>
    /// Translate the text in it and lay the translation over it — macshot's
    /// <c>autoTranslateOverlayMode</c>, which only <c>macshot://ocr-translate</c> sets.
    /// </summary>
    /// <remarks>
    /// The one intent that leaves the toolbar up, as macshot's does: a translation is a
    /// guess at what the words mean and it is drawn onto the picture, so it is something
    /// to look at and correct before the capture is delivered. Reading text out into a
    /// window has nothing to correct, which is why <see cref="Recognize"/> does not
    /// wait.
    /// </remarks>
    Translate,

    /// <summary>Scroll the window behind it and stitch — macshot's "Scroll Capture".</summary>
    Scroll,

    /// <summary>Deliver it straight away, unmarked — macshot's "Quick Capture".</summary>
    Quick,
}
