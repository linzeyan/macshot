using System.Text.Json;
using System.Text.Json.Serialization;
using Macshot.Windows.Core.Output;

namespace Macshot.Windows.Services;

/// <summary>
/// Reads and writes macshot's preferences, the Windows counterpart of macOS's
/// <c>UserDefaults</c>.
/// </summary>
/// <remarks>
/// A JSON file under <c>%LOCALAPPDATA%</c> rather than
/// <c>ApplicationData.LocalSettings</c>, because the latter needs package
/// identity and macshot currently builds unpackaged. See
/// <c>docs/windows-port/architecture.md</c>.
/// </remarks>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // The file is meant to be hand-editable, and "Jpeg" survives a reordering
        // of the enum in a way that a bare 1 does not.
        Converters = { new JsonStringEnumConverter() },
    };

    private CaptureSettings _current;

    public SettingsStore()
        : this(DefaultPath)
    {
    }

    public SettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
        _current = Read(path);
    }

    /// <summary>Raised after a successful save so open windows can re-read the values.</summary>
    public event EventHandler<CaptureSettings>? Changed;

    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "macshot",
        "settings.json");

    public string Path { get; }

    public CaptureSettings Current => _current;

    public void Save(CaptureSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var normalized = settings.Normalized();
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(Path, JsonSerializer.Serialize(normalized, SerializerOptions));
        _current = normalized;
        Changed?.Invoke(this, normalized);
    }

    /// <summary>
    /// Falls back to the defaults for a missing, unreadable, or corrupt file. Losing
    /// preferences is a nuisance; refusing to capture because a JSON brace is missing
    /// would make the app unusable, and the next save repairs the file.
    /// </summary>
    private static CaptureSettings Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return CaptureSettings.Default;
            }

            var settings = JsonSerializer.Deserialize<CaptureSettings>(
                File.ReadAllText(path),
                SerializerOptions);
            return (settings ?? CaptureSettings.Default).Normalized();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return CaptureSettings.Default;
        }
    }
}
