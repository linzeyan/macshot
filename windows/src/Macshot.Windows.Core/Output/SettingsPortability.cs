using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Macshot.Windows.Core.Output;

/// <summary>What an export produced.</summary>
public sealed record SettingsExport(string Json, int KeyCount);

/// <summary>
/// What an import produced: the settings to save, or the reason there are none.
/// </summary>
public sealed record SettingsImport(
    CaptureSettings? Settings,
    string? Failure,
    int AppliedCount,
    IReadOnlyList<string> SkippedKeys,
    string? SourceAppVersion)
{
    public bool Succeeded => Settings is not null;

    public static SettingsImport Failed(string reason) => new(null, reason, 0, [], null);
}

/// <summary>
/// Moving preferences to another machine, the counterpart of macshot's
/// <c>Services/SettingsPortability.swift</c>.
/// </summary>
/// <remarks>
/// <para>
/// This file exists because "the port's settings are already one readable JSON file, so
/// copying it is the export" was written into the parity document and was wrong. Copying
/// the file takes <see cref="CaptureSettings.SaveDirectory"/> — a path the other machine
/// does not have — along with anything credential-shaped in it. macshot's exporter drops
/// both, and dropping them is most of what an exporter is for.
/// </para>
/// <para>
/// The filter works on the **serialized name**, not on a hand-kept list of properties.
/// macshot filters `UserDefaults` keys by their shape for the same reason: a new setting
/// then exports with no maintenance at all, where an allow-list means every feature has
/// to remember to register itself or silently stops transferring. The port has it easier
/// — the property set is closed and known — but the failure mode of a list that someone
/// has to remember is identical, so the rule is a rule rather than a list.
/// </para>
/// <para>
/// <see cref="LooksSecret"/> fails **closed**: a future setting called `dropboxToken`
/// is excluded the day it is added, without this file being touched. That is macshot's
/// guarantee and the one worth keeping, even though the port has no secret left in
/// <see cref="CaptureSettings"/> today — the translation key that used to be there is
/// exactly the kind of thing that comes back.
/// </para>
/// <para>
/// There is no size cap on values here, where macshot caps a single <c>Data</c> blob at
/// 2 MB. macshot needs one because a custom Beautify background lives in its defaults;
/// every value in this record is a scalar, a short string, or a small list.
/// </para>
/// </remarks>
public static class SettingsPortability
{
    /// <summary>Names the envelope so an unrelated JSON file is refused rather than half-applied.</summary>
    public const string FileType = "macshot-settings";

    public const int SchemaVersion = 1;

    /// <summary>
    /// Settings that describe *this machine* rather than this user's preferences. Taking
    /// them along is worse than dropping them: a save directory from another computer
    /// either does not exist or belongs to someone else.
    /// </summary>
    private static readonly HashSet<string> MachineSpecificKeys = new(StringComparer.Ordinal)
    {
        // A path, and on the macOS side a security-scoped bookmark with it.
        "saveDirectory",

        // Where the last selection was, in the coordinates of a display this machine has.
        "lastSelection",
        "lastSelectionDisplay",
    };

    /// <summary>
    /// Substrings that mark a name as a credential. macshot's list, kept identical so a
    /// setting that is refused on one platform is refused on the other.
    /// </summary>
    private static readonly string[] SecretSubstrings =
    [
        "apikey", "secret", "token", "password", "credential", "bookmark", "s3",
    ];

    /// <summary>
    /// The serialized name of every property that can be *set*, to the property itself.
    /// <see cref="CaptureSettings"/> also has computed getters — the parsed hotkey
    /// bindings, the effective history size — which serialize but cannot be read back.
    /// Exporting those would put values in the file that an import silently ignores.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, PropertyInfo> Properties = BuildPropertyIndex();

    public static bool LooksSecret(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var lower = key.ToLowerInvariant();
        return SecretSubstrings.Any(lower.Contains);
    }

    /// <summary>Whether a setting is safe to carry to another machine.</summary>
    public static bool IsPortable(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        // Secrets first, so nothing below can force one through.
        return !LooksSecret(key) && !MachineSpecificKeys.Contains(key);
    }

    /// <summary>
    /// Writes the portable half of <paramref name="settings"/> into a dated envelope.
    /// </summary>
    /// <param name="appVersion">
    /// Passed in rather than read: Core has no bundle to ask. Shown back to the user on
    /// import, where "made by 1.2" explains a setting that did not take.
    /// </param>
    public static SettingsExport Export(
        CaptureSettings settings,
        string appVersion,
        DateTimeOffset exportedAt)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var source = ToObject(settings);
        var portable = new JsonObject();
        foreach (var (key, value) in source)
        {
            if (IsPortable(key) && Properties.ContainsKey(key))
            {
                portable[key] = value?.DeepClone();
            }
        }

        var envelope = new JsonObject
        {
            ["type"] = FileType,
            ["schemaVersion"] = SchemaVersion,
            ["appVersion"] = appVersion,
            ["exportedAt"] = exportedAt.ToString("O"),
            ["settings"] = portable,
        };

        return new SettingsExport(
            envelope.ToJsonString(CaptureSettingsJson.Options),
            portable.Count);
    }

    /// <summary>
    /// Reads an exported file and answers the settings to save, leaving this machine's
    /// own values in place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// **Replace-portable**, which is macshot's semantics: every portable setting comes
    /// from the file, and one the file does not mention goes back to its default rather
    /// than keeping whatever was here. Anything not portable — the save directory, the
    /// last selection, a future credential — is untouched. Without that rule an import
    /// would be a merge, and a user who exported a tidy configuration would get their
    /// old mess back wherever the file happened to be silent.
    /// </para>
    /// <para>
    /// Portability is re-checked on the way in. A hand-edited file naming
    /// <c>saveDirectory</c> must not be able to redirect where captures land on the
    /// machine it is imported into.
    /// </para>
    /// <para>
    /// A value of the wrong type costs that one setting, not the file. An import is a
    /// user action on a file that may have been edited by hand, and refusing all of it
    /// over one bad number would be the least useful answer available.
    /// </para>
    /// </remarks>
    public static SettingsImport Import(string? json, CaptureSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (string.IsNullOrWhiteSpace(json))
        {
            return SettingsImport.Failed("This file is not a macshot settings file.");
        }

        JsonObject envelope;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject parsed)
            {
                return SettingsImport.Failed("This file is not a macshot settings file.");
            }

            envelope = parsed;
        }
        catch (JsonException)
        {
            return SettingsImport.Failed("This file is not a macshot settings file.");
        }

        if (Text(envelope, "type") != FileType)
        {
            return SettingsImport.Failed("This file is not a macshot settings file.");
        }

        // A newer file is refused rather than partly applied: this version cannot know
        // which of its settings the newer one redefined.
        if (Number(envelope, "schemaVersion") is { } schema && schema > SchemaVersion)
        {
            return SettingsImport.Failed(
                "This settings file was made by a newer version of macshot. Update macshot first.");
        }

        if (envelope["settings"] is not JsonObject incoming || incoming.Count == 0)
        {
            return SettingsImport.Failed("This settings file contains no settings.");
        }

        // Start from the defaults so a portable setting the file omits reverts, then put
        // this machine's non-portable values back.
        var merged = ToObject(CaptureSettings.Default);
        foreach (var (key, value) in ToObject(current))
        {
            if (!IsPortable(key))
            {
                merged[key] = value?.DeepClone();
            }
        }

        var applied = 0;
        var skipped = new List<string>();
        foreach (var (key, value) in incoming)
        {
            if (!IsPortable(key)
                || !Properties.TryGetValue(key, out var property)
                || !Readable(value, property))
            {
                skipped.Add(key);
                continue;
            }

            merged[key] = value?.DeepClone();
            applied++;
        }

        var settings = merged.Deserialize<CaptureSettings>(CaptureSettingsJson.Options);
        if (settings is null)
        {
            return SettingsImport.Failed("This settings file could not be read.");
        }

        return new SettingsImport(
            settings.Normalized(),
            null,
            applied,
            skipped.Order(StringComparer.Ordinal).ToArray(),
            Text(envelope, "appVersion"));
    }

    /// <summary>
    /// Whether this value can become that property. Checked per key so that one setting
    /// of the wrong type is dropped instead of taking the whole file down with it.
    /// </summary>
    private static bool Readable(JsonNode? value, PropertyInfo property)
    {
        try
        {
            value.Deserialize(property.PropertyType, CaptureSettingsJson.Options);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return false;
        }
    }

    private static JsonObject ToObject(CaptureSettings settings) =>
        JsonSerializer.SerializeToNode(settings, CaptureSettingsJson.Options) as JsonObject
            ?? new JsonObject();

    private static string? Text(JsonObject envelope, string key) =>
        envelope[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static int? Number(JsonObject envelope, string key) =>
        envelope[key] is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;

    private static Dictionary<string, PropertyInfo> BuildPropertyIndex()
    {
        var index = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
        foreach (var property in typeof(CaptureSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || property.SetMethod is null)
            {
                continue;
            }

            var name = CaptureSettingsJson.Options.PropertyNamingPolicy?.ConvertName(property.Name)
                ?? property.Name;
            index[name] = property;
        }

        return index;
    }
}
