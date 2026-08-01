using System.Text.Json;
using System.Text.Json.Serialization;

namespace Macshot.Windows.Core.Output;

/// <summary>
/// How <see cref="CaptureSettings"/> is written, wherever it is written.
/// </summary>
/// <remarks>
/// One copy, shared by the settings file and by <see cref="SettingsPortability"/>,
/// because the two have to agree on every property name. A second set of options
/// somewhere else is a bug that only shows up when an exported file is imported and
/// half the settings quietly do not take.
/// </remarks>
public static class CaptureSettingsJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,

        // The file is meant to be hand-editable, and "Jpeg" survives a reordering
        // of the enum in a way that a bare 1 does not.
        Converters = { new JsonStringEnumConverter() },
    };
}
