namespace Macshot.Windows.Core.Output;

/// <summary>
/// Which light or dark to draw macshot's own windows in.
/// </summary>
/// <remarks>
/// macshot's <c>appTheme</c>. It covers the settings window, the editor and the
/// recognition window — the ones that are ordinary windows — and deliberately not the
/// capture toolbar, which is drawn over a screenshot rather than inside a window and so
/// keeps its own dark palette whatever this says. A toolbar that followed the system
/// theme would disappear into half the captures anyone takes.
/// </remarks>
public enum AppTheme
{
    /// <summary>Whatever Windows is set to, and changing with it.</summary>
    System,

    Light,

    Dark,
}
