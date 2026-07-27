namespace Macshot.Windows.Core.Capture;

/// <summary>A point in capture pixels. See <c>docs/windows-port/architecture.md</c>, decision D6.</summary>
public readonly record struct CapturePoint(double X, double Y);
