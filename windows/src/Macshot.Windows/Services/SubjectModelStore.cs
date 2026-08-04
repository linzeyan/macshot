#if !OFFLINE
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

using static Macshot.Windows.Services.Localization;

namespace Macshot.Windows.Services;

/// <summary>
/// The segmentation model the local background remover runs, fetched the first time it is
/// asked for and kept beside macshot's settings from then on.
/// </summary>
/// <remarks>
/// <para>
/// Downloaded rather than shipped. The model is 4.4 MB against a zip that is already 69,
/// and most users never press Remove Background — but the deciding reason is that a
/// bundled model is a bundled licence: keeping it out of the package keeps macshot's own
/// distribution answerable for macshot's own code.
/// </para>
/// <para>
/// Compiled out of the offline variant entirely. A build whose reason for existing is that
/// it makes no network calls cannot carry a downloader, so the offline build is left with
/// the Windows AI Foundry backend alone — which is what it had before this existed.
/// </para>
/// </remarks>
internal static class SubjectModelStore
{
    /// <summary>
    /// U²-Net's small variant, from the reference implementation's own release. Apache-2.0,
    /// which is what makes it usable beside GPL code at all; the fuller U²-Net weights are
    /// forty times the size for a mask a screenshot does not need, and the models that beat
    /// both are non-commercial.
    /// </summary>
    private const string ModelUrl = "https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx";

    /// <summary>
    /// The published file's SHA-256, checked on every read rather than only after a
    /// download. A model is a program the user did not write: a truncated download and a
    /// substituted file are the same event as far as anything downstream can tell, and
    /// neither should ever reach the runtime.
    /// </summary>
    private const string ModelHash = "309c8469258dda742793dce0ebea8e6dd393174f89934733ecc8b14c76f4ddd8";

    /// <summary>
    /// One client for the life of the process, as <c>HttpClient</c> wants — a new one per
    /// call leaks a socket per call. Generous timeout: this is a single 4 MB body over
    /// whatever connection the user has, not an API call.
    /// </summary>
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(5) };

    /// <summary>Serializes concurrent first presses, which would otherwise race on the file.</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>
    /// Beside settings and history rather than beside the executable. macshot is unpacked
    /// wherever the user put the zip, which may be read-only and is replaced wholesale by
    /// the next version — a model written there would be downloaded again after every
    /// update, or not at all.
    /// </summary>
    private static string ModelPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "macshot",
        "models",
        "u2netp.onnx");

    /// <summary>Whether the model is already on the machine, so no download is coming.</summary>
    internal static bool IsReady => File.Exists(ModelPath);

    /// <summary>
    /// The model's path, fetching it if this is the first time.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// It could not be fetched or what arrived was not the published file. Both are things
    /// to tell the user rather than log: they pressed a button and are owed an answer.
    /// </exception>
    internal static async Task<string> EnsureAsync(CancellationToken cancellation = default)
    {
        var path = ModelPath;

        await Gate.WaitAsync(cancellation);
        try
        {
            if (File.Exists(path) && await MatchesAsync(path, cancellation))
            {
                return path;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Written beside the target and moved into place, so an interrupted download
            // leaves no file rather than a file that fails its hash on every later press.
            var partial = path + ".part";
            DiagnosticLog.Write("Fetching the background removal model.");

            try
            {
                await using (var body = await Client.GetStreamAsync(ModelUrl, cancellation))
                await using (var file = File.Create(partial))
                {
                    await body.CopyToAsync(file, cancellation);
                }

                if (!await MatchesAsync(partial, cancellation))
                {
                    File.Delete(partial);
                    throw new InvalidOperationException(NotTheExpectedFile);
                }

                File.Move(partial, path, overwrite: true);
                DiagnosticLog.Write("The background removal model is ready.");
                return path;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
            {
                TryDelete(partial);
                throw new InvalidOperationException(CouldNotFetch, exception);
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<bool> MatchesAsync(string path, CancellationToken cancellation)
    {
        try
        {
            await using var file = File.OpenRead(path);
            var digest = await SHA256.HashDataAsync(file, cancellation);
            return Convert.ToHexStringLower(digest) == ModelHash;
        }
        catch (IOException)
        {
            // Unreadable is indistinguishable from wrong for this purpose, and both lead to
            // the same place: fetch it again.
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException exception)
        {
            // A leftover .part costs 4 MB and is overwritten by the next attempt. Not worth
            // replacing the user's real error with this one.
            DiagnosticLog.Write($"Could not clean up a partial model download: {exception.Message}");
        }
    }

    private static string CouldNotFetch =>
        L("The background removal model could not be downloaded. Check the connection and try again.");

    private static string NotTheExpectedFile =>
        L("The background removal model that arrived was not the expected file, so it was discarded.");
}
#endif
