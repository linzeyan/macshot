#if !OFFLINE
using System.Net.Http.Headers;
using Macshot.Windows.Core.Upload;

namespace Macshot.Windows.Upload;

/// <summary>
/// Puts an image on imgbb and hands back the public link.
/// </summary>
/// <remarks>
/// <para>
/// The whole of macshot's <c>ImgbbUploader</c>: one multipart POST carrying the PNG as
/// base64, which is what the endpoint takes. Base64 costs a third of the transfer over
/// sending the bytes, and it is what the API documents; a screenshot is small enough
/// that the difference is not worth a second code path.
/// </para>
/// <para>
/// Images only. imgbb has no video endpoint, which is why the video editor's Upload
/// button is dark while this is the chosen provider.
/// </para>
/// </remarks>
internal static class ImgbbUploader
{
    private const string Endpoint = "https://api.imgbb.com/1/upload";

    /// <summary>Uploads PNG bytes, reporting how far through it is.</summary>
    public static async Task<UploadOutcome> UploadAsync(
        HttpClient client,
        byte[] png,
        string apiKey,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var key = string.IsNullOrWhiteSpace(apiKey) ? ImgbbResponse.SharedApiKey : apiKey.Trim();
        var boundary = Guid.NewGuid().ToString();

        // Assembled by hand rather than with MultipartFormDataContent so the body can be
        // one buffer that ProgressContent can count through. The parts are exactly
        // macshot's: a single "image" field holding base64, and nothing else.
        var head = $"--{boundary}\r\nContent-Disposition: form-data; name=\"image\"\r\n\r\n";
        var tail = $"\r\n--{boundary}--\r\n";
        var body = new MemoryStream();
        Write(body, head);
        Write(body, Convert.ToBase64String(png));
        Write(body, tail);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Endpoint}?key={Uri.EscapeDataString(key)}")
        {
            Content = new ProgressContent(body.ToArray(), "text/plain", progress),
        };
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={boundary}");

        try
        {
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ImgbbResponse.Read(text);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            // An upload is an extra offered on top of a capture the user already has, so
            // a network that is not there has to end as a sentence in the toast.
            return UploadOutcome.Failed(error.Message);
        }

        static void Write(Stream stream, string text)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}
#endif
