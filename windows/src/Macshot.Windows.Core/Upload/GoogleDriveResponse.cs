using System.Text.Json;

namespace Macshot.Windows.Core.Upload;

/// <summary>What a token endpoint handed back.</summary>
/// <param name="AccessToken">Used until <paramref name="ExpiresInSeconds"/> runs out.</param>
/// <param name="RefreshToken">
/// Null on a refresh, which does not reissue one — the caller keeps the token it already
/// had. Never null on the first exchange, or there would be no way back after an hour.
/// </param>
/// <param name="ExpiresInSeconds">How long the access token is good for.</param>
public sealed record GoogleDriveToken(string AccessToken, string? RefreshToken, int ExpiresInSeconds);

/// <summary>
/// Reads the four Google endpoints macshot's Drive uploader talks to.
/// </summary>
/// <remarks>
/// <para>
/// Google reports failure as a 200 with an <c>error</c> object at least as often as it
/// reports it as a status code, so every reader here looks for that object first. The
/// status code is folded into the sentence rather than replacing it: "File not found"
/// alone does not say which request failed, and macshot's own messages carry both.
/// </para>
/// <para>
/// In Core, and not compiled out of the offline build, for the reason the other readers
/// in this folder give — parsing is testable and reaches nothing. The flow that calls it
/// is what the offline build removes.
/// </para>
/// </remarks>
public static class GoogleDriveResponse
{
    /// <summary>How long before expiry a token is treated as spent — macshot's minute.</summary>
    /// <remarks>
    /// An upload that starts at 59 minutes and 59 seconds would fail halfway through with
    /// a 401 that costs the whole transfer. Retiring the token a minute early costs one
    /// extra refresh an hour.
    /// </remarks>
    public const int ExpiryMarginSeconds = 60;

    /// <summary>The default lifetime, for a response that does not say.</summary>
    public const int DefaultExpirySeconds = 3600;

    /// <summary>Reads an access token out of a token or refresh response.</summary>
    public static GoogleDriveToken? ReadToken(string? json)
    {
        var root = Parse(json);
        if (root is not { ValueKind: JsonValueKind.Object } body)
        {
            return null;
        }

        if (Text(body, "access_token") is not { } accessToken)
        {
            return null;
        }

        var expiry = body.TryGetProperty("expires_in", out var expires)
            && expires.ValueKind == JsonValueKind.Number
            && expires.TryGetInt32(out var seconds)
                ? seconds
                : DefaultExpirySeconds;

        return new GoogleDriveToken(accessToken, Text(body, "refresh_token"), expiry);
    }

    /// <summary>
    /// Reads the id of the macshot folder out of a file search, or null when the search
    /// ran and found nothing — which is the signal to create it.
    /// </summary>
    /// <param name="failure">
    /// Set when the search itself did not work, which is a different thing from finding
    /// no folder: creating a second folder because a request failed is how a Drive ends
    /// up with five of them.
    /// </param>
    public static string? ReadFolderId(string? json, int statusCode, out string? failure)
    {
        var root = Parse(json);
        if (root is not { ValueKind: JsonValueKind.Object } body)
        {
            failure = $"Folder search: invalid response (HTTP {statusCode})";
            return null;
        }

        if (ErrorMessage(body) is { } error)
        {
            failure = $"Folder search: {error} (HTTP {statusCode})";
            return null;
        }

        if (!body.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
        {
            failure = $"Folder search: unexpected response format (HTTP {statusCode})";
            return null;
        }

        failure = null;
        foreach (var file in files.EnumerateArray())
        {
            if (file.ValueKind == JsonValueKind.Object && Text(file, "id") is { } id)
            {
                return id;
            }
        }

        return null;
    }

    /// <summary>Reads the id of a folder that has just been created.</summary>
    public static string? ReadCreatedFolderId(string? json, int statusCode, out string? failure)
    {
        var root = Parse(json);
        if (root is not { ValueKind: JsonValueKind.Object } body)
        {
            failure = $"Create folder: invalid response (HTTP {statusCode})";
            return null;
        }

        if (ErrorMessage(body) is { } error)
        {
            failure = $"Create folder: {error} (HTTP {statusCode})";
            return null;
        }

        if (Text(body, "id") is not { } id)
        {
            failure = $"Create folder: missing folder ID in response (HTTP {statusCode})";
            return null;
        }

        failure = null;
        return id;
    }

    /// <summary>Reads an upload response into the link that opens the file.</summary>
    public static UploadOutcome ReadUpload(string? json, int statusCode)
    {
        var root = Parse(json);
        if (root is not { ValueKind: JsonValueKind.Object } body)
        {
            return UploadOutcome.Failed($"Upload returned no data (HTTP {statusCode})");
        }

        if (ErrorMessage(body) is { } error)
        {
            return UploadOutcome.Failed($"Upload: {error} (HTTP {statusCode})");
        }

        return Text(body, "id") is { } id
            ? UploadOutcome.Uploaded(ViewLink(id))
            : UploadOutcome.Failed($"Upload failed (HTTP {statusCode})");
    }

    /// <summary>Where a Drive file with this id can be opened.</summary>
    public static string ViewLink(string fileId) => $"https://drive.google.com/file/d/{fileId}/view";

    /// <summary>Reads the signed-in address out of a userinfo response, for the settings window.</summary>
    public static string? ReadEmail(string? json)
    {
        var root = Parse(json);
        return root is { ValueKind: JsonValueKind.Object } body ? Text(body, "email") : null;
    }

    private static JsonElement? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            // Cloned because the document is disposed on the way out and an element
            // borrowed from a disposed document reads as garbage rather than throwing.
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ErrorMessage(JsonElement body) =>
        body.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
            ? Text(error, "message")
            : null;

    private static string? Text(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
