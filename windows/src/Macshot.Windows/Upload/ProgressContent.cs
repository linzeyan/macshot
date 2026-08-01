#if !OFFLINE
using System.Net;
using System.Net.Http.Headers;

namespace Macshot.Windows.Upload;

/// <summary>
/// A request body that says how far through it is as it is written.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ByteArrayContent"/> would do the job in one line and report nothing.
/// macshot's uploads show a percentage — <c>S3Uploader.onProgress</c> and the Drive
/// uploader's progress observation — and a recording is large enough that a toast
/// reading "Uploading..." for ninety seconds is indistinguishable from one that has
/// hung.
/// </para>
/// <para>
/// Written in fixed pieces rather than in one call so there is something to count. The
/// piece is 64 KB: small enough that a slow line still moves the number every second or
/// so, large enough that the write syscalls are not the cost of the upload.
/// </para>
/// <para>
/// The fraction it reports is how much has been handed to the socket, which on a fast
/// link reaches 100% while the far end is still reading. That is the same thing
/// <c>URLSession</c>'s <c>fractionCompleted</c> measures, so both products overstate it
/// by the same buffer.
/// </para>
/// </remarks>
internal sealed class ProgressContent : HttpContent
{
    private const int ChunkSize = 64 * 1024;

    private readonly ReadOnlyMemory<byte> _payload;
    private readonly IProgress<double>? _progress;

    public ProgressContent(ReadOnlyMemory<byte> payload, string contentType, IProgress<double>? progress)
    {
        _payload = payload;
        _progress = progress;
        Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
    }

    protected override async Task SerializeToStreamAsync(
        Stream stream,
        TransportContext? context,
        CancellationToken cancellationToken)
    {
        var written = 0;
        while (written < _payload.Length)
        {
            var take = Math.Min(ChunkSize, _payload.Length - written);
            await stream.WriteAsync(_payload.Slice(written, take), cancellationToken).ConfigureAwait(false);
            written += take;
            _progress?.Report((double)written / _payload.Length);
        }
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override bool TryComputeLength(out long length)
    {
        length = _payload.Length;
        return true;
    }
}
#endif
