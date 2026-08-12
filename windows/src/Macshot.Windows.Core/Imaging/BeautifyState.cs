using System.Text.Json.Serialization;

namespace Macshot.Windows.Core.Imaging;

/// <summary>
/// The frame a capture is carrying, as numbers beside the untouched pixels rather than as
/// the background they were mounted on.
/// </summary>
/// <remarks>
/// <para>
/// macshot's beautify half of <c>CaptureEditState</c>
/// (<c>Model/CaptureEditState.swift:11-19</c>). A frame is not a mark and not an image
/// operation: it is decided once and applied last, over everything that was drawn, so the
/// only thing that has to survive a capture being archived is what it was set to. Written
/// down, the capture reopens as the picture that was approved <em>and</em> as one whose
/// frame can still be taken off; burnt into the pixels, the background becomes the
/// screenshot and there is no undo left that could tell the two apart.
/// </para>
/// <para>
/// Every measurement is in points, as macshot's are, with <see cref="Scale"/> saying what
/// turned them into pixels. That field is this port's own and has no counterpart on the
/// Mac, where an <c>NSImage</c> carries its own scale: a <c>CapturedFrame</c> here is a
/// bare buffer, so a capture framed on a 200% display would reopen with half the padding
/// it was delivered with unless the number that scaled it is kept too.
/// </para>
/// </remarks>
public sealed record BeautifyState
{
    /// <summary>Whether there is a frame at all — macshot's <c>beautifyEnabled</c>.</summary>
    public bool Enabled { get; init; }

    /// <summary>Whether the card is drawn as a window, with a title bar above the capture.</summary>
    public BeautifyMode Mode { get; init; } = BeautifyOptions.Default.Mode;

    /// <summary>
    /// Which background, or <see cref="BeautifyOptions.CustomBackgroundStyle"/> for the
    /// picture in <see cref="Background"/>.
    /// </summary>
    public int StyleIndex { get; init; } = BeautifyOptions.Default.StyleIndex;

    public double Padding { get; init; } = BeautifyOptions.Default.Padding;

    public double CornerRadius { get; init; } = BeautifyOptions.Default.CornerRadius;

    public double ShadowRadius { get; init; } = BeautifyOptions.Default.ShadowRadius;

    public double BackgroundBlur { get; init; } = BeautifyOptions.Default.BackgroundBlur;

    /// <summary>
    /// Whether the capture is a window picked out of the desktop rather than a region
    /// dragged over it.
    /// </summary>
    /// <remarks>
    /// macshot's <c>beautifyIsWindowSnap</c>, and it is stored rather than derived because
    /// the pixels cannot be asked: those of a snapped window arrive with their own title bar
    /// and their own rounded corners already in them, so a frame put round them a second
    /// time has to draw neither. Reopened without it, re-arming the frame would stack a
    /// synthetic title bar on top of a real one.
    /// </remarks>
    public bool IsWindowSnap { get; init; }

    /// <summary>
    /// How many pixels a point was worth when the frame was drawn.
    /// </summary>
    /// <remarks>
    /// This port's own field. macshot needs none because an <c>NSImage</c> carries its
    /// scale; a <c>CapturedFrame</c> is a bare buffer, so without this a capture framed on
    /// a 200% display reopens with half the padding it was delivered with.
    /// </remarks>
    public double Scale { get; init; } = 1;

    /// <summary>
    /// The picture the capture was mounted on, byte for byte as it was chosen, or null for
    /// one of the gradients.
    /// </summary>
    /// <remarks>
    /// Carried here rather than pointed at, which is what macshot does with it
    /// (<c>CaptureEditState.swift:67-72</c>) and for the reason that outlives any path:
    /// there is one custom background on the machine, the user replaces it whenever they
    /// like, and a capture that reopened on whichever picture is current would reopen as a
    /// different picture from the one that was approved. Not re-encoded on the way in — the
    /// bytes are the file the user chose, whatever format it was — so the field says what
    /// it holds rather than promising PNG.
    /// </remarks>
    public byte[]? Background { get; init; }

    /// <summary>No frame, which is what a capture nobody framed has.</summary>
    public static BeautifyState Default { get; } = new();

    /// <summary>
    /// What the renderer needs, with the picture decoded by whoever could decode it.
    /// </summary>
    /// <remarks>
    /// macshot's <c>beautifyConfig()</c> (<c>CaptureEditState.swift:46-62</c>), including
    /// its one omission: <c>beautifyBgRadius</c> is read there with a default of 8 and then
    /// passed as 0 regardless, so it rounds nothing and is not stored here either.
    /// </remarks>
    /// <param name="backdrop">
    /// <see cref="Background"/> as pixels. Core cannot decode an image, so it arrives from
    /// the caller; without it the custom style falls back to a gradient, which is what
    /// <see cref="BeautifyOptions.Normalized"/> does with a sentinel it cannot honour.
    /// </param>
    public BeautifyOptions ToOptions(BeautifyBackdrop? backdrop = null) => new BeautifyOptions(
        StyleIndex,
        Padding,
        CornerRadius,
        ShadowRadius,
        BeautifyOptions.Default.ShadowOpacity,
        Enabled,
        Mode,
        BackgroundBlur,
        backdrop).Normalized();

    /// <summary>
    /// The frame that was drawn, ready to be archived beside the capture it was drawn
    /// around.
    /// </summary>
    /// <param name="background">
    /// The bytes of the picture in use, which are kept only while that picture is the
    /// background — macshot's own guard (<c>CaptureEditState.swift:68</c>). A megabyte of
    /// JPEG in the sidecar of a capture on a gradient would be a megabyte nothing reads.
    /// </param>
    public static BeautifyState Of(
        BeautifyOptions options,
        bool isWindowSnap,
        double scale,
        byte[]? background = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var resolved = options.Normalized();
        return new BeautifyState
        {
            Enabled = resolved.Enabled,
            Mode = resolved.Mode,
            StyleIndex = resolved.StyleIndex,
            Padding = resolved.Padding,
            CornerRadius = resolved.CornerRadius,
            ShadowRadius = resolved.ShadowRadius,
            BackgroundBlur = resolved.BackgroundBlur,
            IsWindowSnap = isWindowSnap,
            Scale = BeautifyRenderer.SaneScale(scale),
            Background = resolved.StyleIndex == BeautifyOptions.CustomBackgroundStyle
                ? background
                : null,
        }.Normalized();
    }

    /// <summary>
    /// Clamps every field into the range the frame's row can ask for, so a hand-edited
    /// sidecar cannot reopen a capture inside a frame no control could take back off.
    /// </summary>
    public BeautifyState Normalized()
    {
        // The sentinel survives only while there are bytes to honour it, which is
        // BeautifyOptions' rule one step earlier: there the picture has been decoded, here
        // it has not, and a style index of -1 over an empty field indexes nothing.
        var custom = StyleIndex == BeautifyOptions.CustomBackgroundStyle
            && Background is { Length: > 0 };

        return this with
        {
            StyleIndex = custom
                ? BeautifyOptions.CustomBackgroundStyle
                : BeautifyRenderer.Styles.Count == 0
                    ? 0
                    : Math.Clamp(StyleIndex, 0, BeautifyRenderer.Styles.Count - 1),

            // The far ends of the frame's four sliders, and BeautifyOptions' own bands, so
            // a number that arrives from a file means the same thing as one that arrives
            // from a drag.
            Padding = Math.Clamp(Padding, 0, BeautifyOptions.MaximumPadding),
            CornerRadius = Math.Clamp(CornerRadius, 0, BeautifyOptions.MaximumCornerRadius),
            ShadowRadius = Math.Clamp(ShadowRadius, 0, BeautifyOptions.MaximumShadowRadius),
            BackgroundBlur = Math.Clamp(BackgroundBlur, 0, BeautifyOptions.MaximumBackgroundBlur),
            Mode = Enum.IsDefined(Mode) ? Mode : BeautifyOptions.Default.Mode,
            Scale = BeautifyRenderer.SaneScale(Scale),
            Background = custom ? Background : null,
        };
    }

    /// <summary>
    /// By value, including the picture's bytes.
    /// </summary>
    /// <remarks>
    /// Written out because the synthesized comparison of a record compares a
    /// <see cref="byte"/> array by reference, and this value is the editor's answer to "has
    /// anything changed since this was last written down". Two states holding the same
    /// picture read from disk twice would otherwise differ, and the window would offer to
    /// save a capture nobody had touched.
    /// </remarks>
    public bool Equals(BeautifyState? other) =>
        other is not null
        && Enabled == other.Enabled
        && Mode == other.Mode
        && StyleIndex == other.StyleIndex
        && Padding == other.Padding
        && CornerRadius == other.CornerRadius
        && ShadowRadius == other.ShadowRadius
        && BackgroundBlur == other.BackgroundBlur
        && IsWindowSnap == other.IsWindowSnap
        && Scale == other.Scale
        && ((Background is null && other.Background is null)
            || (Background is not null
                && other.Background is not null
                && Background.AsSpan().SequenceEqual(other.Background)));

    /// <summary>
    /// The picture's length rather than its contents, because this is only ever a bucket
    /// hint and hashing a megabyte on every comparison would cost more than the comparison.
    /// </summary>
    public override int GetHashCode() => HashCode.Combine(
        HashCode.Combine(Enabled, Mode, StyleIndex, Padding, CornerRadius, ShadowRadius),
        BackgroundBlur,
        IsWindowSnap,
        Scale,
        Background?.Length ?? 0);

    /// <summary>
    /// Whether the frame is one the file has to remember — macshot's half of
    /// <c>hasPostProcessing</c> (<c>CaptureEditState.swift:36</c>).
    /// </summary>
    /// <remarks>
    /// The switch alone, not the numbers: padding left at its default around a capture
    /// nobody framed is not a frame, and every other field is inert while this one is off.
    /// </remarks>
    [JsonIgnore]
    public bool IsIdentity => !Enabled;
}
