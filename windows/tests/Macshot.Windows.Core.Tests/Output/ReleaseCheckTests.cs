using Macshot.Windows.Core.Output;

namespace Macshot.Windows.Core.Tests.Output;

/// <summary>
/// Which published release this build is offered, and which it is not.
/// </summary>
/// <remarks>
/// The whole point of testing this is that the alternative is publishing a release and
/// finding out who was offered it. Two of these are the ones that would go wrong quietly:
/// a Windows user sent a .dmg, and an offline user sent the ordinary build.
/// </remarks>
[TestClass]
public sealed class ReleaseCheckTests
{
    private const string Page = "https://github.com/linzeyan/macshot/releases/tag/v3.9.0";

    [TestMethod]
    public void AVersionIsReadWithOrWithoutItsTagPrefix()
    {
        Assert.AreEqual(new ReleaseVersion(3, 8, 0, string.Empty), ReleaseVersion.TryParse("v3.8.0"));
        Assert.AreEqual(new ReleaseVersion(3, 8, 0, string.Empty), ReleaseVersion.TryParse("3.8.0"));
        Assert.AreEqual(new ReleaseVersion(3, 8, 0, string.Empty), ReleaseVersion.TryParse("windows-v3.8.0"));
        Assert.AreEqual(new ReleaseVersion(4, 0, 0, string.Empty), ReleaseVersion.TryParse("v4"));
        Assert.AreEqual(new ReleaseVersion(3, 8, 0, "beta.3"), ReleaseVersion.TryParse("v3.8.0-beta.3"));
    }

    [TestMethod]
    public void SomethingWithNoNumberInItIsNotAVersion()
    {
        Assert.IsNull(ReleaseVersion.TryParse(null));
        Assert.IsNull(ReleaseVersion.TryParse(""));
        Assert.IsNull(ReleaseVersion.TryParse("latest"));
    }

    /// <summary>
    /// The ordering that stops a beta tester being offered the beta they are running as
    /// an update to itself, and offers them the stable version of it when it ships.
    /// </summary>
    [TestMethod]
    public void APreReleaseRanksBelowTheVersionItLeadsTo()
    {
        var beta = ReleaseVersion.TryParse("3.8.0-beta.3")!.Value;
        var stable = ReleaseVersion.TryParse("3.8.0")!.Value;
        var older = ReleaseVersion.TryParse("3.7.9")!.Value;

        Assert.IsTrue(beta.CompareTo(stable) < 0);
        Assert.IsTrue(beta.CompareTo(older) > 0);
        Assert.IsTrue(stable.CompareTo(older) > 0);
        Assert.AreEqual(0, beta.CompareTo(ReleaseVersion.TryParse("v3.8.0-beta.3")!.Value));
    }

    [TestMethod]
    public void OnlyAWindowsBuildForThisVariantCounts()
    {
        Assert.IsTrue(ReleaseCheck.IsWindowsAsset("Macshot-Windows-3.9.0.msi", offline: false));
        Assert.IsTrue(ReleaseCheck.IsWindowsAsset("macshot-win-x64.zip", offline: false));
        Assert.IsTrue(ReleaseCheck.IsWindowsAsset("Macshot-Windows-Offline.msi", offline: true));

        // The Mac releases in the same repository, which is what this exists to refuse.
        Assert.IsFalse(ReleaseCheck.IsWindowsAsset("MacShot.dmg", offline: false));
        Assert.IsFalse(ReleaseCheck.IsWindowsAsset("MacShot-Offline.dmg", offline: true));

        // Neither variant is offered the other's build.
        Assert.IsFalse(ReleaseCheck.IsWindowsAsset("Macshot-Windows-Offline.msi", offline: false));
        Assert.IsFalse(ReleaseCheck.IsWindowsAsset("Macshot-Windows.msi", offline: true));

        Assert.IsFalse(ReleaseCheck.IsWindowsAsset(null, offline: false));
        Assert.IsFalse(ReleaseCheck.IsWindowsAsset("release-notes.txt", offline: false));
    }

    [TestMethod]
    public void ANewerReleaseWithAWindowsBuildIsOffered()
    {
        var offer = ReleaseCheck.Offer(
            [Release("v3.9.0", ["Macshot-Windows-3.9.0.msi"])],
            "3.8.0",
            beta: false,
            offline: false);

        Assert.IsNotNull(offer);
        Assert.AreEqual("v3.9.0", offer.Value.Tag);
        Assert.AreEqual(Page, offer.Value.PageUrl);
    }

    /// <summary>
    /// The one that matters most today: every release published so far carries a .dmg and
    /// nothing else, so the answer has to be that there is no update — not the newest Mac
    /// release.
    /// </summary>
    [TestMethod]
    public void AReleaseWithNoWindowsBuildIsNotAnUpdate()
    {
        Assert.IsNull(ReleaseCheck.Offer(
            [Release("v3.9.0", ["MacShot.dmg", "MacShot-Offline.dmg"])],
            "3.8.0",
            beta: false,
            offline: false));
    }

    [TestMethod]
    public void TheSameVersionOrAnOlderOneIsNotAnUpdate()
    {
        ReleaseListing[] published =
        [
            Release("v3.8.0", ["Macshot-Windows-3.8.0.msi"]),
            Release("v3.7.0", ["Macshot-Windows-3.7.0.msi"]),
        ];

        Assert.IsNull(ReleaseCheck.Offer(published, "3.8.0", beta: false, offline: false));
    }

    [TestMethod]
    public void APreReleaseIsOfferedOnlyToSomeoneWhoAskedForOne()
    {
        ReleaseListing[] published =
        [
            Release("v3.9.0-beta.1", ["Macshot-Windows-3.9.0-beta.1.msi"], preRelease: true),
        ];

        Assert.IsNull(ReleaseCheck.Offer(published, "3.8.0", beta: false, offline: false));
        Assert.IsNotNull(ReleaseCheck.Offer(published, "3.8.0", beta: true, offline: false));
    }

    /// <summary>
    /// GitHub returns them newest first, but nothing promises that and a repository with
    /// a back-ported patch release breaks it. The newest is chosen rather than the first.
    /// </summary>
    [TestMethod]
    public void TheNewestIsChosenWhateverOrderTheyArrivedIn()
    {
        var offer = ReleaseCheck.Offer(
            [
                Release("v3.9.0", ["Macshot-Windows-3.9.0.msi"]),
                Release("v3.11.0", ["Macshot-Windows-3.11.0.msi"]),
                Release("v3.10.0", ["Macshot-Windows-3.10.0.msi"]),
            ],
            "3.8.0",
            beta: false,
            offline: false);

        Assert.AreEqual("v3.11.0", offer!.Value.Tag);
    }

    /// <summary>
    /// A version this build cannot read about itself offers nothing. Reading it as zero
    /// would offer an update to everybody, every time.
    /// </summary>
    [TestMethod]
    public void AnUnreadableCurrentVersionOffersNothing()
    {
        Assert.IsNull(ReleaseCheck.Offer(
            [Release("v3.9.0", ["Macshot-Windows-3.9.0.msi"])],
            "unknown",
            beta: false,
            offline: false));
    }

    [TestMethod]
    public void TheReleaseListIsReadOutOfGitHubsAnswer()
    {
        const string Json = """
        [
          {
            "tag_name": "v3.9.0",
            "prerelease": false,
            "draft": false,
            "html_url": "https://github.com/linzeyan/macshot/releases/tag/v3.9.0",
            "assets": [ { "name": "Macshot-Windows-3.9.0.msi" }, { "name": "MacShot.dmg" } ]
          },
          {
            "tag_name": "v3.10.0",
            "prerelease": true,
            "draft": true,
            "html_url": "https://github.com/linzeyan/macshot/releases/tag/v3.10.0",
            "assets": []
          }
        ]
        """;

        var releases = ReleaseCheck.Parse(Json);

        // The draft is gone: it is visible only to whoever can publish it.
        Assert.AreEqual(1, releases.Count);
        Assert.AreEqual("v3.9.0", releases[0].Tag);
        Assert.IsFalse(releases[0].PreRelease);
        Assert.AreEqual(2, releases[0].Assets.Count);
        Assert.AreEqual("Macshot-Windows-3.9.0.msi", releases[0].Assets[0]);
    }

    /// <summary>
    /// An answer that is not a release list — a rate-limit object, an error page, a
    /// truncated response — is no releases rather than an exception on the UI thread.
    /// </summary>
    [TestMethod]
    public void AnAnswerThatIsNotAReleaseListIsNoReleases()
    {
        Assert.AreEqual(0, ReleaseCheck.Parse(null).Count);
        Assert.AreEqual(0, ReleaseCheck.Parse("").Count);
        Assert.AreEqual(0, ReleaseCheck.Parse("{\"message\":\"API rate limit exceeded\"}").Count);
        Assert.AreEqual(0, ReleaseCheck.Parse("[{\"tag_name\":").Count);
        Assert.AreEqual(0, ReleaseCheck.Parse("[ { \"prerelease\": false } ]").Count);
    }

    private static ReleaseListing Release(string tag, string[] assets, bool preRelease = false) =>
        new(tag, preRelease, Page, assets);
}
