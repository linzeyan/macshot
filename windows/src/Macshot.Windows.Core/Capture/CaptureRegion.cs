namespace Macshot.Windows.Core.Capture;

public readonly record struct CaptureRegion(double X, double Y, double Width, double Height)
{
    public static CaptureRegion FromPoints(double startX, double startY, double endX, double endY)
    {
        return new CaptureRegion(
            Math.Min(startX, endX),
            Math.Min(startY, endY),
            Math.Abs(endX - startX),
            Math.Abs(endY - startY));
    }

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public double Right => X + Width;

    public double Bottom => Y + Height;

    /// <summary>
    /// Half-open containment: the right and bottom edges belong to the neighbour.
    /// Adjacent displays share an edge coordinate, and letting both claim it would
    /// make the monitor a pointer lands on ambiguous.
    /// </summary>
    public bool Contains(double x, double y) => x >= X && x < Right && y >= Y && y < Bottom;

    public CaptureRegion Union(CaptureRegion other)
    {
        if (IsEmpty)
        {
            return other;
        }

        if (other.IsEmpty)
        {
            return this;
        }

        return FromPoints(
            Math.Min(X, other.X),
            Math.Min(Y, other.Y),
            Math.Max(Right, other.Right),
            Math.Max(Bottom, other.Bottom));
    }
}
