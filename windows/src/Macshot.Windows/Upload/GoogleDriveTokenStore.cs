#if !OFFLINE
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Macshot.Windows.Upload;

/// <summary>What is kept between sign-in and the next upload.</summary>
/// <param name="Expiry">
/// Unix seconds, as macshot stores it. An absolute moment rather than a lifetime,
/// because a lifetime is only meaningful next to the moment it was issued and that
/// moment is exactly what a program that has been closed since does not have.
/// </param>
internal sealed record GoogleDriveTokens(
    [property: JsonPropertyName("accessToken")] string? AccessToken,
    [property: JsonPropertyName("refreshToken")] string? RefreshToken,
    [property: JsonPropertyName("expiry")] double? Expiry)
{
    public static GoogleDriveTokens None { get; } = new(null, null, null);
}

/// <summary>
/// Where the Drive tokens live.
/// </summary>
/// <remarks>
/// <para>
/// A file beside the settings, not the Windows credential store, and the same choice
/// macshot made when it moved these out of the Keychain: a credential vault asks the
/// user to approve each read, and an upload that stops to ask for permission to use the
/// account they signed into is a worse experience than the file it replaced.
/// </para>
/// <para>
/// The file is in the user's own local application data, which is already only readable
/// by them and by anything running as them. macshot sets 0600 on its copy for the same
/// reason and gets the same protection.
/// </para>
/// <para>
/// Never in <c>CaptureSettings</c>. That record is exported by
/// <c>SettingsPortability</c>, and although its filter would refuse anything called a
/// token, a refresh token is not a preference and must not be one field rename away
/// from being carried to another machine.
/// </para>
/// </remarks>
internal static class GoogleDriveTokenStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "macshot",
        "gdrive_tokens.json");

    public static GoogleDriveTokens Load()
    {
        try
        {
            return File.Exists(Path)
                ? JsonSerializer.Deserialize<GoogleDriveTokens>(File.ReadAllText(Path), Options) ?? GoogleDriveTokens.None
                : GoogleDriveTokens.None;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            // A file that cannot be read is a sign-out, not a crash. The user signs in
            // again and it is replaced.
            return GoogleDriveTokens.None;
        }
    }

    public static void Save(GoogleDriveTokens tokens)
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(Path, JsonSerializer.Serialize(tokens, Options));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Best effort: the upload that is about to happen still has its access token
            // in memory. The cost is signing in again next time.
        }
    }

    public static void Clear()
    {
        try
        {
            File.Delete(Path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }
}
#endif
