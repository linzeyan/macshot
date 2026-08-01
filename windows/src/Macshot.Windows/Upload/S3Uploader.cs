#if !OFFLINE
using Macshot.Windows.Core.Upload;

namespace Macshot.Windows.Upload;

/// <summary>
/// Puts an object in an S3-compatible bucket with a signature it made itself.
/// </summary>
/// <remarks>
/// Everything that can be got wrong here — the canonical request, the header order, the
/// path encoding, the signing key — is in <see cref="S3Request"/> in Core, where it is
/// tested. What is left is the send, which is the part no test can stand in for.
/// </remarks>
internal static class S3Uploader
{
    public static async Task<UploadOutcome> UploadAsync(
        HttpClient client,
        S3Settings settings,
        byte[] payload,
        string filename,
        string contentType,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var plan = S3Request.Build(settings, filename, contentType, payload, DateTimeOffset.UtcNow, out var failure);
        if (plan is null)
        {
            return UploadOutcome.Failed(failure ?? "S3 not configured — check Settings");
        }

        using var request = new HttpRequestMessage(HttpMethod.Put, plan.Url)
        {
            Content = new ProgressContent(payload, contentType, progress),
        };

        foreach (var (name, value) in plan.Headers)
        {
            if (string.Equals(name, "Host", StringComparison.OrdinalIgnoreCase))
            {
                // Set explicitly as well as signed over. HttpClient would fill it in from
                // the URL and get the same answer, but a proxy configuration that rewrote
                // it would produce a 403 about credentials rather than about the proxy.
                request.Headers.Host = value;
                continue;
            }

            request.Headers.TryAddWithoutValidation(name, value);
        }

        try
        {
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return UploadOutcome.Uploaded(plan.PublicLink);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var message = S3Request.ErrorMessage(body) ?? $"HTTP {(int)response.StatusCode}";
            return UploadOutcome.Failed($"S3 error ({(int)response.StatusCode}): {message}");
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            return UploadOutcome.Failed(error.Message);
        }
    }
}
#endif
