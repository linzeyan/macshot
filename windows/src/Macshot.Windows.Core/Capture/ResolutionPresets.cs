namespace Macshot.Windows.Core.Capture;

/// <summary>
/// A shape the selection can be held to, or an exact size it can be set to.
/// </summary>
/// <param name="Label">What the menu shows.</param>
/// <param name="Aspect">The ratio to hold, or null for the exact sizes and for freeform.</param>
/// <param name="Width">The exact width in pixels, for the sizes.</param>
/// <param name="Height">The exact height in pixels, for the sizes.</param>
public readonly record struct ResolutionPreset(
    string Label,
    double? Aspect = null,
    int Width = 0,
    int Height = 0)
{
    /// <summary>True when this preset names a size rather than a shape.</summary>
    public bool IsExact => Width > 0 && Height > 0;
}

/// <summary>
/// What the size box offers: the shapes a capture is usually wanted in, and the sizes it
/// is usually wanted at.
/// </summary>
/// <remarks>
/// The same two lists the macOS app has, in the same order. They are the reason the box is
/// worth having at all — a capture for a 16 : 9 slide is otherwise dragged out by eye and
/// then complained about by whatever it is pasted into.
/// </remarks>
public static class ResolutionPresets
{
    /// <summary>The freeform entry, which clears whatever shape was being held.</summary>
    public static ResolutionPreset Freeform { get; } = new("Freeform");

    public static IReadOnlyList<ResolutionPreset> Ratios { get; } =
    [
        Freeform,
        new("1 : 1", 1.0),
        new("4 : 3", 4.0 / 3.0),
        new("3 : 2", 3.0 / 2.0),
        new("16 : 10", 16.0 / 10.0),
        new("16 : 9", 16.0 / 9.0),
        new("21 : 9", 21.0 / 9.0),
        new("5 : 1", 5.0),
        new("3 : 4", 3.0 / 4.0),
        new("9 : 16", 9.0 / 16.0),
    ];

    public static IReadOnlyList<ResolutionPreset> Sizes { get; } =
    [
        new("1920 × 1080", Width: 1920, Height: 1080),
        new("1920 × 384", Width: 1920, Height: 384),
        new("1280 × 720", Width: 1280, Height: 720),
        new("1080 × 1080", Width: 1080, Height: 1080),
        new("1080 × 1920", Width: 1080, Height: 1920),
        new("800 × 600", Width: 800, Height: 600),
        new("640 × 480", Width: 640, Height: 480),
    ];
}
