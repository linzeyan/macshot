using System.Buffers;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using Macshot.Windows.Core.Output;

namespace Macshot.Windows.Services;

/// <summary>
/// What macshot's Check for Updates... item asks, and what it can offer.
/// </summary>
/// <remarks>
/// <para>
/// macOS hands this to Sparkle, which reads an appcast the release workflow writes and
/// installs what it finds. There is no Sparkle on Windows, so this is that job done by
/// hand: read the releases the project publishes, and — when this installation is one
/// that can replace itself — fetch the right zip for it. The decisions are
/// <see cref="ReleaseCheck"/>'s and tested there; putting the download in place is
/// <see cref="UpdateInstaller"/>'s.
/// </para>
/// <para>
/// In the offline build as well. That variant is about captures not leaving the machine,
/// not about the machine being offline — macOS's own offline build still checks its
/// appcast, and a user who cannot be told about a security fix is worse served than one
/// whose updater talks to GitHub.
/// </para>
/// </remarks>
internal static class UpdateService
{
    /// <summary>
    /// Where the releases are published. The API rather than the page, because the page
    /// is HTML and what is needed from it is the asset list.
    /// </summary>
    /// <remarks>
    /// Thirty is every release this project has ever cut and then some. Asking for one
    /// page rather than following <c>Link</c> headers keeps the check to a single request:
    /// what is being looked for is the newest, and the newest is on the first page.
    /// </remarks>
    private const string ReleasesUrl =
        "https://api.github.com/repos/linzeyan/macshot/releases?per_page=30";

    /// <summary>Where the user is sent when the check itself cannot be made.</summary>
    public const string ReleasesPage = "https://github.com/linzeyan/macshot/releases";

    /// <summary>
    /// How long the check may take before it is given up on. A menu item that has been
    /// pressed and says nothing is one that gets pressed again.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    /// <summary>How much of a download is moved at a time.</summary>
    private const int ChunkBytes = 64 * 1024;

    /// <summary>
    /// One client for the life of the process, as <c>HttpClient</c> wants: a new one per
    /// check holds its socket open for two minutes after it is disposed, which is how a
    /// program that checks on a timer runs out of them.
    /// </summary>
    private static readonly HttpClient Client = CreateClient();

    /// <summary>The running version, as the release tags spell it.</summary>
    /// <remarks>
    /// The informational version rather than the assembly version, because that is the one
    /// that still has the pre-release part in it: an AssemblyVersion is four numbers and
    /// cannot hold "-beta.3", so a beta build comparing itself against the tags would call
    /// itself 3.8.0 and be offered 3.8.0 as an update to 3.8.0. The release workflow sets
    /// both. Trimmed at '+' because the build metadata a source-linked build appends is
    /// not part of the version anybody released.
    /// </remarks>
    public static string CurrentVersion
    {
        get
        {
            var assembly = typeof(UpdateService).Assembly;
            var informational = assembly
                .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                .OfType<AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()?
                .InformationalVersion;

            if (informational is { Length: > 0 })
            {
                var metadata = informational.IndexOf('+', StringComparison.Ordinal);
                return metadata < 0 ? informational : informational[..metadata];
            }

            return assembly.GetName().Version?.ToString(3) ?? string.Empty;
        }
    }

    /// <summary>
    /// The release this build should be offered, or null when there is none.
    /// </summary>
    /// <remarks>
    /// Throws what the request threw. The caller decides whether a failed check is worth
    /// a message box — it is when the user asked for the check, and it is not when a timer
    /// asked for it on a machine that happens to be on a train.
    /// </remarks>
    public static async Task<ReleaseListing?> FindUpdateAsync(
        bool beta,
        CancellationToken token = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(Patience);

        var json = await Client.GetStringAsync(new Uri(ReleasesUrl), deadline.Token)
            .ConfigureAwait(false);

        return ReleaseCheck.Offer(
            ReleaseCheck.Parse(json),
            CurrentVersion,
            beta,
            BuildVariant.IsOffline);
    }

    /// <summary>
    /// Which build of Windows this process is, spelled as the release names spell it.
    /// </summary>
    /// <remarks>
    /// The process rather than the machine. An x64 macshot on an arm64 machine is running
    /// under emulation and must stay x64 — swapping it for the native build mid-update
    /// would be changing which product is installed, not updating it, and this is not the
    /// place that decision gets made.
    /// </remarks>
    public static string Architecture => RuntimeInformation.ProcessArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
        _ => "x64",
    };

    /// <summary>
    /// Fetches <paramref name="asset"/> to <paramref name="path"/>, saying how far it has
    /// got.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No deadline, unlike the check: this is a hundred and fifty megabytes and the only
    /// thing that should end it early is the user or the app quitting.
    /// </para>
    /// <para>
    /// The length is checked at the end. A connection cut partway leaves a zip that is a
    /// valid file and an invalid archive, and the size GitHub published is the cheapest
    /// thing that catches it before the archive is unpacked over a working installation.
    /// </para>
    /// </remarks>
    public static async Task DownloadAsync(
        ReleaseAsset asset,
        string path,
        IProgress<double>? progress,
        CancellationToken token)
    {
        using var response = await Client
            .GetAsync(new Uri(asset.Url), HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var expected = response.Content.Headers.ContentLength ?? asset.Size;

        using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var destination = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            ChunkBytes,
            useAsync: true);

        var buffer = ArrayPool<byte>.Shared.Rent(ChunkBytes);
        try
        {
            long written = 0;
            var lastReported = -1;

            int read;
            while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                written += read;

                if (progress is null || expected <= 0)
                {
                    continue;
                }

                // Whole percent only. A report per chunk is one marshalled call to the UI
                // thread every sixty-four kilobytes, which is thousands of them for a file
                // this size and no more informative for any of it.
                var percent = (int)(written * 100 / expected);
                if (percent != lastReported)
                {
                    lastReported = percent;
                    progress.Report(percent / 100.0);
                }
            }

            if (expected > 0 && written != expected)
            {
                throw new IOException(
                    $"The download stopped early: {written} bytes of {expected}.");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();

        // GitHub refuses a request with no user agent, and names the version it is
        // answering by — which is what makes a rate-limit complaint traceable to a build.
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("macshot-windows", CurrentVersion));

        // The documented way of pinning the answer's shape: without it GitHub is free to
        // change the default representation under a client that has already shipped.
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        return client;
    }
}
