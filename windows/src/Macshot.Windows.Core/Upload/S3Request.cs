using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Macshot.Windows.Core.Upload;

/// <summary>Everything needed to put an object in a bucket.</summary>
/// <param name="Endpoint">
/// The service, not the bucket: <c>https://abc123.r2.cloudflarestorage.com</c>. The
/// bucket is a path segment, because path-style addressing is the one form every
/// S3-compatible service accepts — virtual-host style needs a wildcard certificate that
/// a MinIO on a LAN will not have.
/// </param>
/// <param name="Region">"auto" for R2, a real region for AWS. It is signed over, so it has to match.</param>
/// <param name="PublicUrlBase">
/// Where the object can be read from, which is usually not where it was written to. Empty
/// means "use the endpoint URL", which is what macshot does and is honest about: the
/// settings window says it may not be publicly reachable.
/// </param>
/// <param name="PathPrefix">An optional folder within the bucket, with or without its slash.</param>
public sealed record S3Settings(
    string Endpoint,
    string Region,
    string Bucket,
    string AccessKeyId,
    string SecretAccessKey,
    string PublicUrlBase,
    string PathPrefix)
{
    public static S3Settings Empty { get; } = new(string.Empty, "auto", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

    /// <summary>
    /// Whether there is enough here to try. macshot's <c>Config.isValid</c> — the region
    /// and the two optional URL parts are not in it, because a bucket with no region
    /// signs against "auto" and works on R2.
    /// </summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Endpoint)
        && !string.IsNullOrWhiteSpace(Bucket)
        && !string.IsNullOrWhiteSpace(AccessKeyId)
        && !string.IsNullOrWhiteSpace(SecretAccessKey);
}

/// <summary>A signed PUT, ready to be sent by whatever does the sending.</summary>
/// <param name="Url">Where the body goes.</param>
/// <param name="ObjectKey">The key within the bucket, before encoding — for a message about it.</param>
/// <param name="PublicLink">What the user is given afterwards.</param>
/// <param name="Headers">
/// Every header that was signed over except Content-Type, which belongs to the body and
/// is set on it. Sending one of these with a different value than was signed is a 403
/// that says nothing about which header was wrong, so they travel together.
/// </param>
public sealed record S3PutRequest(
    string Url,
    string ObjectKey,
    string PublicLink,
    IReadOnlyList<KeyValuePair<string, string>> Headers);

/// <summary>
/// Builds and signs the one request macshot's S3 uploader makes.
/// </summary>
/// <remarks>
/// <para>
/// AWS Signature V4 by hand, as macshot does it, rather than through the AWS SDK: the
/// SDK is a large dependency for one PUT, and it cannot be pointed at an arbitrary
/// S3-compatible endpoint without most of the same configuration anyway.
/// </para>
/// <para>
/// All of it is in Core and none of it does any I/O, because a signature is exactly the
/// kind of thing that is either right or silently 403 — there is no partial credit and
/// no useful error message from the far end. The tests sign a request with the worked
/// example from Amazon's own documentation, which is the only way to know this is right
/// without a bucket.
/// </para>
/// <para>
/// Not compiled out of the offline build. It reaches nothing on its own: the uploader
/// that sends what this returns is what that build removes.
/// </para>
/// </remarks>
public static class S3Request
{
    private const string Service = "s3";
    private const string Algorithm = "AWS4-HMAC-SHA256";

    /// <summary>
    /// The headers that are signed, in the order the canonical request needs them —
    /// which is alphabetical, and is the order they are written in below.
    /// </summary>
    private static readonly string[] SignedHeaderNames =
        ["content-type", "host", "x-amz-content-sha256", "x-amz-date"];

    /// <summary>
    /// Builds a signed PUT for <paramref name="payload"/>, or explains why it cannot.
    /// </summary>
    /// <param name="when">
    /// Passed in rather than read from the clock so the signature is reproducible in a
    /// test. AWS refuses a request whose date is more than fifteen minutes out, so this
    /// is the caller's "now" in every real use.
    /// </param>
    public static S3PutRequest? Build(
        S3Settings settings,
        string filename,
        string contentType,
        ReadOnlySpan<byte> payload,
        DateTimeOffset when,
        out string? failure)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(filename);
        ArgumentNullException.ThrowIfNull(contentType);

        if (!settings.IsComplete)
        {
            failure = "S3 not configured — check Settings";
            return null;
        }

        if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var endpoint)
            || string.IsNullOrEmpty(endpoint.Host)
            || (endpoint.Scheme != Uri.UriSchemeHttps && endpoint.Scheme != Uri.UriSchemeHttp))
        {
            failure = "Invalid S3 endpoint URL";
            return null;
        }

        var objectKey = ObjectKeyFor(settings.PathPrefix, filename);
        var encodedKey = EncodePath(objectKey);

        // The authority as it will appear in the Host header, port and all. A default
        // port is left off: Uri keeps 443 in IsDefaultPort but not in the string, and a
        // Host header that says :443 signs differently from one that does not.
        var authority = endpoint.IsDefaultPort
            ? endpoint.Host
            : $"{endpoint.Host}:{endpoint.Port}";

        var canonicalUri = $"/{EncodeSegment(settings.Bucket)}/{encodedKey}";
        var url = $"{endpoint.Scheme}://{authority}{canonicalUri}";

        var amzDate = when.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var dateStamp = when.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var payloadHash = Hex(SHA256.HashData(payload));

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["content-type"] = contentType,
            ["host"] = authority,
            ["x-amz-content-sha256"] = payloadHash,
            ["x-amz-date"] = amzDate,
        };

        var canonicalHeaders = new StringBuilder();
        foreach (var name in SignedHeaderNames)
        {
            canonicalHeaders.Append(name).Append(':').Append(values[name].Trim()).Append('\n');
        }

        var signedHeaders = string.Join(';', SignedHeaderNames);
        var canonicalRequest = string.Join(
            '\n',
            "PUT",
            canonicalUri,

            // No query string. The credentials go in the Authorization header rather
            // than in the URL, so there is nothing to canonicalize here.
            string.Empty,
            canonicalHeaders.ToString(),
            signedHeaders,
            payloadHash);

        var credentialScope = $"{dateStamp}/{settings.Region}/{Service}/aws4_request";
        var stringToSign = string.Join(
            '\n',
            Algorithm,
            amzDate,
            credentialScope,
            Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest))));

        var signature = Hex(SigningKey(settings.SecretAccessKey, dateStamp, settings.Region, stringToSign));

        var authorization =
            $"{Algorithm} Credential={settings.AccessKeyId}/{credentialScope}, "
            + $"SignedHeaders={signedHeaders}, Signature={signature}";

        failure = null;
        return new S3PutRequest(
            url,
            objectKey,
            PublicLink(settings.PublicUrlBase, encodedKey, url),
            [
                new KeyValuePair<string, string>("Host", authority),
                new KeyValuePair<string, string>("X-Amz-Date", amzDate),
                new KeyValuePair<string, string>("X-Amz-Content-Sha256", payloadHash),
                new KeyValuePair<string, string>("Authorization", authorization),
            ]);
    }

    /// <summary>
    /// The key an upload of <paramref name="filename"/> lands under.
    /// </summary>
    /// <remarks>
    /// Spaces become underscores, as macshot's do: a space in a key is legal and survives
    /// signing, but it reaches the user as %20 in a link they are about to paste
    /// somewhere that will break it at the space.
    /// </remarks>
    public static string ObjectKeyFor(string pathPrefix, string filename)
    {
        ArgumentNullException.ThrowIfNull(pathPrefix);
        ArgumentNullException.ThrowIfNull(filename);

        var prefix = pathPrefix.Trim();
        if (prefix.Length > 0 && !prefix.EndsWith('/'))
        {
            prefix += "/";
        }

        return prefix + filename.Replace(' ', '_');
    }

    /// <summary>What the file is, by its extension — macshot's own five answers.</summary>
    public static string ContentTypeFor(string filename)
    {
        ArgumentNullException.ThrowIfNull(filename);

        return Path.GetExtension(filename).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            ".webm" => "video/webm",
            _ => "application/octet-stream",
        };
    }

    /// <summary>
    /// Pulls the sentence out of an S3 error body, which is XML whatever the service.
    /// </summary>
    /// <remarks>
    /// Read by hand rather than parsed, exactly as macshot reads it: the body arrives
    /// when something has already gone wrong, and an XML parser that throws on a
    /// truncated body would replace a useful message with a less useful one.
    /// </remarks>
    public static string? ErrorMessage(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return null;
        }

        const string open = "<Message>";
        const string close = "</Message>";
        var start = body.IndexOf(open, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += open.Length;
        var end = body.IndexOf(close, start, StringComparison.Ordinal);
        return end < 0 ? null : body[start..end];
    }

    private static string PublicLink(string publicUrlBase, string encodedKey, string url)
    {
        var basePart = publicUrlBase.Trim();
        if (basePart.Length == 0)
        {
            return url;
        }

        if (!basePart.EndsWith('/'))
        {
            basePart += "/";
        }

        return basePart + encodedKey;
    }

    private static byte[] SigningKey(string secretAccessKey, string dateStamp, string region, string stringToSign)
    {
        var key = HMACSHA256.HashData(Encoding.UTF8.GetBytes("AWS4" + secretAccessKey), Encoding.UTF8.GetBytes(dateStamp));
        key = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(region));
        key = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(Service));
        key = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes("aws4_request"));
        return HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(stringToSign));
    }

    /// <summary>Percent-encodes each segment of a key, leaving the slashes between them.</summary>
    private static string EncodePath(string key) =>
        string.Join('/', key.Split('/').Select(EncodeSegment));

    /// <summary>
    /// Percent-encodes everything outside RFC 3986's unreserved set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stricter than macshot, which encodes with Foundation's <c>urlPathAllowed</c> and so
    /// leaves <c>+ , ; = : @ $ &amp; ' ( ) *</c> alone. For every name either product can
    /// produce — a filename template renders digits, dashes and underscores — the two
    /// agree exactly. Where they differ, this one is what Amazon specifies, and macshot's
    /// would be a 403 nobody could read.
    /// </para>
    /// <para>
    /// The same function encodes the URL that is sent and the canonical URI that is
    /// signed, which is the property that actually matters: a request whose path is
    /// encoded one way and signed another is rejected however correct either half is.
    /// </para>
    /// </remarks>
    private static string EncodeSegment(string segment)
    {
        var builder = new StringBuilder(segment.Length);
        foreach (var b in Encoding.UTF8.GetBytes(segment))
        {
            var c = (char)b;
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~')
            {
                builder.Append(c);
            }
            else
            {
                builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
