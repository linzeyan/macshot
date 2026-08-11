using System.Text.Json;
using System.Text.Json.Serialization;

namespace Macshot.Windows.Core.Imaging;

/// <summary>
/// The post-processing a capture is carrying, kept as numbers rather than as pixels so
/// that reopening it can undo them.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>CaptureEditState</c>. An adjustment is not a mark: it has no shape to
/// select and no undo step of its own, and once it is burnt into the archived pixels
/// there is nothing left to say it happened — a capture sent two stops brighter reopens
/// as one that simply <em>is</em> two stops brighter, and the sliders it came from open
/// at nought over it. Storing the numbers beside the untouched pixels is what makes the
/// popover the same control the second time it is opened as the first.
/// </para>
/// <para>
/// Written beside the entry it belongs to, like the marks, and read back the same way:
/// nothing here throws, because the folder is one the user can edit and delete from, and
/// every way this file can be wrong has to end as "no adjustment" rather than as an
/// exception over a capture they only wanted to look at.
/// </para>
/// </remarks>
public sealed record CaptureEditState(ImageEffectsOptions Effects)
{
    /// <summary>Nothing applied, which is what an entry archived without one had.</summary>
    public static CaptureEditState None { get; } = new(ImageEffectsOptions.Default);

    /// <summary>
    /// Whether this is worth archiving at all. macshot asks the same question before it
    /// writes the file (<c>ScreenshotHistory.swift:110</c>) — an entry whose state is the
    /// default gains nothing from a sidecar saying so.
    /// </summary>
    [JsonIgnore]
    public bool HasPostProcessing => !Effects.Normalized().IsIdentity;

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

            // Clamped on the way in as well as on the way out: the file is hand-editable,
            // and a contrast of 40 read from one would drive the reopened capture to two
            // colours with no slider position that could explain it.
            // Effects can come back null from a document that names the field and leaves
            // it empty, which the record's own signature says is impossible.
            return state?.Effects is null ? None : state with { Effects = state.Effects.Normalized() };
        }
        catch (JsonException)
        {
            return None;
        }
    }
}
