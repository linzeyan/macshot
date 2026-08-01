#if !OFFLINE
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using Macshot.Windows.Services;
using Microsoft.Win32;

namespace Macshot.Windows.Upload;

/// <summary>
/// How the browser gets an authorization code back into a running macshot.
/// </summary>
/// <remarks>
/// <para>
/// macOS has <c>ASWebAuthenticationSession</c>, which opens the browser and hands the
/// redirect straight back to the app. Windows has no equivalent an unpackaged app can
/// use: <c>WebAuthenticationBroker</c> needs a package identity this build does not
/// have, and an embedded WebView is refused by Google outright — signing in through one
/// is what <c>disallowed_useragent</c> exists to stop.
/// </para>
/// <para>
/// What is left is the flow the client id was registered for: the default browser, a
/// custom URL scheme, and the redirect coming back as a second launch of macshot. The
/// scheme is the reversed client id, which is the one Google will accept for it — a
/// loopback redirect would be cleaner on Windows and is refused for this client type.
/// </para>
/// <para>
/// That second launch is the interesting part. macshot is single-instance, so the copy
/// the shell starts to handle the redirect would ordinarily put up "macshot is already
/// running" and exit, losing the code. Instead it is intercepted before the instance
/// lock is claimed, written down a named pipe to the copy that is waiting, and ends
/// silently.
/// </para>
/// <para>
/// The registration is per-user, written when the user presses Sign In and removed when
/// they sign out — a screenshot tool has no business owning a URL scheme it is not
/// using.
/// </para>
/// <para>
/// None of this has been through a browser on real hardware yet.
/// </para>
/// </remarks>
internal static class GoogleOAuthRedirect
{
    /// <summary>
    /// The public client id both products use. No secret: there is no way to keep one in
    /// a program the user has a copy of, which is what PKCE is for.
    /// </summary>
    public const string ClientId = "92758256085-8gkpg2b9to7bu7to0vgh9c7af755hp5d.apps.googleusercontent.com";

    /// <summary>The scheme Google will redirect to: the client id, backwards.</summary>
    public static string Scheme { get; } = string.Join('.', ClientId.Split('.').Reverse());

    /// <summary>The full redirect, which has to match the request and the exchange exactly.</summary>
    public static string RedirectUri { get; } = $"{Scheme}:/oauthredirect";

    private static string PipeName => $"{typeof(GoogleOAuthRedirect).Assembly.GetName().Name}.oauth";

    /// <summary>
    /// Whether this process was started by the shell to deliver a redirect, and if so,
    /// hands it to the macshot that is waiting.
    /// </summary>
    /// <remarks>
    /// Called before the instance lock is claimed, because this process is not trying to
    /// be macshot — it is a messenger, and it exits either way.
    /// </remarks>
    public static bool ForwardIfRedirect(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var redirect = arguments.FirstOrDefault(
            argument => argument.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase));

        if (redirect is null)
        {
            return false;
        }

        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);

            // Two seconds: the copy that opened the browser has been listening since
            // before the browser was opened, so either it is there now or it is gone.
            pipe.Connect(2000);
            var bytes = Encoding.UTF8.GetBytes(redirect);
            pipe.Write(bytes, 0, bytes.Length);
            pipe.Flush();
        }
        catch (Exception error) when (error is IOException or TimeoutException or UnauthorizedAccessException)
        {
            // Nothing is listening: macshot was quit while the browser was open. Silence
            // is right — the sign-in the user started no longer exists to report to.
            DiagnosticLog.Write("A Google sign-in redirect arrived with no macshot waiting for it.");
        }

        return true;
    }

    /// <summary>
    /// Opens <paramref name="authorizationUrl"/> in the browser and waits for the
    /// redirect to come back, giving the whole callback URL or null if it never does.
    /// </summary>
    /// <remarks>
    /// The listener is started before the browser, so a user who is already signed in to
    /// Google — and is therefore redirected in under a second — cannot outrun it.
    /// </remarks>
    public static async Task<string?> AwaitRedirectAsync(
        string authorizationUrl,
        CancellationToken cancellationToken)
    {
        Register();

        try
        {
            using var server = new NamedPipeServerStream(
                PipeName,
                PipeDirection.In,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            var waiting = server.WaitForConnectionAsync(cancellationToken);

            Process.Start(new ProcessStartInfo(authorizationUrl) { UseShellExecute = true })?.Dispose();

            await waiting.ConfigureAwait(false);

            using var reader = new StreamReader(server, Encoding.UTF8);
            var redirect = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(redirect) ? null : redirect.Trim();
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>Reads the authorization code out of the callback URL.</summary>
    public static string? CodeFrom(string? redirect)
    {
        if (string.IsNullOrWhiteSpace(redirect)
            || !Uri.TryCreate(redirect, UriKind.Absolute, out var uri))
        {
            return null;
        }

        // The query, wherever it sits: the redirect is scheme:/oauthredirect?code=…,
        // which has an authority-less path, and Uri keeps the query separate anyway.
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = pair.IndexOf('=', StringComparison.Ordinal);
            if (split > 0 && pair[..split] == "code")
            {
                return Uri.UnescapeDataString(pair[(split + 1)..]);
            }
        }

        return null;
    }

    /// <summary>Claims the scheme for this executable, for this user only.</summary>
    public static void Register()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Scheme}", writable: true);
            if (key is null)
            {
                return;
            }

            key.SetValue(string.Empty, "URL:macshot Google sign-in", RegistryValueKind.String);

            // The marker that makes the shell treat this as a protocol rather than as a
            // file association. Its value is meaningless and its presence is not.
            key.SetValue("URL Protocol", string.Empty, RegistryValueKind.String);

            using var command = key.CreateSubKey(@"shell\open\command", writable: true);

            // Quoted: the path leads through Program Files often enough that an unquoted
            // one would be read as a command and its first space.
            command?.SetValue(string.Empty, $"\"{Environment.ProcessPath}\" \"%1\"", RegistryValueKind.String);
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Group policy can lock the key. The sign-in will then fail at the redirect
            // rather than here, which is the only place it can be reported from anyway.
            DiagnosticLog.Write("Windows refused the Google sign-in URL scheme registration.");
        }
    }

    /// <summary>Gives the scheme back, which sign-out does.</summary>
    public static void Unregister()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{Scheme}", throwOnMissingSubKey: false);
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }
    }
}
#endif
