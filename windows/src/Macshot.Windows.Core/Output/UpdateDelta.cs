using System.IO.Compression;
using System.IO.Hashing;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Macshot.Windows.Core.Output;

/// <summary>What making a release's folder cost, for the log.</summary>
/// <param name="Fetched">Files that differed from the installed copy and were downloaded.</param>
/// <param name="Reused">Files the installed copy already had, copied from it instead.</param>
/// <param name="FetchedBytes">Everything that crossed the network, directory included.</param>
/// <param name="ArchiveBytes">What downloading the whole release would have cost.</param>
/// <param name="Requests">Ranged requests made, the first one included.</param>
public readonly record struct UpdateDeltaResult(
    int Fetched,
    int Reused,
    long FetchedBytes,
    long ArchiveBytes,
    int Requests);

/// <summary>
/// Makes a release's folder out of its zip without downloading the zip: the files that
/// differ from the installed copy are fetched out of it by byte range, and the rest are
/// copied from the installation.
/// </summary>
/// <remarks>
/// <para>
/// A release is 77MB and nearly all of it is the .NET runtime and the Windows App SDK,
/// which do not change from one release to the next. Measured across 0.8.10, 0.8.14 and
/// 0.8.15 to 0.8.16: 9 of 521 files differed each time — 2.8MB. On the network this was
/// measured from, the whole zip took three and a half minutes; downloading it over eight
/// connections instead of one was no faster, because the limit was the line and not the
/// connection. Fetching less is the only thing that helps.
/// </para>
/// <para>
/// It needs nothing from the release. A zip keeps its directory at the end, and every
/// entry in the directory carries its CRC-32, its size and where it starts — so reading
/// the last few kilobytes says which files changed, and each changed file is one ranged
/// request. GitHub's release storage answers ranges, and every release ever cut is
/// already in this shape.
/// </para>
/// <para>
/// The zip itself is read by <see cref="ZipArchive"/> through <see cref="RangeStream"/>,
/// rather than by a parser written for this: the format has corners — ZIP64, data
/// descriptors, stored entries — that the framework already reads correctly.
/// </para>
/// </remarks>
public static class UpdateDelta
{
    /// <summary>
    /// What a local file header adds in front of an entry's data, beyond its fixed thirty
    /// bytes and its name. The extra field is almost always empty in a zip made on
    /// Windows; a longer one costs one more request, not a failure.
    /// </summary>
    private const int ExtraAllowance = 256;

    /// <summary>
    /// Writes the release <paramref name="archive"/> holds into <paramref name="payload"/>,
    /// taking from <paramref name="installed"/> every file that is already right there.
    /// </summary>
    /// <remarks>
    /// A file is taken from the installation only when its size and CRC-32 both match the
    /// entry's. The CRC is not a defence against tampering and is not asked to be one: it
    /// is compared with a value from the same HTTPS answer the whole zip would have come
    /// in. Every fetched file is checked against it too, so a range answered wrongly
    /// fails here rather than being installed.
    /// </remarks>
    /// <exception cref="NotSupportedException">The server does not answer ranges.</exception>
    /// <exception cref="InvalidDataException">An entry is malformed or arrived damaged.</exception>
    public static async Task<UpdateDeltaResult> StageAsync(
        HttpClient client,
        Uri archive,
        string installed,
        string payload,
        IProgress<double>? progress,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(archive);

        var root = Path.GetFullPath(payload);
        Directory.CreateDirectory(root);

        await using var remote = await RangeStream.OpenAsync(client, archive, token).ConfigureAwait(false);
        await using var zip = await ZipArchive
            .CreateAsync(remote, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: null, token)
            .ConfigureAwait(false);

        var wanted = new List<(ZipArchiveEntry Entry, string Target)>();
        var reused = 0;

        foreach (var entry in zip.Entries)
        {
            var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            var target = Inside(root, relative, entry.FullName);

            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            var local = Path.Combine(installed, relative);
            if (await MatchesAsync(local, entry, token).ConfigureAwait(false))
            {
                File.Copy(local, target);
                reused++;
            }
            else
            {
                wanted.Add((entry, target));
            }
        }

        // Whole percent only, as the full download reports: anything finer is a call onto
        // the UI thread for nothing a person could see.
        var needed = wanted.Sum(pair => Allowance(pair.Entry) + pair.Entry.CompressedLength);
        var start = remote.Fetched;
        var reported = -1;
        remote.Progressed = fetched =>
        {
            var percent = needed == 0 ? 100 : (int)Math.Min(100, (fetched - start) * 100 / needed);
            if (percent != reported)
            {
                reported = percent;
                progress?.Report(percent / 100.0);
            }
        };

        foreach (var (entry, target) in wanted)
        {
            // One request for the header and the data together, rather than a guess the
            // stream would have to make: the entry's compressed length is in the directory.
            remote.Expect(Allowance(entry) + entry.CompressedLength);
            await ExtractAsync(entry, target, token).ConfigureAwait(false);
        }

        progress?.Report(1);
        return new UpdateDeltaResult(wanted.Count, reused, remote.Fetched, remote.Length, remote.Requests);
    }

    private static long Allowance(ZipArchiveEntry entry) =>
        30 + Encoding.UTF8.GetByteCount(entry.FullName) + ExtraAllowance;

    /// <summary>
    /// Where <paramref name="relative"/> lands under <paramref name="root"/>, refusing any
    /// name that would land outside it.
    /// </summary>
    /// <remarks>
    /// <see cref="ZipFile.ExtractToDirectory(string, string)"/> refuses these itself; this
    /// writes each file by hand, so it has to as well. The names come off the network, and
    /// one spelled <c>..\..\x</c> would otherwise write wherever it liked.
    /// </remarks>
    private static string Inside(string root, string relative, string name)
    {
        var target = Path.GetFullPath(Path.Combine(root, relative));
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;

        return target.StartsWith(prefix, StringComparison.Ordinal)
            ? target
            : throw new InvalidDataException($"The update names a file outside its own folder: {name}");
    }

    private static async Task<bool> MatchesAsync(string local, ZipArchiveEntry entry, CancellationToken token)
    {
        var file = new FileInfo(local);
        if (!file.Exists || file.Length != entry.Length)
        {
            return false;
        }

        var crc = new Crc32();
        await using var stream = new FileStream(
            local, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, useAsync: true);
        await crc.AppendAsync(stream, token).ConfigureAwait(false);

        return crc.GetCurrentHashAsUInt32() == entry.Crc32;
    }

    private static async Task ExtractAsync(ZipArchiveEntry entry, string target, CancellationToken token)
    {
        var crc = new Crc32();
        var buffer = new byte[1 << 16];

        await using (var source = await entry.OpenAsync(token).ConfigureAwait(false))
        await using (var destination = new FileStream(
            target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
        {
            int read;
            while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                crc.Append(buffer.AsSpan(0, read));
                await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            }
        }

        var actual = crc.GetCurrentHashAsUInt32();
        if (actual != entry.Crc32)
        {
            throw new InvalidDataException(
                $"{entry.FullName} arrived damaged: CRC {actual:X8}, the release says {entry.Crc32:X8}.");
        }
    }
}

/// <summary>
/// A remote file read as a seekable stream, a byte range at a time.
/// </summary>
/// <remarks>
/// Everything fetched is kept, because <see cref="ZipArchive"/> goes back over what it has
/// read — the directory first, then each entry's header, then its data — and a stream that
/// forgot would ask the network for the same bytes twice. What is kept is only what was
/// asked for, a few megabytes for an update.
/// </remarks>
internal sealed class RangeStream : Stream
{
    /// <summary>
    /// How much of the end is fetched up front. The directory of a release is 40KB for
    /// 521 files; one request for it and the end record beats ZipArchive's walk backwards
    /// through it, which would be a request per step.
    /// </summary>
    private const int Tail = 64 * 1024;

    /// <summary>What a read nobody said anything about fetches.</summary>
    private const int Window = 64 * 1024;

    private readonly HttpClient _client;
    private readonly Uri _uri;
    private readonly List<(long Start, byte[] Bytes)> _held = [];
    private long _position;
    private long? _expected;

    private RangeStream(HttpClient client, Uri uri, long length)
    {
        _client = client;
        _uri = uri;
        Length = length;
    }

    /// <summary>Bytes that have crossed the network so far.</summary>
    public long Fetched { get; private set; }

    /// <summary>Ranged requests made so far.</summary>
    public int Requests { get; private set; }

    /// <summary>Called with <see cref="Fetched"/> as the bytes arrive.</summary>
    public Action<long>? Progressed { get; set; }

    public override long Length { get; }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Position
    {
        get => _position;
        set => _position = value;
    }

    /// <summary>Opens <paramref name="uri"/>, which must answer byte ranges.</summary>
    /// <remarks>
    /// One byte is asked for first, because its answer says three things at once: how long
    /// the file is, whether ranges are answered at all — a server that ignores them sends
    /// the whole file with a 200 — and where the redirects end. GitHub sends a release asset
    /// on to a signed address on another host, and asking that host directly saves a round
    /// trip on every request after.
    /// </remarks>
    public static async Task<RangeStream> OpenAsync(HttpClient client, Uri uri, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Range = new RangeHeaderValue(0, 0);

        using var response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        if (response.StatusCode != HttpStatusCode.PartialContent
            || response.Content.Headers.ContentRange?.Length is not { } length)
        {
            throw new NotSupportedException(
                $"{uri.Host} does not answer byte ranges: it sent {(int)response.StatusCode} to a request for one.");
        }

        var stream = new RangeStream(client, response.RequestMessage?.RequestUri ?? uri, length)
        {
            Requests = 1,
            Fetched = 1,
        };

        var tail = (int)Math.Min(Tail, length);
        await stream.FetchAsync(length - tail, tail, token).ConfigureAwait(false);
        return stream;
    }

    /// <summary>
    /// Says how much the next fetch should take, when the caller knows better than a guess.
    /// </summary>
    public void Expect(long bytes) => _expected = bytes;

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var total = 0;
        while (total < buffer.Length && _position < Length)
        {
            var copied = CopyHeld(buffer.Span[total..]);
            if (copied > 0)
            {
                total += copied;
                _position += copied;
                continue;
            }

            var wanted = Math.Max(_expected ?? Window, buffer.Length - total);
            _expected = null;
            await FetchAsync(_position, wanted, cancellationToken).ConfigureAwait(false);
        }

        return total;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <remarks>
    /// <see cref="ZipArchive"/> reads its directory synchronously the first time its
    /// entries are asked for, even when it was opened asynchronously — and nothing else
    /// here reads synchronously. That read is of bytes the tail already holds unless the
    /// directory has outgrown it, and a miss then blocks on the fetch. Everything the fetch
    /// awaits is <c>ConfigureAwait(false)</c>, so blocking on it cannot wait on the thread
    /// it is holding.
    /// </remarks>
    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override long Seek(long offset, SeekOrigin origin) => _position = origin switch
    {
        SeekOrigin.Begin => offset,
        SeekOrigin.Current => _position + offset,
        SeekOrigin.End => Length + offset,
        _ => throw new ArgumentOutOfRangeException(nameof(origin)),
    };

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private int CopyHeld(Span<byte> destination)
    {
        foreach (var (start, bytes) in _held)
        {
            if (_position >= start && _position < start + bytes.Length)
            {
                var from = (int)(_position - start);
                var count = Math.Min(destination.Length, bytes.Length - from);
                bytes.AsSpan(from, count).CopyTo(destination);
                return count;
            }
        }

        return 0;
    }

    private async Task FetchAsync(long start, long count, CancellationToken token)
    {
        // Stopped short of anything already held, so that the last entry before the
        // directory does not fetch the directory a second time.
        var end = Math.Min(Length, start + count);
        foreach (var (heldStart, _) in _held)
        {
            if (heldStart > start && heldStart < end)
            {
                end = heldStart;
            }
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, _uri);
        request.Headers.Range = new RangeHeaderValue(start, end - 1);

        using var response = await _client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        Requests++;

        if (response.StatusCode != HttpStatusCode.PartialContent
            || response.Content.Headers.ContentRange?.From != start)
        {
            throw new InvalidDataException(
                $"Asked for bytes {start}-{end - 1} and was sent {(int)response.StatusCode}"
                    + $" {response.Content.Headers.ContentRange}.");
        }

        var bytes = new byte[end - start];
        var filled = 0;

        await using (var body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
        {
            int read;
            while (filled < bytes.Length
                && (read = await body.ReadAsync(bytes.AsMemory(filled), token).ConfigureAwait(false)) > 0)
            {
                filled += read;
                Fetched += read;
                Progressed?.Invoke(Fetched);
            }
        }

        if (filled != bytes.Length)
        {
            throw new InvalidDataException($"Bytes {start}-{end - 1} stopped after {filled}.");
        }

        _held.Add((start, bytes));
    }
}
