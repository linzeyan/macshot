using System.IO.Pipes;
using System.Text;
using Macshot.Windows.Core.Capture;
using Microsoft.Win32;

namespace Macshot.Windows.Services;

/// <summary>
/// How a <c>macshot://</c> URL opened by something else reaches macshot.
/// </summary>
/// <remarks>
/// <para>
/// macOS registers the scheme in the bundle's <c>Info.plist</c> and LaunchServices
/// delivers the URL to the running app. Windows has neither half: a scheme belongs to
/// whoever writes it into the registry, and the shell delivers a URL by starting the
/// program again with the URL as its argument.
/// </para>
/// <para>
/// So the second launch is intercepted before the instance lock and written down a named
/// pipe to the macshot already running — the same shape as
/// <see cref="Upload.GoogleOAuthRedirect"/>, for the same reason: without it, a URL sent
/// to a running macshot would put up "macshot is already running" and do nothing else.
/// When there is no macshot to hand it to, the launch keeps the URL and becomes macshot,
/// which is what makes a link work from cold.
/// </para>
/// <para>
/// Both build variants claim the same scheme, as both macOS variants do, so on a machine
/// with both installed the one that registered last is the one a link opens. Registering
/// a second scheme for the offline build would be a link that means something different
/// on the two products.
/// </para>
/// </remarks>
internal sealed class UrlSchemeHost : IDisposable
{
    /// <summary>
    /// How long a launch waits for the running macshot to take the URL.
    /// </summary>
    /// <remarks>
    /// Short, because this is paid in full by every launch that has nobody to hand the
    /// URL to — a link used while macshot is closed, which is the case that then has to
    /// start the whole app. An existing pipe is connected to in well under a millisecond;
    /// the rest of this is slack for a macshot between one connection and the next.
    /// </remarks>
    private const int ConnectMilliseconds = 500;

    private static string PipeName => $"{typeof(UrlSchemeHost).Assembly.GetName().Name}.urlscheme";

    private CancellationTokenSource? _listening;
    private bool _registered;
    private bool _disposed;

    /// <summary>A URL that arrived, on the thread that read it off the pipe.</summary>
    public event EventHandler<UrlSchemeCommand>? CommandReceived;

    /// <summary>
    /// The <c>macshot://</c> URL among <paramref name="arguments"/>, or null when this
    /// launch is not carrying one.
    /// </summary>
    public static string? CommandUrlIn(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments.FirstOrDefault(UrlSchemeCommands.IsCommandUrl);
    }

    /// <summary>
    /// Hands <paramref name="url"/> to the macshot that is already running, answering
    /// whether one took it.
    /// </summary>
    /// <remarks>
    /// False covers both "no macshot is running" and "the one running has the setting
    /// off". The caller cannot tell them apart and does not need to: either way this
    /// process is the only one that can act on the URL.
    /// </remarks>
    public static bool Forward(string url)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipe.Connect(ConnectMilliseconds);

            var bytes = Encoding.UTF8.GetBytes(url);
            pipe.Write(bytes, 0, bytes.Length);
            pipe.Flush();
            return true;
        }
        catch (Exception error) when (error is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Claims or gives up the scheme and the pipe, according to what the settings say.
    /// </summary>
    /// <remarks>
    /// Called on every save rather than read once, because the alternative is a checkbox
    /// that asks a background app with no window to be restarted before it means
    /// anything.
    /// </remarks>
    public void Apply(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (enabled)
        {
            Register();
            Listen();
        }
        else
        {
            StopListening();
            Unregister();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // The registration is left alone. It names this executable, which is still where
        // macshot is, and taking it away on quit would mean a link only worked while
        // macshot happened to be running — which is the one case that needs no link.
        StopListening();
    }

    /// <summary>
    /// Answers URLs until told to stop, one connection at a time.
    /// </summary>
    /// <remarks>
    /// A fresh server stream per connection rather than one reused: a stream that has
    /// carried a message is finished with, and the messenger has already exited by the
    /// time it is read. One at a time is enough — a URL is a few dozen bytes, and the
    /// client waits <see cref="ConnectMilliseconds"/> for its turn.
    /// </remarks>
    private void Listen()
    {
        if (_listening is not null)
        {
            return;
        }

        var stopping = new CancellationTokenSource();
        _listening = stopping;
        _ = Task.Run(() => AcceptAsync(stopping));
    }

    /// <summary>
    /// Asks the loop to end. It disposes its own source once it has, which is the only
    /// safe moment: cancelling and disposing here would pull the registration out from
    /// under a wait that is still using it.
    /// </summary>
    private void StopListening()
    {
        if (_listening is not { } stopping)
        {
            return;
        }

        _listening = null;
        stopping.Cancel();
    }

    private async Task AcceptAsync(CancellationTokenSource source)
    {
        var stopping = source.Token;

        try
        {
            while (!stopping.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(stopping).ConfigureAwait(false);

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var url = (await reader.ReadToEndAsync(stopping).ConfigureAwait(false)).Trim();

                    if (UrlSchemeCommands.Parse(url) is { } command)
                    {
                        DiagnosticLog.Verbose($"macshot:// asked for {command.Action}");
                        CommandReceived?.Invoke(this, command);
                    }
                    else
                    {
                        // Named unconditionally: a URL macshot does not know is exactly
                        // the report that arrives as "the link does nothing", and the
                        // name in it is the whole diagnosis.
                        DiagnosticLog.Write($"macshot does not know the URL '{url}'.");
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // One messenger's connection went wrong, or the name is still held by
                    // the loop this one replaced — the setting turned off and straight
                    // back on. Neither is worth ending the loop over, but retrying at
                    // once would spin, so the next attempt waits out the same window a
                    // messenger is prepared to wait in.
                    DiagnosticLog.Write($"A macshot:// URL could not be read: {error.Message}");
                    await Task.Delay(ConnectMilliseconds, stopping).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The delay above, cancelled. Ordinary.
        }
        finally
        {
            source.Dispose();
        }
    }

    /// <summary>Claims the scheme for this executable, for this user only.</summary>
    private void Register()
    {
        if (_registered)
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                $@"Software\Classes\{UrlSchemeCommands.Scheme}",
                writable: true);

            if (key is null)
            {
                return;
            }

            key.SetValue(string.Empty, $"URL:{BuildVariant.DisplayName}", RegistryValueKind.String);

            // The marker that makes the shell treat this as a protocol rather than as a
            // file association. Its value is meaningless and its presence is not.
            key.SetValue("URL Protocol", string.Empty, RegistryValueKind.String);

            using var command = key.CreateSubKey(@"shell\open\command", writable: true);

            // Quoted: the path leads through Program Files often enough that an unquoted
            // one would be read as a command and its first space.
            command?.SetValue(
                string.Empty,
                $"\"{Environment.ProcessPath}\" \"%1\"",
                RegistryValueKind.String);

            _registered = true;
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Group policy can lock the key. Written down rather than shown: the user
            // ticked a box for something they intend to use later, and a message box
            // about the registry now is one they cannot act on and did not ask for.
            DiagnosticLog.Write($"Windows refused the macshot:// registration: {error.Message}");
        }
    }

    /// <summary>Gives the scheme back, which turning the setting off does.</summary>
    private void Unregister()
    {
        _registered = false;

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(
                $@"Software\Classes\{UrlSchemeCommands.Scheme}",
                throwOnMissingSubKey: false);
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            DiagnosticLog.Write($"Windows refused to give up the macshot:// registration: {error.Message}");
        }
    }
}
