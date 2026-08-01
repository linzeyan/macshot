using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Macshot.Windows.Core.Upload;

namespace Macshot.Windows.Core.Tests.Upload;

/// <summary>
/// Addressing and signing the one PUT the S3 uploader makes.
/// </summary>
/// <remarks>
/// A wrong signature comes back as a 403 with no hint about which of the eight inputs
/// was wrong, and a bucket cannot be part of continuous integration, so this is the only
/// place the signing is ever checked.
/// </remarks>
[TestClass]
public sealed class S3RequestTests
{
    private static readonly S3Settings Configured = new(
        "https://abc123.r2.cloudflarestorage.com",
        "auto",
        "shots",
        "AKIAIOSFODNN7EXAMPLE",
        "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
        string.Empty,
        string.Empty);

    private static readonly DateTimeOffset Noon = new(2026, 3, 14, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Build_RefusesSettingsWithNothingInThem()
    {
        var request = S3Request.Build(S3Settings.Empty, "a.png", "image/png", [], Noon, out var failure);

        Assert.IsNull(request);
        Assert.AreEqual("S3 not configured — check Settings", failure);
    }

    [TestMethod]
    public void Build_RefusesAnEndpointThatIsNotAUrl()
    {
        // A bucket name pasted into the endpoint box is the likely mistake, and it has to
        // be reported as the endpoint being wrong rather than as a failed upload.
        var settings = Configured with { Endpoint = "my-bucket" };

        var request = S3Request.Build(settings, "a.png", "image/png", [], Noon, out var failure);

        Assert.IsNull(request);
        Assert.AreEqual("Invalid S3 endpoint URL", failure);
    }

    [TestMethod]
    public void Build_AddressesTheBucketThroughThePathRatherThanTheHost()
    {
        // Path style, because it is the only form every S3-compatible service accepts:
        // virtual-host style needs a wildcard certificate a MinIO on a LAN will not have.
        var request = S3Request.Build(Configured, "shot.png", "image/png", [], Noon, out _);

        Assert.IsNotNull(request);
        Assert.AreEqual("https://abc123.r2.cloudflarestorage.com/shots/shot.png", request.Url);
    }

    [TestMethod]
    public void Build_PutsTheObjectUnderThePrefixWithTheSlashItIsMissing()
    {
        var settings = Configured with { PathPrefix = "screenshots" };

        var request = S3Request.Build(settings, "shot.png", "image/png", [], Noon, out _);

        Assert.IsNotNull(request);
        Assert.AreEqual("screenshots/shot.png", request.ObjectKey);
    }

    [TestMethod]
    public void Build_TurnsSpacesInTheNameIntoUnderscores()
    {
        // A space survives signing and reaches the user as %20 in a link they are about
        // to paste somewhere that will break it at the space.
        var request = S3Request.Build(Configured, "my shot.png", "image/png", [], Noon, out _);

        Assert.IsNotNull(request);
        Assert.AreEqual("my_shot.png", request.ObjectKey);
        Assert.IsFalse(request.Url.Contains("%20", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Build_LinksToThePublicBaseWhenThereIsOne()
    {
        var settings = Configured with { PublicUrlBase = "https://cdn.example.com", PathPrefix = "shots/" };

        var request = S3Request.Build(settings, "a.png", "image/png", [], Noon, out _);

        Assert.IsNotNull(request);
        Assert.AreEqual("https://cdn.example.com/shots/a.png", request.PublicLink);
    }

    [TestMethod]
    public void Build_LinksToTheEndpointWhenThereIsNoPublicBase()
    {
        // Which may not be readable by anyone but the account holder. That is macshot's
        // behaviour and the settings window says so; inventing a link that works would
        // mean inventing a CDN.
        var request = S3Request.Build(Configured, "a.png", "image/png", [], Noon, out _);

        Assert.IsNotNull(request);
        Assert.AreEqual(request.Url, request.PublicLink);
    }

    [TestMethod]
    public void Build_KeepsANonDefaultPortInTheHostHeaderAndTheUrl()
    {
        // A MinIO on a LAN is reached on :9000, and a Host header that disagrees with the
        // one that was signed is a 403 about credentials.
        var settings = Configured with { Endpoint = "http://192.168.1.4:9000" };

        var request = S3Request.Build(settings, "a.png", "image/png", [], Noon, out _);

        Assert.IsNotNull(request);
        Assert.AreEqual("http://192.168.1.4:9000/shots/a.png", request.Url);
        Assert.AreEqual("192.168.1.4:9000", Header(request, "Host"));
    }

    [TestMethod]
    public void Build_LeavesTheDefaultPortOutOfTheHostHeader()
    {
        var request = S3Request.Build(Configured, "a.png", "image/png", [], Noon, out _);

        Assert.IsNotNull(request);
        Assert.AreEqual("abc123.r2.cloudflarestorage.com", Header(request, "Host"));
    }

    [TestMethod]
    public void Build_DatesTheRequestFromTheMomentItIsGiven()
    {
        var request = S3Request.Build(Configured, "a.png", "image/png", [], Noon, out _);

        Assert.IsNotNull(request);
        Assert.AreEqual("20260314T120000Z", Header(request, "X-Amz-Date"));
    }

    [TestMethod]
    public void Build_HashesTheBodyItIsGoingToSend()
    {
        var payload = Encoding.UTF8.GetBytes("not really a png");

        var request = S3Request.Build(Configured, "a.png", "image/png", payload, Noon, out _);

        Assert.IsNotNull(request);
        Assert.AreEqual(
            Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            Header(request, "X-Amz-Content-Sha256"));
    }

    [TestMethod]
    public void Build_SignsWhatAmazonSaysItShouldSign()
    {
        // The expected value is derived here from the specification's own steps rather
        // than copied from a previous run of the code under test: a recorded output would
        // still pass with the header order, the date format or the path encoding wrong.
        // Amazon publishes no worked example whose signed header set matches this one,
        // so a second expression of the algorithm is the closest thing to a vector.
        var payload = Encoding.UTF8.GetBytes("body");

        var request = S3Request.Build(Configured, "a shot.png", "image/png", payload, Noon, out _);

        Assert.IsNotNull(request);
        Assert.AreEqual(
            ExpectedAuthorization(Configured, "shots", "a_shot.png", "image/png", payload, Noon),
            Header(request, "Authorization"));
    }

    [TestMethod]
    public void Build_EncodesTheSamePathItSignsOver()
    {
        // The property that actually matters: a request whose path is encoded one way and
        // signed another is refused however correct either half is on its own.
        var payload = Encoding.UTF8.GetBytes("body");
        var settings = Configured with { PathPrefix = "diagrams+notes/" };

        var request = S3Request.Build(settings, "über shot.png", "image/png", payload, Noon, out _);

        Assert.IsNotNull(request);
        Assert.AreEqual(
            "https://abc123.r2.cloudflarestorage.com/shots/diagrams%2Bnotes/%C3%BCber_shot.png",
            request.Url);
        Assert.AreEqual(
            ExpectedAuthorization(settings, "shots", "diagrams+notes/über_shot.png", "image/png", payload, Noon),
            Header(request, "Authorization"));
    }

    [TestMethod]
    public void ContentTypeFor_NamesTheFiveKindsEitherProductUploads()
    {
        Assert.AreEqual("image/png", S3Request.ContentTypeFor("a.png"));
        Assert.AreEqual("image/gif", S3Request.ContentTypeFor("a.GIF"));
        Assert.AreEqual("video/mp4", S3Request.ContentTypeFor("a.mp4"));
        Assert.AreEqual("video/quicktime", S3Request.ContentTypeFor("a.mov"));
        Assert.AreEqual("video/webm", S3Request.ContentTypeFor("a.webm"));
        Assert.AreEqual("application/octet-stream", S3Request.ContentTypeFor("a.zzz"));
    }

    [TestMethod]
    public void ErrorMessage_ReadsTheSentenceOutOfAnS3Refusal()
    {
        var body = "<?xml version=\"1.0\"?><Error><Code>AccessDenied</Code>"
            + "<Message>Access Denied</Message></Error>";

        Assert.AreEqual("Access Denied", S3Request.ErrorMessage(body));
    }

    [TestMethod]
    public void ErrorMessage_SaysNothingRatherThanGuessingAtABodyItCannotRead()
    {
        // A truncated body arrives when something has already gone wrong; replacing the
        // HTTP status with a guess would lose the only fact there is.
        Assert.IsNull(S3Request.ErrorMessage("<Error><Message>cut off"));
        Assert.IsNull(S3Request.ErrorMessage("<html>502 Bad Gateway</html>"));
        Assert.IsNull(S3Request.ErrorMessage(null));
    }

    private static string Header(S3PutRequest request, string name) =>
        request.Headers.First(header => header.Key == name).Value;

    /// <summary>
    /// Signature Version 4, written out from Amazon's description of it, so that the
    /// assertion above compares two independent readings of the same specification.
    /// </summary>
    private static string ExpectedAuthorization(
        S3Settings settings,
        string bucket,
        string objectKey,
        string contentType,
        byte[] payload,
        DateTimeOffset when)
    {
        var amzDate = when.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var dateStamp = when.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var host = new Uri(settings.Endpoint).IsDefaultPort
            ? new Uri(settings.Endpoint).Host
            : $"{new Uri(settings.Endpoint).Host}:{new Uri(settings.Endpoint).Port}";
        var payloadHash = Hex(SHA256.HashData(payload));

        var path = "/" + string.Join('/', $"{bucket}/{objectKey}".Split('/').Select(Encode));

        var canonicalRequest = string.Join(
            '\n',
            "PUT",
            path,
            string.Empty,
            $"content-type:{contentType}\nhost:{host}\nx-amz-content-sha256:{payloadHash}\nx-amz-date:{amzDate}\n",
            "content-type;host;x-amz-content-sha256;x-amz-date",
            payloadHash);

        var scope = $"{dateStamp}/{settings.Region}/s3/aws4_request";
        var stringToSign = string.Join(
            '\n',
            "AWS4-HMAC-SHA256",
            amzDate,
            scope,
            Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest))));

        var key = Hmac(Encoding.UTF8.GetBytes("AWS4" + settings.SecretAccessKey), dateStamp);
        key = Hmac(key, settings.Region);
        key = Hmac(key, "s3");
        key = Hmac(key, "aws4_request");
        var signature = Hex(Hmac(key, stringToSign));

        return $"AWS4-HMAC-SHA256 Credential={settings.AccessKeyId}/{scope}, "
            + "SignedHeaders=content-type;host;x-amz-content-sha256;x-amz-date, "
            + $"Signature={signature}";

        static byte[] Hmac(byte[] key, string data) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));

        static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

        static string Encode(string segment)
        {
            var builder = new StringBuilder();
            foreach (var b in Encoding.UTF8.GetBytes(segment))
            {
                if (char.IsAsciiLetterOrDigit((char)b) || (char)b is '-' or '.' or '_' or '~')
                {
                    builder.Append((char)b);
                }
                else
                {
                    builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
                }
            }

            return builder.ToString();
        }
    }
}
