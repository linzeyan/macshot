using Macshot.Windows.Core.Upload;

namespace Macshot.Windows.Core.Tests.Upload;

/// <summary>
/// Reading what the three destinations answer with, including when they refuse.
/// </summary>
/// <remarks>
/// Every failure here has to end as a sentence in the toast. An upload is an extra
/// offered on top of a capture the user already has, so a thrown exception over a
/// finished screenshot would lose the screenshot to a failure of the extra.
/// </remarks>
[TestClass]
public sealed class UploadResponseTests
{
    [TestMethod]
    public void Imgbb_ReadsTheLinkAndTheOneThatTakesItDown()
    {
        var outcome = ImgbbResponse.Read(
            """
            {"data":{"url":"https://i.ibb.co/abc/shot.png",
             "delete_url":"https://ibb.co/abc/def"},"success":true,"status":200}
            """);

        Assert.IsTrue(outcome.Succeeded);
        Assert.AreEqual("https://i.ibb.co/abc/shot.png", outcome.Link);
        Assert.AreEqual("https://ibb.co/abc/def", outcome.DeleteLink);
    }

    [TestMethod]
    public void Imgbb_RefusesASuccessWithNoLinkInIt()
    {
        // success:true with no url is not a link anyone can be given, and reporting it as
        // one would put an empty string on the clipboard as if it had worked.
        var outcome = ImgbbResponse.Read("""{"success":true,"data":{}}""");

        Assert.IsFalse(outcome.Succeeded);
    }

    [TestMethod]
    public void Imgbb_PrefersTheServiceOwnSentenceToTheStatusCode()
    {
        var outcome = ImgbbResponse.Read(
            """{"status_code":400,"error":{"message":"Invalid API key","code":100},"success":false}""");

        Assert.AreEqual("Invalid API key", outcome.Failure);
    }

    [TestMethod]
    public void Imgbb_FallsBackToTheStatusCodeWhenThereIsNoSentence()
    {
        var outcome = ImgbbResponse.Read("""{"status_code":429,"success":false}""");

        Assert.AreEqual("API error (status 429)", outcome.Failure);
    }

    [TestMethod]
    public void Imgbb_SaysSomethingRatherThanThrowingOnAPageOfHtml()
    {
        // Which is what a rate-limited or blocked caller is answered with, so it is an
        // ordinary way for imgbb to say no rather than an exceptional one.
        var outcome = ImgbbResponse.Read("<html><body>429 Too Many Requests</body></html>");

        Assert.IsFalse(outcome.Succeeded);
        Assert.AreEqual("imgbb returned something unreadable.", outcome.Failure);
    }

    [TestMethod]
    public void Imgbb_SaysSomethingRatherThanThrowingOnAnEmptyBody()
    {
        Assert.AreEqual("imgbb returned nothing.", ImgbbResponse.Read(null).Failure);
        Assert.AreEqual("imgbb returned nothing.", ImgbbResponse.Read("   ").Failure);
    }

    [TestMethod]
    public void Drive_ReadsAnAccessTokenAndItsLifetime()
    {
        var token = GoogleDriveResponse.ReadToken(
            """{"access_token":"ya29.abc","refresh_token":"1//xyz","expires_in":3599}""");

        Assert.IsNotNull(token);
        Assert.AreEqual("ya29.abc", token.AccessToken);
        Assert.AreEqual("1//xyz", token.RefreshToken);
        Assert.AreEqual(3599, token.ExpiresInSeconds);
    }

    [TestMethod]
    public void Drive_AcceptsARefreshThatReissuesNoRefreshToken()
    {
        // Which is every refresh. Treating the missing token as a failure would sign the
        // user out an hour after they signed in.
        var token = GoogleDriveResponse.ReadToken("""{"access_token":"ya29.def","expires_in":3600}""");

        Assert.IsNotNull(token);
        Assert.IsNull(token.RefreshToken);
    }

    [TestMethod]
    public void Drive_GivesATokenWithNoStatedLifetimeTheDocumentedOne()
    {
        var token = GoogleDriveResponse.ReadToken("""{"access_token":"ya29.ghi"}""");

        Assert.IsNotNull(token);
        Assert.AreEqual(GoogleDriveResponse.DefaultExpirySeconds, token.ExpiresInSeconds);
    }

    [TestMethod]
    public void Drive_ReadsNoTokenOutOfAnError()
    {
        Assert.IsNull(GoogleDriveResponse.ReadToken("""{"error":"invalid_grant"}"""));
        Assert.IsNull(GoogleDriveResponse.ReadToken("not json at all"));
        Assert.IsNull(GoogleDriveResponse.ReadToken(null));
    }

    [TestMethod]
    public void Drive_FindsTheFolderInASearchThatFoundOne()
    {
        var id = GoogleDriveResponse.ReadFolderId("""{"files":[{"id":"folder-1"}]}""", 200, out var failure);

        Assert.AreEqual("folder-1", id);
        Assert.IsNull(failure);
    }

    [TestMethod]
    public void Drive_TellsAnEmptySearchApartFromAFailedOne()
    {
        // Creating a second folder because a request failed is how a Drive ends up with
        // five of them, so "found nothing" and "could not look" must not read alike.
        var empty = GoogleDriveResponse.ReadFolderId("""{"files":[]}""", 200, out var noFailure);
        Assert.IsNull(empty);
        Assert.IsNull(noFailure);

        var refused = GoogleDriveResponse.ReadFolderId(
            """{"error":{"message":"Invalid Credentials"}}""",
            401,
            out var failure);
        Assert.IsNull(refused);
        Assert.AreEqual("Folder search: Invalid Credentials (HTTP 401)", failure);
    }

    [TestMethod]
    public void Drive_ReportsAnUnreadableSearchWithTheStatusItCameWith()
    {
        var id = GoogleDriveResponse.ReadFolderId("<html>502</html>", 502, out var failure);

        Assert.IsNull(id);
        Assert.AreEqual("Folder search: invalid response (HTTP 502)", failure);
    }

    [TestMethod]
    public void Drive_ReadsTheIdOfAFolderItHasJustMade()
    {
        var id = GoogleDriveResponse.ReadCreatedFolderId("""{"id":"folder-2"}""", 200, out var failure);

        Assert.AreEqual("folder-2", id);
        Assert.IsNull(failure);
    }

    [TestMethod]
    public void Drive_ReportsACreationThatCameBackWithoutOne()
    {
        var id = GoogleDriveResponse.ReadCreatedFolderId("""{"kind":"drive#file"}""", 200, out var failure);

        Assert.IsNull(id);
        Assert.AreEqual("Create folder: missing folder ID in response (HTTP 200)", failure);
    }

    [TestMethod]
    public void Drive_TurnsAnUploadedFileIntoALinkThatOpensIt()
    {
        var outcome = GoogleDriveResponse.ReadUpload("""{"id":"file-9","name":"shot.png"}""", 200);

        Assert.IsTrue(outcome.Succeeded);
        Assert.AreEqual("https://drive.google.com/file/d/file-9/view", outcome.Link);

        // Nothing to hand back: a Drive file is deleted where it lives, not through a URL
        // anyone holding it could use.
        Assert.AreEqual(string.Empty, outcome.DeleteLink);
    }

    [TestMethod]
    public void Drive_ReportsAnUploadRefusedWithATwoHundred()
    {
        // Google reports failure as a 200 with an error object at least as often as it
        // reports it as a status code.
        var outcome = GoogleDriveResponse.ReadUpload(
            """{"error":{"message":"The user has exceeded their Drive storage quota."}}""",
            200);

        Assert.IsFalse(outcome.Succeeded);
        Assert.AreEqual(
            "Upload: The user has exceeded their Drive storage quota. (HTTP 200)",
            outcome.Failure);
    }

    [TestMethod]
    public void Drive_ReadsTheSignedInAddress()
    {
        Assert.AreEqual("someone@example.com", GoogleDriveResponse.ReadEmail("""{"email":"someone@example.com"}"""));
        Assert.IsNull(GoogleDriveResponse.ReadEmail("""{"id":"1"}"""));
        Assert.IsNull(GoogleDriveResponse.ReadEmail(null));
    }

    [TestMethod]
    public void Pkce_HashesTheVerifierTheWayTheSpecificationSays()
    {
        // RFC 7636's own worked example, appendix B.
        Assert.AreEqual(
            "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            PkceChallenge.ChallengeFor("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
    }

    [TestMethod]
    public void Pkce_MakesAFreshPairEveryTimeAndKeepsItUrlSafe()
    {
        var first = PkceChallenge.Create();
        var second = PkceChallenge.Create();

        Assert.AreNotEqual(first.Verifier, second.Verifier);
        Assert.AreEqual(PkceChallenge.ChallengeFor(first.Verifier), first.Challenge);

        // Base64 in the URL alphabet, unpadded — anything else is rejected by the
        // authorization endpoint as a malformed challenge.
        foreach (var text in new[] { first.Verifier, first.Challenge })
        {
            Assert.IsFalse(text.Contains('+', StringComparison.Ordinal));
            Assert.IsFalse(text.Contains('/', StringComparison.Ordinal));
            Assert.IsFalse(text.Contains('=', StringComparison.Ordinal));
        }
    }
}
