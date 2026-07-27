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
}
