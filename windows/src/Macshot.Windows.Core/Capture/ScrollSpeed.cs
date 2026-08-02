namespace Macshot.Windows.Core.Capture;

/// <summary>
/// How far a scroll capture moves the page between one frame and the next.
/// </summary>
/// <remarks>
/// macshot's <c>scrollSpeed</c>, and its four words for it. Four fixed choices rather
/// than a number, because the useful range is narrow and both ends of it fail: too small
/// and a long page takes hundreds of frames, too large and a frame no longer contains any
/// of the one before it, which is the only thing the stitcher has to line them up by.
/// </remarks>
public enum ScrollSpeed
{
    Slow,

    Medium,

    /// <summary>The default, and the step this port has always taken.</summary>
    Fast,

    VeryFast,
}

/// <summary>What each speed means to the wheel.</summary>
public static class ScrollSpeeds
{
    /// <summary>
    /// Wheel notches sent per step.
    /// </summary>
    /// <remarks>
    /// <see cref="ScrollSpeed.Fast"/> is three, which is what the driver has always sent
    /// and the only one of these with any mileage on it. The others are one step either
    /// side rather than a wide spread: a step larger than the frame is a step that leaves
    /// the stitcher nothing to match, and how large that is depends on how tall a region
    /// the user drew — which nothing here can know.
    /// </remarks>
    public static int NotchesPerStep(ScrollSpeed speed) => speed switch
    {
        ScrollSpeed.Slow => 1,
        ScrollSpeed.Medium => 2,
        ScrollSpeed.VeryFast => 4,
        _ => 3,
    };
}
