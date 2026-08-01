using System.Security.Cryptography;
using System.Text;

namespace Macshot.Windows.Core.Upload;

/// <summary>
/// The proof-key pair that keeps an authorization code useless to anyone who intercepts it.
/// </summary>
/// <param name="Verifier">Kept in memory, and sent only with the code exchange.</param>
/// <param name="Challenge">Sent with the authorization request, where it is public.</param>
public readonly record struct PkceCodes(string Verifier, string Challenge);

/// <summary>
/// RFC 7636 PKCE, as macshot's <c>GoogleDriveUploader</c> generates it.
/// </summary>
/// <remarks>
/// <para>
/// PKCE rather than a client secret because there is no way to keep a secret in a
/// program the user has a copy of. Both products use the same public client id, and the
/// verifier is what makes an intercepted redirect worthless.
/// </para>
/// <para>
/// In Core so the encoding can be asserted against the specification's own worked
/// example rather than against whatever the code happens to produce. Nothing here talks
/// to Google; the flow that does is compiled out of the offline build.
/// </para>
/// </remarks>
public static class PkceChallenge
{
    /// <summary>
    /// A fresh pair, from 32 bytes of cryptographic randomness — macshot's length.
    /// </summary>
    public static PkceCodes Create()
    {
        var entropy = RandomNumberGenerator.GetBytes(32);
        var verifier = Base64Url(entropy);
        return new PkceCodes(verifier, ChallengeFor(verifier));
    }

    /// <summary>The S256 challenge for a verifier: base64url of its SHA-256, unpadded.</summary>
    public static string ChallengeFor(string verifier)
    {
        ArgumentNullException.ThrowIfNull(verifier);

        return Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));
    }

    /// <summary>
    /// Base64 in the URL alphabet with the padding dropped.
    /// </summary>
    /// <remarks>
    /// Written out rather than using <c>Base64Url.EncodeToString</c>, which arrives in
    /// .NET 9; this project targets .NET 8.
    /// </remarks>
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
}
