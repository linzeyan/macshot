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

    /// <summary>eRender and eCapture, which is what tells the two endpoint kinds apart.</summary>
    private const int RenderFlow = 0;

    private const int CaptureFlow = 1;

    /// <summary>eConsole: the endpoint the user hears and speaks into, not the one games use.</summary>
    private const int ConsoleRole = 0;

    /// <summary>DEVICE_STATE_ACTIVE. A disabled or unplugged endpoint is not offered.</summary>
    private const uint DeviceStateActive = 0x1;

    /// <summary>STGM_READ, which is all a name needs.</summary>
    private const uint StorageRead = 0;

    /// <summary>VT_LPWSTR, the only type a friendly name comes back as.</summary>
    private const short VtLpwstr = 31;

    /// <summary>
    /// A PROPVARIANT, whose union starts one pointer in on both architectures this ships
    /// for. Read by offset rather than as a struct, the way <see cref="WaveFormat"/>
    /// writes its WAVEFORMATEX: the union has no C# spelling that does not either need
    /// unsafe code or leave fields nothing ever assigns.
    /// </summary>
    private const int SizeOfPropVariant = 24;

    private const int PropVariantValueOffset = 8;

    /// <summary>
    /// PKEY_Device_FriendlyName — "Headset (WH-1000XM4)" rather than the endpoint id.
    /// </summary>
    private static readonly PropertyKey FriendlyNameKey = new()
    {
        FormatId = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        Id = 14,
    };

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
    /// Opens an endpoint for <paramref name="source"/>, or null when the machine has none
    /// or refuses the format.
    /// </summary>
    /// <param name="deviceId">
    /// Which microphone, from <see cref="Microphones"/>, or null for whichever one Windows
    /// would open. Meaningless for <see cref="AudioSource.System"/>: what a loopback
    /// stream records is what the machine is playing, and macshot offers no choice of that
    /// either.
    /// </param>
    /// <remarks>
    /// Null rather than an exception: a machine with no microphone is an ordinary
    /// machine, and a recording that fails outright because nothing is plugged in would
    /// be worse than one without sound.
    /// </remarks>
    public static AudioEndpoint? Open(AudioSource source, string? deviceId)
    {
        nint format = 0;
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();

            // A loopback stream is opened on the *render* endpoint — the speakers — and
            // read from as though it were a capture device. eConsole rather than
            // eMultimedia: it is what the user hears.
            var flow = source == AudioSource.System ? RenderFlow : CaptureFlow;

            IMMDevice? device = null;
            if (source == AudioSource.Microphone && !string.IsNullOrEmpty(deviceId)
                && enumerator.GetDevice(deviceId, out var chosen) == 0)
            {
                device = chosen;
            }

            // A remembered microphone that no longer resolves falls through to the default
            // one, which is macshot's answer (RecordingEngine.swift:278). A headset
            // unplugged since the last recording must not be why this one is silent.
            if (device is null)
            {
                if (enumerator.GetDefaultAudioEndpoint(flow, ConsoleRole, out var fallback) != 0
                    || fallback is null)
                {
                    return null;
                }

                device = fallback;
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

    /// <summary>
    /// Every microphone the machine has switched on, in the order the audio engine lists
    /// them — which is the order Windows itself prefers them in.
    /// </summary>
    /// <remarks>
    /// Through the same API the endpoint is opened with rather than through
    /// <c>DeviceInformation</c>, because the id has to be one <c>GetDevice</c> accepts:
    /// the enumeration APIs name the same hardware differently, and a remembered id from
    /// the wrong one would resolve to nothing every time and silently record from the
    /// default microphone for ever.
    /// </remarks>
    public static IReadOnlyList<RecordingDevice> Microphones()
    {
        var found = new List<RecordingDevice>();

        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (enumerator.EnumAudioEndpoints(CaptureFlow, DeviceStateActive, out var collection) != 0
                || collection is null)
            {
                return found;
            }

            try
            {
                if (collection.GetCount(out var count) != 0)
                {
                    return found;
                }

                for (uint index = 0; index < count; index++)
                {
                    if (collection.Item(index, out var device) != 0 || device is null)
                    {
                        continue;
                    }

                    try
                    {
                        // Both or neither: a device with no name has no row to show, and
                        // one with no id has nothing to remember the choice by.
                        if (device.GetId(out var id) == 0 && FriendlyName(device) is { } name)
                        {
                            found.Add(new RecordingDevice(id, name));
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(collection);
            }
        }
        catch (COMException)
        {
            // A machine whose audio service will not answer offers no menu, which is the
            // same as a machine with no microphone.
        }
        catch (InvalidCastException)
        {
        }

        return found;
    }

    /// <summary>
    /// The microphone Windows would open unasked, or null when there is none.
    /// </summary>
    /// <remarks>
    /// Asked for so the menu can tick it: with nothing remembered, the row that would
    /// actually be recorded is the default one, and a menu ticking nothing would read as
    /// though the switch were off.
    /// </remarks>
    public static string? DefaultMicrophoneId()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (enumerator.GetDefaultAudioEndpoint(CaptureFlow, ConsoleRole, out var device) != 0
                || device is null)
            {
                return null;
            }

            try
            {
                return device.GetId(out var id) == 0 ? id : null;
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
        }
        catch (COMException)
        {
            return null;
        }
        catch (InvalidCastException)
        {
            return null;
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
    /// What an endpoint calls itself, or null when it will not say.
    /// </summary>
    /// <remarks>
    /// The name is what the menu shows, so a device that has none is not offered: a blank
    /// row cannot be chosen between, and an endpoint id shown in its place would be forty
    /// characters of braces and zeroes.
    /// </remarks>
    private static string? FriendlyName(IMMDevice device)
    {
        if (device.OpenPropertyStore(StorageRead, out var store) != 0 || store is null)
        {
            return null;
        }

        var key = FriendlyNameKey;
        var value = Marshal.AllocHGlobal(SizeOfPropVariant);

        try
        {
            // Zeroed first: PropVariantClear reads the tag to decide what to free, and
            // whatever AllocHGlobal handed over would be freed as though it were one.
            for (var offset = 0; offset < SizeOfPropVariant; offset += sizeof(long))
            {
                Marshal.WriteInt64(value, offset, 0);
            }

            if (store.GetValue(ref key, value) != 0 || Marshal.ReadInt16(value, 0) != VtLpwstr)
            {
                return null;
            }

            return Marshal.PtrToStringUni(Marshal.ReadIntPtr(value, PropVariantValueOffset));
        }
        finally
        {
            PropVariantClear(value);
            Marshal.FreeHGlobal(value);
            Marshal.ReleaseComObject(store);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(nint value);

    /// <summary>A PROPERTYKEY: which property, of which set.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint Id;
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

    // The interfaces below are declared in the order their methods appear in
    // Mmdeviceapi.h, Audioclient.h and Propsys.h. The order *is* the binding — a method
    // declared out of place calls whichever function sits at that slot — so nothing here
    // may be reordered or left out, however unused it is.
    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, uint stateMask, out IMMDeviceCollection? devices);

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
        int OpenPropertyStore(uint access, out IPropertyStore? store);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        [PreserveSig]
        int GetState(out uint state);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out uint count);

        [PreserveSig]
        int Item(uint index, out IMMDevice? device);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint count);

        [PreserveSig]
        int GetAt(uint index, out PropertyKey key);

        // The value is a caller-allocated PROPVARIANT, passed as memory rather than as a
        // struct for the reason SizeOfPropVariant gives.
        [PreserveSig]
        int GetValue(ref PropertyKey key, nint value);

        [PreserveSig]
        int SetValue(ref PropertyKey key, nint value);

        [PreserveSig]
        int Commit();
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
