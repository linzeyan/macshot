namespace Macshot.Windows.Core.Capture;

/// <summary>
/// Which of the three kinds of pre-selection preset the settings file is holding.
/// </summary>
/// <remarks>
/// <see cref="Inherited"/> is not a fourth kind of shape — it is what a file that has never
/// been asked holds, and it means "whatever keep-ratio is holding". Without it, someone who
/// has only ever used the size box would find the shape they set there ignored the moment
/// this feature shipped. macshot's <c>PreSelectionPresetStorageKind</c>, in its order
/// (<c>OverlayView.swift:935-939</c>).
/// </remarks>
public enum PreSelectionPresetKind
{
    Inherited = 0,
    Freeform = 1,
    Ratio = 2,
    Resolution = 3,
}

/// <summary>
/// The shape, or the exact size, the next drag will produce — chosen while there is still
/// no region to shape.
/// </summary>
/// <remarks>
/// <para>
/// The same two things the size box offers, asked for an hour earlier: a ratio holds the
/// marquee as it is dragged out, an exact size stops it being a drag at all and makes the
/// press place a box of that size. Freeform is the absence of both, and is the default.
/// </para>
/// <para>
/// Built through the factories rather than the positional constructor, which cannot refuse
/// a ratio of zero — that is how "no shape" is spelt in the settings file, and it would
/// divide a height away to nothing.
/// </para>
/// </remarks>
public readonly record struct PreSelectionPreset(double? Aspect = null, int Width = 0, int Height = 0)
{
    /// <summary>No shape and no size: the next drag is whatever the pointer makes it.</summary>
    public static PreSelectionPreset Freeform => default;

    /// <summary>A shape to hold the next drag to, or freeform when it is not a shape.</summary>
    public static PreSelectionPreset OfRatio(double aspect) =>
        double.IsFinite(aspect) && aspect > 0 ? new(aspect) : Freeform;

    /// <summary>A size to place, or freeform when it is not a size.</summary>
    public static PreSelectionPreset OfSize(int width, int height) =>
        width > 0 && height > 0 ? new(null, width, height) : Freeform;

    /// <summary>True when this names a size to be placed rather than a shape to be dragged.</summary>
    public bool IsExact => Width > 0 && Height > 0;

    /// <summary>The shape being held, or null when there is none. Never zero.</summary>
    public double? Ratio => !IsExact && Aspect is { } aspect && aspect > 0 ? aspect : null;

    /// <summary>
    /// What the button says it is holding, or null when it is holding nothing.
    /// </summary>
    /// <remarks>
    /// The catalogue's own wording wherever there is one, so the button and the menu name
    /// the same shape the same way. macshot reduces an unnamed ratio against the selection's
    /// pixels; there is no selection here, so an unnamed one falls back to the decimal form
    /// that is macshot's own last resort (<c>OverlayView.swift:2545</c>).
    /// </remarks>
    public string? Label
    {
        get
        {
            // Copied out first: a lambda inside a struct may not reach this instance's own
            // members, and the catalogue is searched with one.
            var (width, height) = (Width, Height);

            if (IsExact)
            {
                var size = ResolutionPresets.Sizes
                    .FirstOrDefault(preset => preset.Width == width && preset.Height == height);

                return size.IsExact ? size.Label : $"{width} × {height}";
            }

            if (Ratio is not { } ratio)
            {
                return null;
            }

            var named = ResolutionPresets.Ratios
                .FirstOrDefault(preset => preset.Aspect is { } value && Math.Abs(value - ratio) < 0.001);

            return named.Aspect is null
                ? ratio.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture) + " : 1"
                : named.Label;
        }
    }
}
