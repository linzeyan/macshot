namespace Macshot.Windows.Core.Annotations;

public enum LineStyle
{
    Solid,
    Dashed,
    Dotted,
}

public static class LineStyleExtensions
{
    /// <param name="pixelsPerPoint">
    /// What the dots' floor of 6 points comes to where the line is drawn — see
    /// <see cref="AnnotationStyle.PixelsPerPoint"/>.
    /// </param>
    public static IReadOnlyList<double> CreateDashPattern(
        this LineStyle style,
        double strokeWidth,
        double pixelsPerPoint = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(strokeWidth);

        return style switch
        {
            LineStyle.Solid => [],
            LineStyle.Dashed => [strokeWidth * 3, strokeWidth * 2],
            LineStyle.Dotted => [0, Math.Max(strokeWidth * 2, 6 * pixelsPerPoint)],
            _ => throw new ArgumentOutOfRangeException(nameof(style)),
        };
    }
}
