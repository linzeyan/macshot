using System.Text.Json;
using System.Text.Json.Serialization;

namespace Macshot.Windows.Core.Imaging;

/// <summary>
/// The post-processing a capture is carrying, kept as numbers rather than as pixels so
/// that reopening it can undo them.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>CaptureEditState</c>. Neither half is a mark: they have no shape to
/// select and no undo step of their own, and once either is burnt into the archived pixels
/// there is nothing left to say it happened — a capture sent two stops brighter reopens
/// as one that simply <em>is</em> two stops brighter, and the sliders it came from open
/// at nought over it. Storing the numbers beside the untouched pixels is what makes the
/// popover the same control the second time it is opened as the first, and what lets a
/// framed capture be reopened at all rather than archived as flat pixels.
/// </para>
/// <para>
/// Written beside the entry it belongs to, like the marks, and read back the same way:
/// nothing here throws, because the folder is one the user can edit and delete from, and
/// every way this file can be wrong has to end as "no adjustment and no frame" rather than
/// as an exception over a capture they only wanted to look at.
/// </para>
/// </remarks>
public sealed record CaptureEditState(ImageEffectsOptions Effects, BeautifyState Beautify)
{
    /// <summary>Nothing applied, which is what an entry archived without one had.</summary>
    public static CaptureEditState None { get; } = new(ImageEffectsOptions.Default, BeautifyState.Default);

    /// <summary>
    /// Whether this is worth archiving at all. macshot asks the same question before it
    /// writes the file (<c>ScreenshotHistory.swift:110</c>) — an entry whose state is the
    /// default gains nothing from a sidecar saying so.
    /// </summary>
    /// <remarks>
    /// Either half counts, which is macshot's rule too (<c>CaptureEditState.swift:36</c>).
    /// A capture nobody drew on and nobody adjusted, delivered inside a frame, is exactly
    /// the one whose sidecar is the only thing standing between the frame and the pixels.
    /// </remarks>
    [JsonIgnore]
    public bool HasPostProcessing => !Effects.Normalized().IsIdentity || !Beautify.IsIdentity;

    private static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The state as a document, ready to be written beside the capture.</summary>
    public static string Write(CaptureEditState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return JsonSerializer.Serialize(state, SerializerOptions);
    }

    /// <summary>
    /// What a document says, or <see cref="None"/> for anything it does not.
    /// </summary>
    public static CaptureEditState Read(string? document)
    {
        if (string.IsNullOrWhiteSpace(document))
        {
            return None;
        }

        try
        {
            var state = JsonSerializer.Deserialize<CaptureEditState>(document, SerializerOptions);
            if (state is null)
            {
                return None;
            }

            // Clamped on the way in as well as on the way out: the file is hand-editable,
            // and a contrast of 40 read from one would drive the reopened capture to two
            // colours with no slider position that could explain it.
            // Either half can come back null — from a document that names the field and
            // leaves it empty, and from every sidecar written before the frame was part of
            // this — which the record's own signature says is impossible. A missing frame
            // reads as no frame, which is what those older entries had.
            var effects = state.Effects is null ? ImageEffectsOptions.Default : state.Effects;
            var beautify = state.Beautify is null ? BeautifyState.Default : state.Beautify;

            return new CaptureEditState(effects.Normalized(), beautify.Normalized());
        }
        catch (JsonException)
        {
            return None;
        }
    }
}
