namespace Macshot.Windows.Core.Capture;

/// <summary>
/// How loud the microphone is, as the meter on the toolbar's mic button draws it.
/// </summary>
/// <remarks>
/// <para>
/// The meter exists to answer one question before a recording starts: is this microphone
/// the one that is actually hearing me. A switch that only lights up cannot answer it —
/// the wrong device, a muted headset and a hand over the laptop's microphone all look
/// exactly like the right one — and there is no way to find out afterwards, because
/// whether the microphone was on is not something a finished recording can be asked.
/// </para>
/// <para>
/// So it is a peak meter rather than an average: a peak moves on the first syllable, while
/// an RMS over the same window is still climbing when the word has finished. It rises the
/// instant sound arrives and falls back slowly, which is macshot's own rule
/// (<c>OverlayView.swift:8727-8737</c>) and the reason a pause between words leaves the
/// bar somewhere rather than flickering to nothing and back twice a second.
/// </para>
/// </remarks>
public sealed class MicrophoneLevel
{
    /// <summary>
    /// How often the meter is asked. macshot's twenty times a second: fast enough that the
    /// bar moves with the voice, slow enough that it is not a layout pass per audio packet.
    /// </summary>
    public static TimeSpan Interval { get; } = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Below this the meter is not drawn at all. A hairline of green over silence would
    /// say the microphone was hearing something when it is hearing the noise floor.
    /// </summary>
    public const double Silent = 0.001;

    /// <summary>
    /// How much smaller a change may be and still not be worth redrawing for. macshot's
    /// 0.005, and the reason the bar is not re-laid out twenty times a second while nobody
    /// is speaking.
    /// </summary>
    public const double Visible = 0.005;

    /// <summary>
    /// What is kept of the last reading when the new one is quieter. Nothing at all would
    /// make the bar flicker between syllables; too much and it would still be falling from
    /// a cough a second later.
    /// </summary>
    private const double Held = 0.8;

    /// <summary>What the meter should be showing, from nothing to full scale.</summary>
    public double Current { get; private set; }

    /// <summary>
    /// The loudest sample in <paramref name="samples"/>, as a fraction of full scale.
    /// </summary>
    /// <remarks>
    /// The magnitude is taken as an <see cref="int"/> because the most negative 16-bit
    /// sample has no positive counterpart: negating <c>short.MinValue</c> in 16 bits gives
    /// back <c>short.MinValue</c>, so a meter that worked in shorts would read the loudest
    /// possible sound as silence.
    /// </remarks>
    public static double PeakOf(ReadOnlySpan<short> samples)
    {
        var peak = 0;
        foreach (var sample in samples)
        {
            var magnitude = Math.Abs((int)sample);
            if (magnitude > peak)
            {
                peak = magnitude;
            }
        }

        return Math.Min(1, peak / (double)short.MaxValue);
    }

    /// <summary>
    /// Moves the meter towards <paramref name="peak"/>, and says whether it moved far
    /// enough to be worth redrawing.
    /// </summary>
    public bool Follow(double peak)
    {
        var wanted = Math.Clamp(peak, 0, 1);

        // Straight to a louder reading, and only part of the way to a quieter one. A meter
        // that eased upwards would still be climbing when the word had finished, which is
        // the reading the user is looking for.
        var next = wanted > Current ? wanted : (Current * Held) + (wanted * (1 - Held));

        // Settled rather than approached forever: a release that only ever multiplies never
        // reaches zero, and a bar a thousandth high over a silent room is a bar saying the
        // microphone hears something.
        if (next < Silent)
        {
            next = 0;
        }

        var moved = Math.Abs(next - Current) >= Visible || (next == 0 && Current != 0);
        Current = next;
        return moved;
    }

    /// <summary>
    /// Puts the meter back to nothing, for when the microphone is switched off or the
    /// recording setup is left. Says whether anything was showing.
    /// </summary>
    public bool Silence()
    {
        var showing = Current != 0;
        Current = 0;
        return showing;
    }
}
