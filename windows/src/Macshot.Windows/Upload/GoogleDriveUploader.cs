#if !OFFLINE
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Macshot.Windows.Core.Upload;

namespace Macshot.Windows.Upload;

/// <summary>
/// Signs in to Google, and puts files in a folder called macshot.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>GoogleDriveUploader</c>, with the same client id, the same
/// <c>drive.file</c> scope, the same folder, and the same PKCE exchange. That scope is
/// the narrow one: it grants access to files this app created and to nothing else in
/// the Drive, so signing in does not hand a screenshot tool the user's documents.
/// </para>
/// <para>
/// Nothing here is shared publicly. A Drive upload is a link into the user's own
/// storage, which is why the toast offers no delete link for it — the file is deleted
/// where it lives.
/// </para>
/// <para>
/// Not verified against Google on hardware. The parts that could be checked without an
/// account — the PKCE encoding, every response shape — are in Core with tests.
/// </para>
/// </remarks>
internal sealed class GoogleDriveUploader
{
    private const string Scopes = "https://www.googleapis.com/auth/drive.file email";
    private const string AuthorizeUrl = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenUrl = "https://oauth2.googleapis.com/token";
    private const string FilesUrl = "https://www.googleapis.com/drive/v3/files";
    private const string UploadUrl = "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart";
    private const string UserInfoUrl = "https://www.googleapis.com/oauth2/v2/userinfo";

    /// <summary>The folder every upload lands in, created on the first one.</summary>
    private const string FolderName = "macshot";

    private readonly HttpClient _client;

    /// <summary>
    /// Remembered for the life of the process, as macshot remembers it: the search that
    /// finds it is two round trips in front of every upload otherwise.
    /// </summary>
    private string? _folderId;

    public GoogleDriveUploader(HttpClient client) => _client = client;

    /// <summary>Whether there is a refresh token, which is what surviving a restart means.</summary>
    public static bool IsSignedIn => !string.IsNullOrEmpty(GoogleDriveTokenStore.Load().RefreshToken);

    /// <summary>
    /// Runs the whole sign-in: browser, redirect, code exchange, and the address to show
    /// in the settings window. Null means it did not happen — including because the user
    /// closed the browser, which is not an error worth a dialog.
    /// </summary>
    public async Task<string?> SignInAsync(CancellationToken cancellationToken)
    {
        var codes = PkceChallenge.Create();

        var query = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["client_id"] = GoogleOAuthRedirect.ClientId,
            ["redirect_uri"] = GoogleOAuthRedirect.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = Scopes,
            ["code_challenge"] = codes.Challenge,
            ["code_challenge_method"] = "S256",

            // Without both of these Google issues an access token and no refresh token,
            // and the sign-in lasts an hour rather than until the user says otherwise.
            ["access_type"] = "offline",
            ["prompt"] = "consent",
        };

        var url = AuthorizeUrl + "?" + string.Join('&', query.Select(Encoded));
        var redirect = await GoogleOAuthRedirect.AwaitRedirectAsync(url, cancellationToken).ConfigureAwait(false);
        if (GoogleOAuthRedirect.CodeFrom(redirect) is not { } code)
        {
            return null;
        }

        var token = await ExchangeAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["code"] = code,
                ["client_id"] = GoogleOAuthRedirect.ClientId,
                ["redirect_uri"] = GoogleOAuthRedirect.RedirectUri,
                ["grant_type"] = "authorization_code",
                ["code_verifier"] = codes.Verifier,
            },
            cancellationToken).ConfigureAwait(false);

        if (token?.RefreshToken is null)
        {
            return null;
        }

        Store(token, token.RefreshToken);
        return await ReadEmailAsync(token.AccessToken, cancellationToken).ConfigureAwait(false) ?? string.Empty;
    }

    /// <summary>Forgets the account, and gives the URL scheme back.</summary>
    public static void SignOut()
    {
        GoogleDriveTokenStore.Clear();
        GoogleOAuthRedirect.Unregister();
    }

    /// <summary>Uploads a file into the macshot folder.</summary>
    public async Task<UploadOutcome> UploadAsync(
        byte[] payload,
        string filename,
        string contentType,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var accessToken = await ValidTokenAsync(cancellationToken).ConfigureAwait(false);
        if (accessToken is null)
        {
            return UploadOutcome.Failed("Not signed in");
        }

        var folder = await FolderIdAsync(accessToken, cancellationToken).ConfigureAwait(false);
        if (folder.Id is null)
        {
            return UploadOutcome.Failed(folder.Failure ?? "Could not reach the macshot folder");
        }

        return await SendAsync(payload, filename, contentType, folder.Id, accessToken, progress, attempt: 1, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<UploadOutcome> SendAsync(
        byte[] payload,
        string filename,
        string contentType,
        string folderId,
        string accessToken,
        IProgress<double>? progress,
        int attempt,
        CancellationToken cancellationToken)
    {
        // Multipart/related: the metadata that names the file and puts it in the folder,
        // then the bytes. One request rather than a resumable session, as macshot does —
        // a resumable upload is worth its extra round trips for files larger than these.
        var boundary = Guid.NewGuid().ToString();
        var metadata = JsonSerializer.Serialize(new { name = filename, parents = new[] { folderId } });

        var body = new MemoryStream();
        Write(body, $"--{boundary}\r\nContent-Type: application/json; charset=UTF-8\r\n\r\n");
        Write(body, metadata);
        Write(body, $"\r\n--{boundary}\r\nContent-Type: {contentType}\r\n\r\n");
        body.Write(payload, 0, payload.Length);
        Write(body, $"\r\n--{boundary}--\r\n");

        using var request = new HttpRequestMessage(HttpMethod.Post, UploadUrl)
        {
            Content = new ProgressContent(body.ToArray(), "text/plain", progress),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/related; boundary={boundary}");

        const int maxAttempts = 3;
        try
        {
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);

            // A token that expired mid-upload. Refresh once and send it again rather than
            // making the user watch a large file fail over a minute of clock skew.
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt < maxAttempts)
            {
                var refreshed = await RefreshAsync(cancellationToken).ConfigureAwait(false);
                if (refreshed is null)
                {
                    return UploadOutcome.Failed("Authentication expired");
                }

                return await SendAsync(
                    payload, filename, contentType, folderId, refreshed, progress, attempt + 1, cancellationToken)
                    .ConfigureAwait(false);
            }

            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return GoogleDriveResponse.ReadUpload(text, (int)response.StatusCode);
        }
        catch (HttpRequestException) when (attempt < maxAttempts)
        {
            // A dropped connection part-way through a recording is common enough on a
            // laptop that changes network that macshot retries it, backing off as it goes.
            await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken).ConfigureAwait(false);
            return await SendAsync(
                payload, filename, contentType, folderId, accessToken, progress, attempt + 1, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            return UploadOutcome.Failed(error.Message);
        }

        static void Write(Stream stream, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }
    }

    private async Task<(string? Id, string? Failure)> FolderIdAsync(string accessToken, CancellationToken cancellationToken)
    {
        if (_folderId is not null)
        {
            return (_folderId, null);
        }

        var query = Uri.EscapeDataString(
            $"name='{FolderName}' and mimeType='application/vnd.google-apps.folder' and trashed=false");

        using var search = new HttpRequestMessage(HttpMethod.Get, $"{FilesUrl}?q={query}&fields=files(id)");
        search.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            using var response = await _client.SendAsync(search, cancellationToken).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var found = GoogleDriveResponse.ReadFolderId(text, (int)response.StatusCode, out var failure);

            if (failure is not null)
            {
                // A search that failed is not a search that found nothing: creating a
                // folder here is how a Drive ends up with five of them.
                return (null, failure);
            }

            if (found is not null)
            {
                _folderId = found;
                return (found, null);
            }
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            return (null, $"Folder search failed: {error.Message}");
        }

        return await CreateFolderAsync(accessToken, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(string? Id, string? Failure)> CreateFolderAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, FilesUrl)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { name = FolderName, mimeType = "application/vnd.google-apps.folder" }),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var id = GoogleDriveResponse.ReadCreatedFolderId(text, (int)response.StatusCode, out var failure);
            _folderId = id;
            return (id, failure);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            return (null, $"Create folder failed: {error.Message}");
        }
    }

    /// <summary>An access token that is still good, refreshing it first if it is not.</summary>
    private async Task<string?> ValidTokenAsync(CancellationToken cancellationToken)
    {
        var stored = GoogleDriveTokenStore.Load();
        if (stored.Expiry is { } expiry
            && stored.AccessToken is { } token
            && DateTimeOffset.UtcNow.ToUnixTimeSeconds() < expiry)
        {
            return token;
        }

        return await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> RefreshAsync(CancellationToken cancellationToken)
    {
        if (GoogleDriveTokenStore.Load().RefreshToken is not { } refreshToken)
        {
            return null;
        }

        var token = await ExchangeAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["refresh_token"] = refreshToken,
                ["client_id"] = GoogleOAuthRedirect.ClientId,
                ["grant_type"] = "refresh_token",
            },
            cancellationToken).ConfigureAwait(false);

        if (token is null)
        {
            return null;
        }

        // A refresh reissues no refresh token, so the one already held is kept. Treating
        // its absence as a failure would sign the user out an hour after signing in.
        Store(token, refreshToken);
        return token.AccessToken;
    }

    private async Task<GoogleDriveToken?> ExchangeAsync(
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        try
        {
            using var content = new FormUrlEncodedContent(form);
            using var response = await _client.PostAsync(TokenUrl, content, cancellationToken).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return GoogleDriveResponse.ReadToken(text);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private async Task<string?> ReadEmailAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UserInfoUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return GoogleDriveResponse.ReadEmail(text);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            // The address is only shown in the settings window. A sign-in that worked
            // must not be reported as failed because the name of the account is unknown.
            return null;
        }
    }

    private static void Store(GoogleDriveToken token, string refreshToken) =>
        GoogleDriveTokenStore.Save(new GoogleDriveTokens(
            token.AccessToken,
            refreshToken,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                + token.ExpiresInSeconds
                - GoogleDriveResponse.ExpiryMarginSeconds));

    private static string Encoded(KeyValuePair<string, string> pair) =>
        $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}";
}
#endif
