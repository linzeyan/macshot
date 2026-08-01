namespace Macshot.Windows.Core.Upload;

/// <summary>Where a capture is sent when the user asks for a link to it.</summary>
/// <remarks>
/// macshot's <c>uploadProvider</c> setting, whose three values are the three services
/// it knows how to talk to. One choice rather than a list of enabled destinations,
/// because the toolbar has one Upload button and it has to know where it is going
/// without asking.
/// </remarks>
public enum UploadProvider
{
    /// <summary>imgbb.com. Images only, and anonymous — the link is public.</summary>
    Imgbb,

    /// <summary>The signed-in user's Google Drive, into a folder called macshot.</summary>
    GoogleDrive,

    /// <summary>Any S3-compatible bucket: AWS, R2, MinIO, Spaces, B2.</summary>
    S3,
}

/// <summary>What each destination can be asked for.</summary>
public static class UploadProviders
{
    /// <summary>
    /// Whether a recording can go there, not just a screenshot.
    /// </summary>
    /// <remarks>
    /// imgbb is an image host and has no video endpoint at all, which is why macshot
    /// names it "imgbb (images only)" in its own settings window and why the video
    /// editor's Upload button is dark while imgbb is the chosen provider.
    /// </remarks>
    public static bool TakesVideo(UploadProvider provider) => provider is not UploadProvider.Imgbb;

    /// <summary>
    /// What the confirmation asks before a capture leaves the machine — macshot's own
    /// three titles, which is what its translations are keyed by.
    /// </summary>
    public static string ConfirmTitle(UploadProvider provider) => provider switch
    {
        UploadProvider.GoogleDrive => "Upload to Google Drive?",
        UploadProvider.S3 => "Upload to S3?",
        _ => "Upload to imgbb.com?",
    };

    /// <summary>How the settings window names each one.</summary>
    public static string Label(UploadProvider provider) => provider switch
    {
        UploadProvider.GoogleDrive => "Google Drive (images + videos)",
        UploadProvider.S3 => "S3-Compatible (images + videos)",
        _ => "imgbb (images only)",
    };
}
