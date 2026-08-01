namespace Macshot.Windows.Core.Upload;

/// <summary>
/// What an upload produced: a link, or the sentence explaining why there is none.
/// </summary>
/// <param name="Link">Where the file now is, and what is put on the clipboard.</param>
/// <param name="DeleteLink">
/// The link that takes it down again, which only imgbb hands back. Empty for the two
/// destinations that are the user's own storage: a Drive file and a bucket object are
/// deleted where they live, not through a URL anyone holding it could use.
/// </param>
/// <param name="Failure">
/// Why it did not happen, in a sentence the toast can show. Null on success.
/// </param>
public sealed record UploadOutcome(string? Link, string DeleteLink, string? Failure)
{
    public bool Succeeded => Link is not null;

    public static UploadOutcome Uploaded(string link, string deleteLink = "") =>
        new(link, deleteLink, null);

    public static UploadOutcome Failed(string reason) => new(null, string.Empty, reason);
}

/// <summary>
/// One upload that happened, kept so it can be listed and taken down again.
/// </summary>
/// <remarks>
/// macshot's <c>imgbbUploads</c> array. Only imgbb fills it, because only imgbb gives
/// out a delete link — the history is there to offer that link, not to be a log.
/// </remarks>
/// <param name="Link">The public link that was copied to the clipboard.</param>
/// <param name="DeleteLink">The page that removes it.</param>
public sealed record UploadHistoryEntry(string Link, string DeleteLink);
