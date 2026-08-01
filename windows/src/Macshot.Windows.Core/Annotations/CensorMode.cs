namespace Macshot.Windows.Core.Annotations;

/// <summary>
/// How the censor tool covers what it is dragged over. macshot's four, in macshot's
/// order — <c>CensorMode</c> in <c>Model/Annotation.swift</c>.
/// </summary>
/// <remarks>
/// One tool with four modes rather than four tools, because the choice is made after
/// the region is drawn as often as before it: what a redaction should look like depends
/// on what turned out to be inside it.
/// </remarks>
public enum CensorMode
{
    /// <summary>Averaged into blocks. The default, and what a reader recognises as redacted.</summary>
    Pixelate,

    /// <summary>Blurred. Softer, and legible again if the region is small enough — see the radius.</summary>
    Blur,

    /// <summary>Painted over in the chosen colour. The only mode that cannot be undone from the pixels.</summary>
    Solid,

    /// <summary>
    /// Filled with the colours around it, so the region reads as empty background rather
    /// than as something removed.
    /// </summary>
    Erase,
}
