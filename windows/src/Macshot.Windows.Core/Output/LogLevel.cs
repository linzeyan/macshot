namespace Macshot.Windows.Core.Output;

/// <summary>
/// How much macshot writes to its diagnostic log. Each level includes the ones above it.
/// </summary>
public enum LogLevel
{
    /// <summary>
    /// Failures, and the few one-off facts a failure is read against: the version that
    /// started, which codecs loaded, a capture that was slow to appear.
    /// </summary>
    Normal,

    /// <summary>
    /// Every step of a capture, a recording or a delivery, with how long it took.
    /// </summary>
    Debug,

    /// <summary>
    /// Every frame, and every exception thrown anywhere in the process whether or not
    /// something caught it.
    /// </summary>
    Trace,
}
