using System.Runtime.InteropServices;
using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Services;

/// <summary>
/// Which sound an endpoint carries.
/// </summary>
internal enum AudioSource
{
    /// <summary>What the machine is playing, taken off the speakers' own endpoint.</summary>
    System,

    /// <summary>What the microphone hears.</summary>
    Microphone,
}

/// <summary>
/// Reads sound off one Windows audio endpoint, as 48 kHz stereo 16-bit.
/// </summary>
/// <remarks>
/// <para>
/// WASAPI in shared mode, polled. Two things it does are worth knowing about, because
/// both were decisions rather than defaults.
/// </para>
/// <para>
/// The format is <em>asked for</em> rather than accepted: the endpoint's own mix format
/// is whatever the hardware runs at — 44.1 kHz, mono, 32-bit float, any of them — and
/// <c>AUTOCONVERTPCM</c> makes the audio engine do the conversion. Writing a resampler
/// here would be writing the second-worst resampler on the machine.
/// </para>
/// <para>
/// A loopback endpoint delivers <em>nothing</em> while the machine is silent rather than
/// delivering zeroes. That gap is not filled here: the recorder asks for a sample every
/// twenty milliseconds whatever arrived, and <see cref="AudioSampleBuffer"/> fills what
/// is missing. Filling it here would need a clock of its own and would fight the one the
/// recording already runs on.
/// </para>
/// <para>
/// One thing macshot does that this does not: <c>excludesCurrentProcessAudio</c> keeps
/// the app's own sounds out of the recording. The equivalent is Windows 11's process
/// loopback, which is reached through <c>ActivateAudioInterfaceAsync</c> and a COM
/// completion handler rather than through the endpoint below. It is left out because
/// macshot makes no sound while a recording runs, so the whole of the difference is
/// hypothetical — and it would double the amount of interop here that nothing can test.
/// </para>
/// <para>
/// Everything below is compile-checked only. Nothing in continuous integration has a
/// sound card, so the first real answer about whether a recording has sound in it comes
/// from hardware.
/// </para>
/// </remarks>
internal sealed class AudioEndpoint : IDisposable
{
    private const int SharedMode = 0;
    private const uint StreamFlagsLoopback = 0x00020000;
    private const uint StreamFlagsAutoConvertPcm = 0x80000000;
    private const uint StreamFlagsSrcDefaultQuality = 0x08000000;

    /// <summary>How much the endpoint may hold before it starts overwriting: 200 ms, in 100 ns units.</summary>
    private const long BufferDuration = 2_000_000;

    /// <summary>The packet was not a continuation of the last one, so the gap is real silence.</summary>
    private const uint BufferFlagsSilent = 0x2;

    private readonly IAudioClient _client;
    private readonly IAudioCaptureClient _capture;
    private readonly nint _format;

    private bool _started;
    private bool _disposed;

    private AudioEndpoint(IAudioClient client, IAudioCaptureClient capture, nint format)
    {
        _client = client;
        _capture = capture;
        _format = format;
    }

    /// <summary>
    /// Opens the default endpoint for <paramref name="source"/>, or null when the
    /// machine has none or refuses the format.
    /// </summary>
    /// <remarks>
    /// Null rather than an exception: a machine with no microphone is an ordinary
    /// machine, and a recording that fails outright because nothing is plugged in would
    /// be worse than one without sound.
    /// </remarks>
    public static AudioEndpoint? Open(AudioSource source)
    {
        nint format = 0;
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();

            // A loopback stream is opened on the *render* endpoint — the speakers — and
            // read from as though it were a capture device. eConsole rather than
            // eMultimedia: it is what the user hears.
            var flow = source == AudioSource.System ? 0 : 1;
            if (enumerator.GetDefaultAudioEndpoint(flow, 0, out var device) != 0 || device is null)
            {
                return null;
            }

            var audioClientId = typeof(IAudioClient).GUID;
            if (device.Activate(ref audioClientId, 1 /* CLSCTX_INPROC_SERVER */, 0, out var activated) != 0
                || activated is not IAudioClient client)
            {
                return null;
            }

            format = WaveFormat();
            var flags = StreamFlagsAutoConvertPcm | StreamFlagsSrcDefaultQuality
                | (source == AudioSource.System ? StreamFlagsLoopback : 0);

            if (client.Initialize(SharedMode, flags, BufferDuration, 0, format, 0) != 0)
            {
                return null;
            }

            var captureClientId = typeof(IAudioCaptureClient).GUID;
            if (client.GetService(ref captureClientId, out var service) != 0
                || service is not IAudioCaptureClient capture)
            {
                return null;
            }

            var endpoint = new AudioEndpoint(client, capture, format);
            format = 0;
            return endpoint;
        }
        catch (COMException)
        {
            // An endpoint that cannot be opened is a recording without that source in
            // it, which is the same answer as not having asked for it.
            return null;
        }
        catch (InvalidCastException)
        {
            return null;
        }
        finally
        {
            if (format != 0)
            {
                Marshal.FreeHGlobal(format);
            }
        }
    }

    public void Start()
    {
        if (!_started && !_disposed)
        {
            _started = _client.Start() == 0;
        }
    }

    /// <summary>
    /// Moves everything the endpoint has produced since the last call into
    /// <paramref name="into"/>.
    /// </summary>
    public void Drain(AudioSampleBuffer into)
    {
        ArgumentNullException.ThrowIfNull(into);

        if (!_started || _disposed)
        {
            return;
        }

        try
        {
            while (_capture.GetNextPacketSize(out var frames) == 0 && frames > 0)
            {
                if (_capture.GetBuffer(out var data, out var read, out var flags, out _, out _) != 0)
                {
                    return;
                }

                try
                {
                    if (read == 0)
                    {
                        continue;
                    }

                    // A silent packet's memory is not required to hold anything, so its
                    // length is honoured and its contents are not read.
                    var samples = new short[read * AudioPlan.Channels];
                    if ((flags & BufferFlagsSilent) == 0 && data != 0)
                    {
                        Marshal.Copy(data, samples, 0, samples.Length);
                    }

                    into.Append(samples);
                }
                finally
                {
                    _capture.ReleaseBuffer(read);
                }
            }
        }
        catch (COMException)
        {
            // A device removed mid-recording — a headset unplugged — ends this source
            // rather than the recording.
            _started = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (_started)
            {
                _client.Stop();
            }
        }
        catch (COMException)
        {
        }

        Marshal.FreeHGlobal(_format);
        Marshal.ReleaseComObject(_capture);
        Marshal.ReleaseComObject(_client);
    }

    /// <summary>
    /// The format asked of the endpoint, as an unmanaged <c>WAVEFORMATEX</c>.
    /// </summary>
    /// <remarks>
    /// Plain 16-bit PCM rather than <c>WAVEFORMATEXTENSIBLE</c>: two channels need no
    /// channel mask to be understood, and the extensible form is what a format with more
    /// of them or with float samples would need.
    /// </remarks>
    private static nint WaveFormat()
    {
        const int SizeOfWaveFormatEx = 18;
        const short PcmTag = 1;

        var blockAlign = (short)(AudioPlan.Channels * (AudioPlan.BitsPerSample / 8));
        var format = Marshal.AllocHGlobal(SizeOfWaveFormatEx);

        Marshal.WriteInt16(format, 0, PcmTag);
        Marshal.WriteInt16(format, 2, AudioPlan.Channels);
        Marshal.WriteInt32(format, 4, AudioPlan.SampleRate);
        Marshal.WriteInt32(format, 8, AudioPlan.SampleRate * blockAlign);
        Marshal.WriteInt16(format, 12, blockAlign);
        Marshal.WriteInt16(format, 14, AudioPlan.BitsPerSample);
        Marshal.WriteInt16(format, 16, 0);
        return format;
    }

    /// <summary>
    /// The class object <c>new</c> asks COM for. It has no members because it is only
    /// ever cast to <see cref="IMMDeviceEnumerator"/>.
    /// </summary>
    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator
    {
    }

    // The three interfaces below are declared in the order their methods appear in
    // Mmdeviceapi.h and Audioclient.h. The order *is* the binding — a method declared
    // out of place calls whichever function sits at that slot — so nothing here may be
    // reordered or left out, however unused it is.
    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, uint stateMask, out nint devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? device);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice? device);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(nint client);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(nint client);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(
            ref Guid interfaceId,
            uint context,
            nint activationParameters,
            [MarshalAs(UnmanagedType.IUnknown)] out object? instance);

        [PreserveSig]
        int OpenPropertyStore(uint access, out nint store);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        [PreserveSig]
        int GetState(out uint state);
    }

    [ComImport]
    [Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig]
        int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, nint format, nint sessionId);

        [PreserveSig]
        int GetBufferSize(out uint frames);

        [PreserveSig]
        int GetStreamLatency(out long latency);

        [PreserveSig]
        int GetCurrentPadding(out uint frames);

        [PreserveSig]
        int IsFormatSupported(int shareMode, nint format, out nint closestMatch);

        [PreserveSig]
        int GetMixFormat(out nint format);

        [PreserveSig]
        int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);

        [PreserveSig]
        int Start();

        [PreserveSig]
        int Stop();

        [PreserveSig]
        int Reset();

        [PreserveSig]
        int SetEventHandle(nint handle);

        [PreserveSig]
        int GetService(ref Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] out object? instance);
    }

    [ComImport]
    [Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig]
        int GetBuffer(out nint data, out uint frames, out uint flags, out ulong devicePosition, out ulong counterPosition);

        [PreserveSig]
        int ReleaseBuffer(uint frames);

        [PreserveSig]
        int GetNextPacketSize(out uint frames);
    }
}
