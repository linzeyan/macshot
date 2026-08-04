using Macshot.Windows.Core.Capture;
using Microsoft.UI.Dispatching;

namespace Macshot.Windows.Services;

/// <summary>
/// Listens to the microphone while a recording is being set up, so the toolbar's mic
/// button can say whether it is hearing anything.
/// </summary>
/// <remarks>
/// <para>
/// The same endpoint the recording itself opens, read the same way, and deliberately not
/// the same instance: the meter runs while the user is deciding and stops before the
/// recording starts, and sharing one stream between the two would tie the sound in the
/// file to a control that is no longer on screen. WASAPI's shared mode allows both, so
/// this costs a second capture stream on the same device for the length of the setup.
/// </para>
/// <para>
/// A microphone that will not open — none plugged in, or Windows privacy settings saying
/// no — leaves the meter at nothing rather than reporting anything. There is no dialog to
/// raise: unlike macOS, a desktop app is refused the microphone by a settings page rather
/// than by a prompt it can trigger, so the honest signal is a switch that is on and a bar
/// that never moves, which is exactly what the meter is for.
/// </para>
/// <para>
/// Compile-checked only, like everything else that touches a sound card here.
/// </para>
/// </remarks>
internal sealed class MicrophoneMeter : IDisposable
{
    /// <summary>
    /// The scratch a tick's worth of sound is read into: fifty milliseconds of stereo, so
    /// one poll normally empties the buffer in a single pass.
    /// </summary>
    private readonly short[] _scratch =
        new short[(int)(AudioPlan.SampleRate * MicrophoneLevel.Interval.TotalSeconds) * AudioPlan.Channels];

    private readonly AudioSampleBuffer _pending = new();
    private readonly MicrophoneLevel _level = new();
    private readonly AudioEndpoint _endpoint;
    private readonly DispatcherQueueTimer _timer;

    private bool _disposed;

    private MicrophoneMeter(AudioEndpoint endpoint, DispatcherQueueTimer timer)
    {
        _endpoint = endpoint;
        _timer = timer;

        _timer.Interval = MicrophoneLevel.Interval;
        _timer.Tick += OnTick;
    }

    /// <summary>Raised on the thread that started the meter, with the level to draw.</summary>
    public event EventHandler<double>? LevelChanged;

    /// <summary>
    /// Opens the default microphone and starts reading it, or answers null when there is
    /// nothing to read or no dispatcher to report on.
    /// </summary>
    /// <remarks>
    /// Null rather than an object that never moves, so the caller has nothing to dispose
    /// and the button is simply left at nothing.
    /// </remarks>
    public static MicrophoneMeter? Start()
    {
        if (DispatcherQueue.GetForCurrentThread()?.CreateTimer() is not { } timer)
        {
            return null;
        }

        if (AudioEndpoint.Open(AudioSource.Microphone) is not { } endpoint)
        {
            return null;
        }

        var meter = new MicrophoneMeter(endpoint, timer);
        endpoint.Start();
        timer.Start();
        return meter;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _endpoint.Dispose();

        // One last reading, so the bar goes with the microphone. A meter left standing at
        // whatever it last heard says the microphone is still open.
        if (_level.Silence())
        {
            LevelChanged?.Invoke(this, 0);
        }
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        if (_disposed)
        {
            return;
        }

        if (_level.Follow(Loudest()))
        {
            LevelChanged?.Invoke(this, _level.Current);
        }
    }

    /// <summary>
    /// The loudest thing the microphone has produced since the last tick.
    /// </summary>
    /// <remarks>
    /// Drained to the end rather than a fixed amount per tick: the endpoint produces
    /// whatever it produces, and a meter that read a fixed slice would fall further behind
    /// the microphone the longer the setup was left open — showing a level from a second
    /// ago, which is worse than showing none.
    /// </remarks>
    private double Loudest()
    {
        _endpoint.Drain(_pending);

        var peak = 0.0;
        while (true)
        {
            var real = _pending.Take(_scratch);
            if (real == 0)
            {
                return peak;
            }

            peak = Math.Max(peak, MicrophoneLevel.PeakOf(_scratch.AsSpan(0, real)));

            if (real < _scratch.Length)
            {
                return peak;
            }
        }
    }
}
