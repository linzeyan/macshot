namespace Macshot.Windows.Core.Annotations;

/// <summary>
/// How much rounding a finished freehand stroke gets. macshot's three, under macshot's
/// names — PencilToolHandler.swift, mode 0, 1 and 2.
/// </summary>
public enum PencilSmoothing
{
    /// <summary>The path exactly as it was sampled, for tracing something.</summary>
    None,

    /// <summary>Corners cut. Loses the staircase without moving the stroke off its pixels.</summary>
    Smooth,

    /// <summary>Averaged along its length before the corners are cut, for a drawn line.</summary>
    Refined,
}
