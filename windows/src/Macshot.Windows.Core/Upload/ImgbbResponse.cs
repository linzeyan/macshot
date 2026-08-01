using System.Text.Json;

namespace Macshot.Windows.Core.Upload;

/// <summary>
/// Reads what imgbb answers a POST to <c>api.imgbb.com/1/upload</c> with.
/// </summary>
/// <remarks>
/// <para>
/// In Core, away from the request, for the reason <see cref="Recognition.TranslationResponse"/>
/// gives: the parsing is the part that can be wrong in ways worth a test, and the part
/// that has to survive the service changing its mind about error shapes. This file is
/// not compiled out of the offline build and does not need to be — it reaches nothing.
/// </para>
/// <para>
/// The three ways macshot reads an error are kept in macshot's order — the nested
/// <c>error.message</c>, then a bare <c>status_code</c>, then a stand-in — because that
/// order is what decides which sentence the user sees when the body carries both.
/// </para>
/// </remarks>
public static class ImgbbResponse
{
    /// <summary>
    /// The key that is used when the user has not supplied one.
    /// </summary>
    /// <remarks>
    /// macshot's <c>ImgbbUploader.defaultAPIKey</c>, shared by everyone who has not got
    /// their own. Copied rather than referenced so that a user who moves between the two
    /// products does not have to notice which one they are running; it is a rate limit
    /// they share, not a secret — the settings window says as much and points at
    /// imgbb.com/api for anyone who hits it.
    /// </remarks>
    public const string SharedApiKey = "c2c63d156c6baa11136a464dcd22a404";

    /// <summary>Reads the body, giving the link and the delete link or the reason there is none.</summary>
    public static UploadOutcome Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return UploadOutcome.Failed("imgbb returned nothing.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            // A rate-limited or blocked caller gets an HTML page, so this is an ordinary
            // way for imgbb to say no rather than an exceptional one.
            return UploadOutcome.Failed("imgbb returned something unreadable.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return UploadOutcome.Failed("imgbb returned something unreadable.");
            }

            var succeeded = root.TryGetProperty("success", out var success)
                && success.ValueKind == JsonValueKind.True;

            if (succeeded
                && root.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Object
                && Text(data, "url") is { } link
                && Text(data, "delete_url") is { } deleteLink)
            {
                return UploadOutcome.Uploaded(link, deleteLink);
            }

            return UploadOutcome.Failed(ErrorMessage(root));
        }
    }

    private static string ErrorMessage(JsonElement root)
    {
        if (root.TryGetProperty("error", out var error)
            && error.ValueKind == JsonValueKind.Object
            && Text(error, "message") is { } message)
        {
            return message;
        }

        if (root.TryGetProperty("status_code", out var status)
            && status.ValueKind == JsonValueKind.Number
            && status.TryGetInt32(out var code))
        {
            return $"API error (status {code})";
        }

        return "Unknown error";
    }

    private static string? Text(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
