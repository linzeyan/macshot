#if !OFFLINE
using Macshot.Windows.Core.Output;
using Macshot.Windows.Core.Upload;
using Macshot.Windows.Services;
using Windows.ApplicationModel.DataTransfer;

namespace Macshot.Windows.Upload;

/// <summary>
/// Sends a capture where the preferences say, and shows the one panel that reports it.
/// </summary>
/// <remarks>
/// <para>
/// macshot's <c>AppDelegate.showUploadProgress</c>, which is the whole of its upload
/// path: pick the provider, refuse early if it is not set up, hand over the bytes, put
/// the link on the clipboard, and remember it if it came with a way to take it down.
/// </para>
/// <para>
/// One <see cref="HttpClient"/> for every upload the process makes. A client per upload
/// leaks sockets in TIME_WAIT, which on a machine that uploads a recording every few
/// minutes eventually looks like the network failing.
/// </para>
/// <para>
/// The timeouts are macshot's own dedicated upload session: five minutes for a request
/// and ten for the whole transfer. A recording on a slow line takes longer than the
/// hundred seconds <see cref="HttpClient"/> allows by default, and that failure arrives
/// as a cancellation with nothing to say why.
/// </para>
/// </remarks>
internal sealed class UploadService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(10) };

    private readonly SettingsStore _settings;
    private readonly GoogleDriveUploader _drive = new(Client);

    private UploadToastWindow? _toast;

    public UploadService(SettingsStore settings) => _settings = settings;

    /// <summary>Whether the chosen destination could take a recording.</summary>
    public bool TakesVideo => UploadProviders.TakesVideo(_settings.Current.UploadProvider);

    /// <summary>
    /// Whether the chosen destination is ready — signed in, or configured. imgbb always
    /// is: it has a shared key behind it.
    /// </summary>
    public bool IsReady => _settings.Current.UploadProvider switch
    {
        UploadProvider.GoogleDrive => GoogleDriveUploader.IsSignedIn,
        UploadProvider.S3 => _settings.Current.ToS3Settings().IsComplete,
        _ => true,
    };

    /// <summary>Signs in to Google Drive and remembers which account it was.</summary>
    public async Task<bool> SignInToDriveAsync(CancellationToken cancellationToken)
    {
        var account = await _drive.SignInAsync(cancellationToken).ConfigureAwait(true);
        if (account is null)
        {
            return false;
        }

        _settings.Save(_settings.Current with { GoogleDriveAccount = account });
        return true;
    }

    /// <summary>Forgets the Google account, and the URL scheme that went with it.</summary>
    public void SignOutOfDrive()
    {
        GoogleDriveUploader.SignOut();
        _settings.Save(_settings.Current with { GoogleDriveAccount = string.Empty });
    }

    /// <summary>
    /// Uploads a finished capture as a PNG, named the way a saved one would be.
    /// </summary>
    /// <remarks>
    /// PNG whatever the image format setting says, as macshot uploads: the setting is
    /// about what lands in the save folder, and a JPEG artefact in a link someone else
    /// opens cannot be undone by the person who sent it.
    /// </remarks>
    public async Task UploadAsync(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var settings = _settings.Current;
        var png = await ImageDelivery.EncodeAsync(frame, CaptureImageFormat.Png, settings.Quality)
            .ConfigureAwait(true);

        var name = FilenameTemplate.Resolve(settings.FilenameTemplate, DateTimeOffset.Now) + ".png";
        await SendAsync(png, name, "image/png").ConfigureAwait(true);
    }

    /// <summary>Uploads a file that is already written — which is every recording.</summary>
    public async Task UploadFileAsync(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            var toast = Toast();
            toast.ShowUploading(Localization.L("Uploading..."));
            toast.ShowFailure(error.Message);
            return;
        }

        await SendAsync(bytes, Path.GetFileName(path), S3Request.ContentTypeFor(path)).ConfigureAwait(true);
    }

    private async Task SendAsync(byte[] payload, string filename, string contentType)
    {
        var settings = _settings.Current;
        var toast = Toast();
        toast.ShowUploading(Localization.L("Uploading..."));

        // Refused before anything is sent, and with macshot's own two sentences: a
        // provider that was chosen but never set up is the likeliest reason an upload
        // does not happen, and "not signed in" is a different instruction from a failure.
        if (settings.UploadProvider is UploadProvider.GoogleDrive && !GoogleDriveUploader.IsSignedIn)
        {
            toast.ShowFailure(Localization.L("Google Drive not signed in"));
            return;
        }

        if (settings.UploadProvider is UploadProvider.S3 && !settings.ToS3Settings().IsComplete)
        {
            toast.ShowFailure(Localization.L("S3 not configured — check Settings"));
            return;
        }

        if (settings.UploadProvider is UploadProvider.Imgbb && !contentType.StartsWith("image/", StringComparison.Ordinal))
        {
            // Only reachable from a menu entry that should already be dark. Said plainly
            // rather than attempted, because imgbb answers a video with a parse failure.
            toast.ShowFailure(Localization.L("imgbb cannot take videos — choose another provider in Settings"));
            return;
        }

        var progress = new Progress<double>(toast.ShowProgress);

        var outcome = settings.UploadProvider switch
        {
            UploadProvider.GoogleDrive => await _drive
                .UploadAsync(payload, filename, contentType, progress, CancellationToken.None)
                .ConfigureAwait(true),

            UploadProvider.S3 => await S3Uploader
                .UploadAsync(Client, settings.ToS3Settings(), payload, filename, contentType, progress, CancellationToken.None)
                .ConfigureAwait(true),

            _ => await ImgbbUploader
                .UploadAsync(Client, payload, settings.ImgbbApiKey, progress, CancellationToken.None)
                .ConfigureAwait(true),
        };

        if (outcome.Link is not { } link)
        {
            DiagnosticLog.Write($"Upload to {settings.UploadProvider} failed: {outcome.Failure}");
            toast.ShowFailure(outcome.Failure ?? Localization.L("Unknown error"));
            return;
        }

        CopyLink(link);
        Remember(outcome);
        toast.ShowSuccess(link);
    }

    /// <summary>
    /// Keeps an imgbb link and the one that takes it down.
    /// </summary>
    /// <remarks>
    /// Only imgbb fills this. A Drive file and a bucket object are deleted where they
    /// live, so a list of them would be a log rather than something to act on — which is
    /// why macshot's own history section holds imgbb and nothing else.
    /// </remarks>
    private void Remember(UploadOutcome outcome)
    {
        if (outcome.Link is not { } link || outcome.DeleteLink.Length == 0)
        {
            return;
        }

        var settings = _settings.Current;
        _settings.Save(settings with
        {
            ImgbbUploads = [.. settings.ImgbbUploads, new UploadHistoryEntry(link, outcome.DeleteLink)],
        });
    }

    private static void CopyLink(string link)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(link);
            Clipboard.SetContent(package);
            Clipboard.Flush();
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            // Another program holding the clipboard open. The link is still in the toast,
            // where it can be read — which is the reason the toast shows it at all.
            DiagnosticLog.Write($"The upload link could not be put on the clipboard: {error.Message}");
        }
    }

    /// <summary>
    /// The one toast, replacing any that is still up.
    /// </summary>
    /// <remarks>
    /// Two uploads in a row would otherwise stack two panels in the same place, with the
    /// older one on top saying something that is no longer true.
    /// </remarks>
    private UploadToastWindow Toast()
    {
        _toast?.Dismiss();
        _toast = new UploadToastWindow();
        _toast.Closed += (_, _) => _toast = null;
        return _toast;
    }
}
#endif
