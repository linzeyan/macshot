using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using Macshot.Windows.Core.Output;

namespace Macshot.Windows.Core.Tests.Output;

[TestClass]
public sealed class UpdateDeltaTests
{
    private static readonly Uri Release = new("https://example.test/macshot-win-x64.zip");

    private string _folder = string.Empty;

    private string Installed => Path.Combine(_folder, "installed");

    private string Payload => Path.Combine(_folder, "payload");

    [TestInitialize]
    public void Initialize()
    {
        _folder = Path.Combine(Path.GetTempPath(), $"macshot-delta-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Installed);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    /// <summary>
    /// The whole point: a release is mostly a runtime that has not changed, and a user on a
    /// slow line was downloading all of it to replace a handful of files. The folder has to
    /// come out exactly as the release is — every file, the unchanged ones taken from the
    /// installation — while the network carries little more than what changed.
    /// </summary>
    [TestMethod]
    public async Task AnUpdateDownloadsOnlyTheFilesThatChanged()
    {
        var runtime = Noise(1, 1 << 20);
        var framework = Noise(2, 1 << 20);
        var resources = Noise(3, 512 << 10);
        var app = Noise(4, 200 << 10);
        var added = Noise(5, 50 << 10);

        var zip = Zip(
            ("coreclr.dll", runtime),
            ("runtime/System.Private.CoreLib.dll", framework),
            ("el-GR/Microsoft.ui.xaml.dll.mui", resources),
            ("Macshot.Windows.dll", app),
            ("macshot_avif.dll", added),
            ("Assets/", null));

        Install("coreclr.dll", runtime);
        Install("runtime/System.Private.CoreLib.dll", framework);
        Install("el-GR/Microsoft.ui.xaml.dll.mui", resources);
        Install("Macshot.Windows.dll", Noise(6, 150 << 10));

        var server = new RangeServer(zip);
        var result = await Stage(server);

        Assert.AreEqual(2, result.Fetched);
        Assert.AreEqual(3, result.Reused);
        AssertPayload("coreclr.dll", runtime);
        AssertPayload("runtime/System.Private.CoreLib.dll", framework);
        AssertPayload("el-GR/Microsoft.ui.xaml.dll.mui", resources);
        AssertPayload("Macshot.Windows.dll", app);
        AssertPayload("macshot_avif.dll", added);
        Assert.IsTrue(Directory.Exists(Path.Combine(Payload, "Assets")));

        // The two changed files and the directory at the end, and not the 2.5MB beside them.
        Assert.IsTrue(
            server.Served < 400 << 10,
            $"{server.Served} bytes of a {zip.Length}-byte release crossed the network");
        Assert.AreEqual(server.Served, result.FetchedBytes);
    }

    /// <summary>
    /// A file the same size as the release's but not the same bytes — damaged on disk, or
    /// patched by hand — must be replaced, not carried into the new version. Size alone
    /// would have kept it; the CRC is what catches it.
    /// </summary>
    [TestMethod]
    public async Task AnInstalledFileWithTheRightSizeButOtherBytesIsDownloadedAgain()
    {
        var release = Noise(7, 64 << 10);
        var altered = (byte[])release.Clone();
        altered[1000] ^= 0xFF;

        Install("Macshot.Windows.Core.dll", altered);

        var result = await Stage(new RangeServer(Zip(("Macshot.Windows.Core.dll", release))));

        Assert.AreEqual(1, result.Fetched);
        AssertPayload("Macshot.Windows.Core.dll", release);
    }

    /// <summary>
    /// A server or proxy that ignores ranges answers with the whole file. That has to be
    /// recognised before anything is staged, because the caller's answer to it is to
    /// download the release the ordinary way — and the only way to know is the first reply.
    /// </summary>
    [TestMethod]
    public async Task AServerThatIgnoresRangesIsRefusedBeforeAnythingIsStaged()
    {
        var server = new RangeServer(Zip(("Macshot.Windows.dll", Noise(8, 1024)))) { IgnoresRanges = true };

        await Assert.ThrowsExceptionAsync<NotSupportedException>(() => Stage(server));

        Assert.AreEqual(1, server.Requests);
        Assert.AreEqual(0, Directory.GetFiles(Payload, "*", SearchOption.AllDirectories).Length);
    }

    /// <summary>
    /// A range answered with the wrong bytes would otherwise be installed and run. Stored
    /// rather than deflated, so that nothing but the CRC check stands between the damage
    /// and the payload.
    /// </summary>
    [TestMethod]
    public async Task AFileThatArrivesDamagedFailsTheUpdate()
    {
        var release = Noise(9, 100 << 10);
        var marker = release.AsSpan(50_000, 16).ToArray();

        // Placed ahead of a large file, so the directory fetched from the end does not
        // carry it and the damaged range is the one fetched for it alone.
        var zip = Zip(
            ("Macshot.Windows.dll", release, CompressionLevel.NoCompression),
            ("coreclr.dll", Noise(10, 1 << 20), CompressionLevel.Optimal));
        Install("coreclr.dll", Noise(10, 1 << 20));

        var server = new RangeServer(zip)
        {
            Tamper = slice =>
            {
                var at = slice.AsSpan().IndexOf(marker);
                if (at >= 0)
                {
                    slice[at] ^= 0xFF;
                }
            },
        };

        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Stage(server));
    }

    /// <summary>
    /// Entry names come off the network and each file is written by hand here, so the
    /// check <see cref="ZipFile.ExtractToDirectory(string, string)"/> would have made is
    /// this code's to make. A name that climbs out of the payload must write nothing.
    /// </summary>
    [TestMethod]
    public async Task AnEntryNamedOutsideThePayloadWritesNothing()
    {
        var zip = Zip(("../outside.dll", Noise(11, 1024)));

        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Stage(new RangeServer(zip)));

        Assert.IsFalse(File.Exists(Path.Combine(_folder, "outside.dll")));
    }

    private Task<UpdateDeltaResult> Stage(RangeServer server) =>
        UpdateDelta.StageAsync(new HttpClient(server), Release, Installed, Payload, null, CancellationToken.None);

    private void Install(string name, byte[] content)
    {
        var path = Path.Combine(Installed, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    private void AssertPayload(string name, byte[] expected) =>
        CollectionAssert.AreEqual(expected, File.ReadAllBytes(Path.Combine(Payload, name)), name);

    /// <summary>Incompressible, so a file's size in the zip is its size on disk.</summary>
    private static byte[] Noise(int seed, int length)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static byte[] Zip(params (string Name, byte[]? Content)[] files) =>
        Zip([.. files.Select(file => (file.Name, file.Content, CompressionLevel.Optimal))]);

    private static byte[] Zip(params (string Name, byte[]? Content, CompressionLevel Level)[] files)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content, level) in files)
            {
                var entry = zip.CreateEntry(name, level);
                if (content is not null)
                {
                    using var stream = entry.Open();
                    stream.Write(content);
                }
            }
        }

        return memory.ToArray();
    }

    /// <summary>A release host that answers byte ranges, and counts what it sent.</summary>
    private sealed class RangeServer(byte[] file) : HttpMessageHandler
    {
        public long Served { get; private set; }

        public int Requests { get; private set; }

        public bool IgnoresRanges { get; init; }

        public Action<byte[]>? Tamper { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;

            if (IgnoresRanges || request.Headers.Range?.Ranges.Single() is not { From: { } from, To: { } to })
            {
                Served += file.Length;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(file),
                    RequestMessage = request,
                });
            }

            var last = Math.Min(to, file.Length - 1);
            var slice = file[(int)from..(int)(last + 1)];
            Tamper?.Invoke(slice);
            Served += slice.Length;

            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(slice),
                RequestMessage = request,
            };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, last, file.Length);
            return Task.FromResult(response);
        }
    }
}
