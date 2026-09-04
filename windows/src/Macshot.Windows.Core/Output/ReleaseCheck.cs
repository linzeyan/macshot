using System.Globalization;
using System.Text.Json;

namespace Macshot.Windows.Core.Output;

/// <summary>
/// One file attached to a release.
/// </summary>
/// <param name="Name">
/// Its file name, which is the whole of what decides whether this build may install it.
/// See <see cref="ReleaseCheck.IsWindowsAsset"/>.
/// </param>
/// <param name="Url">Where to fetch it. Empty for a release read before this was parsed.</param>
/// <param name="Size">
/// How many bytes it should turn out to be, or zero when GitHub did not say. Checked after
/// a download: a connection cut halfway leaves a file that unzips to a broken install, and
/// the length is the one thing that catches it without a hash.
/// </param>
public readonly record struct ReleaseAsset(string Name, string Url, long Size);

/// <summary>
/// One published release, reduced to the four things a check needs to know about it.
/// </summary>
/// <param name="Tag">The tag it was cut from, which is where its version comes from.</param>
/// <param name="PreRelease">
/// Whether GitHub marks it as a pre-release. macshot's beta channel: someone who has not
/// opted in must never be offered one.
/// </param>
/// <param name="PageUrl">The release's own page, which is where the user is sent.</param>
/// <param name="Assets">
/// What is attached to it. A release with no Windows build in it is not an update to this
/// product, however new its version is — the Mac releases in the same repository are
/// exactly that, and offering one would send a Windows user a .dmg.
/// </param>
public readonly record struct ReleaseListing(
    string Tag,
    bool PreRelease,
    string PageUrl,
    IReadOnlyList<ReleaseAsset> Assets);

/// <summary>
/// A version that can be compared with another, read out of a tag.
/// </summary>
/// <remarks>
/// Three numbers and an optional pre-release word, which is the shape every tag either
/// product has ever been cut from. Not a full semver implementation: build metadata and
/// dotted pre-release precedence are parts of the standard nothing here has ever used,
/// and code for a case that does not arise is code nothing proves.
/// </remarks>
public readonly record struct ReleaseVersion(int Major, int Minor, int Patch, string PreRelease)
    : IComparable<ReleaseVersion>
{
    /// <summary>
    /// The version <paramref name="text"/> names, or null when it names none.
    /// </summary>
    /// <remarks>
    /// Anything before the first digit is dropped, so <c>v3.8.0</c>, <c>3.8.0</c> and
    /// <c>windows-v3.8.0</c> all read the same. Missing minor and patch are zero, because
    /// a tag of <c>v4</c> means 4.0.0 and refusing to read it would be reporting no
    /// update at all.
    /// </remarks>
    public static ReleaseVersion? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var start = 0;
        while (start < text.Length && !char.IsAsciiDigit(text[start]))
        {
            start++;
        }

        if (start == text.Length)
        {
            return null;
        }

        var body = text[start..];
        var dash = body.IndexOf('-', StringComparison.Ordinal);
        var pre = dash < 0 ? string.Empty : body[(dash + 1)..];
        var numbers = (dash < 0 ? body : body[..dash]).Split('.');

        if (!int.TryParse(numbers[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major))
        {
            return null;
        }

        return new ReleaseVersion(major, Part(numbers, 1), Part(numbers, 2), pre);

        static int Part(string[] parts, int index) =>
            index < parts.Length
                && int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0;
    }

    /// <summary>
    /// Orders two versions, with a pre-release ranking below the release it leads to.
    /// </summary>
    /// <remarks>
    /// 3.8.0-beta.3 is older than 3.8.0 and newer than 3.7.9, which is what stops a beta
    /// tester being offered the beta they are already running as an update the day the
    /// stable version of it ships.
    /// </remarks>
    public int CompareTo(ReleaseVersion other)
    {
        if (Major != other.Major)
        {
            return Major.CompareTo(other.Major);
        }

        if (Minor != other.Minor)
        {
            return Minor.CompareTo(other.Minor);
        }

        if (Patch != other.Patch)
        {
            return Patch.CompareTo(other.Patch);
        }

        if (PreRelease.Length == 0 || other.PreRelease.Length == 0)
        {
            // The one without a suffix is the finished version of the other.
            return other.PreRelease.Length.CompareTo(PreRelease.Length);
        }

        return string.CompareOrdinal(PreRelease, other.PreRelease);
    }
}

/// <summary>
/// Which published release, if any, this build should be offered.
/// </summary>
/// <remarks>
/// <para>
/// macOS asks Sparkle, which reads an appcast the release workflow writes. There is no
/// Sparkle on Windows and no installer for it to install, so this reads the releases the
/// project already publishes and hands the user the page to download from. It is the
/// check without the automatic install — which is the half that needs a distribution
/// format decided, and the half that can wait.
/// </para>
/// <para>
/// Every decision in here is a pure function of the release list so that it can be
/// tested: what counts as a Windows build, what counts as newer, and what a beta opt-in
/// changes are the three things that would otherwise only be found out by publishing a
/// release and seeing who was offered it.
/// </para>
/// </remarks>
public static class ReleaseCheck
{
    /// <summary>
    /// What a downloadable Windows build is called. A release carrying none of these is
    /// one of the Mac releases from the same repository.
    /// </summary>
    private static readonly string[] WindowsExtensions =
        [".msi", ".msix", ".msixbundle", ".exe", ".zip"];

    /// <summary>
    /// The releases <paramref name="json"/> lists, newest first as GitHub returns them.
    /// </summary>
    /// <remarks>
    /// Drafts are dropped: they are visible only to the people who can publish them, and
    /// offering one would send everyone else to a page they cannot open. Anything the
    /// document does not shape as expected is skipped rather than thrown over — one
    /// malformed entry must not cost the user the check.
    /// </remarks>
    public static IReadOnlyList<ReleaseListing> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return [];
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var releases = new List<ReleaseListing>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object
                    || Text(element, "tag_name") is not { Length: > 0 } tag
                    || Flag(element, "draft"))
                {
                    continue;
                }

                var assets = new List<ReleaseAsset>();
                if (element.TryGetProperty("assets", out var attached)
                    && attached.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in attached.EnumerateArray())
                    {
                        if (Text(asset, "name") is { Length: > 0 } name)
                        {
                            assets.Add(new ReleaseAsset(
                                name,
                                Text(asset, "browser_download_url") ?? string.Empty,
                                Number(asset, "size")));
                        }
                    }
                }

                releases.Add(new ReleaseListing(
                    tag,
                    Flag(element, "prerelease"),
                    Text(element, "html_url") ?? string.Empty,
                    assets));
            }

            return releases;
        }

        static string? Text(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        static bool Flag(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

        static long Number(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt64(out var size)
                    ? size
                    : 0;
    }

    /// <summary>
    /// Whether <paramref name="name"/> is a build this variant could install.
    /// </summary>
    /// <remarks>
    /// The variant is part of the answer, not a detail after it. The offline build exists
    /// to be provably without the upload code in it, and offering its user the ordinary
    /// build as an update would undo that silently — so each one recognises only its own,
    /// by the same word in the file name the Mac releases use.
    /// </remarks>
    public static bool IsWindowsAsset(string? name, bool offline)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var known = WindowsExtensions.Any(extension =>
            name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

        if (!known || !name.Contains("win", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return name.Contains("offline", StringComparison.OrdinalIgnoreCase) == offline;
    }

    /// <summary>
    /// The one file from <paramref name="release"/> that this build should download, or
    /// null when it carries none it could install.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A zip and nothing else. The MSIX beside it is an installer Windows runs, not
    /// something macshot can unpack over itself, and it needs a signature that no release
    /// carries yet — offering it here would download a file that cannot be installed.
    /// </para>
    /// <para>
    /// Architecture is part of the answer, and until there was something to download it
    /// was nowhere: <see cref="IsWindowsAsset"/> answers whether a release has anything
    /// for this variant at all, and every release has both architectures, so an arm64
    /// machine and an x64 one have always been offered the same list. An exact match is
    /// preferred and x64 is the fallback, because x64 runs on arm64 Windows under
    /// emulation while the reverse does not run at all — a machine whose own build is
    /// missing from a release is better off with the slow one than with none.
    /// </para>
    /// </remarks>
    /// <param name="architecture">
    /// What the running process is, spelled as the release names spell it: <c>x64</c> or
    /// <c>arm64</c>.
    /// </param>
    public static ReleaseAsset? Download(ReleaseListing release, bool offline, string architecture)
    {
        ReleaseAsset? fallback = null;

        foreach (var asset in release.Assets)
        {
            if (!IsWindowsAsset(asset.Name, offline)
                || !asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                || asset.Url.Length == 0)
            {
                continue;
            }

            if (IsArchitecture(asset.Name, architecture))
            {
                return asset;
            }

            if (IsArchitecture(asset.Name, "x64"))
            {
                fallback = asset;
            }
        }

        return fallback;
    }

    /// <summary>
    /// Whether <paramref name="name"/> names a build for <paramref name="architecture"/>.
    /// </summary>
    /// <remarks>
    /// Bounded on both sides rather than searched for, because "arm64" ends in "64" and a
    /// contains-test for "x64" against <c>macshot-1.0.0-win-arm64.zip</c> is the kind of
    /// near miss that would only be found by an arm64 machine downloading the wrong build.
    /// </remarks>
    private static bool IsArchitecture(string name, string architecture) =>
        name.Contains($"-{architecture}.", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The release to offer, or null when this build is already the newest there is.
    /// </summary>
    /// <param name="releases">What the repository publishes, in any order.</param>
    /// <param name="current">
    /// The running version. A version this cannot read means no update is offered: an
    /// unknown current version compares as nothing, and guessing it as zero would offer
    /// an update to everyone every time.
    /// </param>
    /// <param name="beta">
    /// macshot's <c>betaUpdatesEnabled</c>. Off means pre-releases are not looked at,
    /// even when one is newer than anything else published.
    /// </param>
    /// <param name="offline">Whether this is the offline variant. See <see cref="IsWindowsAsset"/>.</param>
    public static ReleaseListing? Offer(
        IEnumerable<ReleaseListing>? releases,
        string? current,
        bool beta,
        bool offline)
    {
        if (releases is null || ReleaseVersion.TryParse(current) is not { } running)
        {
            return null;
        }

        ReleaseListing? best = null;
        ReleaseVersion bestVersion = default;

        foreach (var release in releases)
        {
            if (release.PreRelease && !beta)
            {
                continue;
            }

            if (ReleaseVersion.TryParse(release.Tag) is not { } version
                || version.CompareTo(running) <= 0)
            {
                continue;
            }

            if (!release.Assets.Any(asset => IsWindowsAsset(asset.Name, offline)))
            {
                continue;
            }

            if (best is null || version.CompareTo(bestVersion) > 0)
            {
                best = release;
                bestVersion = version;
            }
        }

        return best;
    }
}
