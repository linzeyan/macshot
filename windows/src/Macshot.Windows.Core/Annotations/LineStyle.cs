namespace Macshot.Windows.Core.Annotations;

public enum LineStyle
{
    Solid,
    Dashed,
    Dotted,
}

public static class LineStyleExtensions
{
    public static IReadOnlyList<double> CreateDashPattern(this LineStyle style, double strokeWidth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(strokeWidth);

        return style switch
        {
            LineStyle.Solid => [],
            LineStyle.Dashed => [strokeWidth * 3, strokeWidth * 2],
            LineStyle.Dotted => [0, Math.Max(strokeWidth * 2, 6)],
            _ => throw new ArgumentOutOfRangeException(nameof(style)),
        };
    }
}
