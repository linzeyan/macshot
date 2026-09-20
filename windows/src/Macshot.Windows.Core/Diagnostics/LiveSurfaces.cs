namespace Macshot.Windows.Core.Diagnostics;

/// <summary>
/// How many of the windows that hold a capture are still alive, against how many were
/// ever made.
/// </summary>
/// <remarks>
/// <para>
/// This is the instrument that found the leak, made permanent. A
/// <c>DispatcherQueue</c> timer holds its handler, its handler holds whatever it closed
/// over, and the queue lives as long as the thread — so one overlay's press-and-hold
/// timer kept every overlay ever raised, toolbar and canvas and frozen screens and all.
/// It read for a long time as "WinUI never collects anything shown once per capture",
/// which is a conclusion about the framework rather than about this code, and it was
/// wrong. What settled it was a <see cref="WeakReference"/> per overlay printed after a
/// forced collection: 5/5 before the fix and 1/5 after.
/// </para>
/// <para>
/// That measurement took a debugger and a machine to run it on. Written into the log it
/// costs one line per capture and answers the same question on a machine nobody here can
/// reach — which is where every report of this actually comes from. A count that does not
/// fall back to one or zero once the capture is delivered is the whole signature.
/// </para>
/// <para>
/// Only meaningful straight after a blocking gen-2 collection: an idle tray app never
/// collects, so before one runs every surface ever made is still alive and the count says
/// nothing at all.
/// </para>
/// <para>
/// Read it knowing what each kind can do. <c>overlay</c> is a <c>UserControl</c> and
/// answered <c>1/1, 1/2, 1/3</c> over three captures on the VM, which is the shape a
/// healthy one has: the one on screen, and every earlier one collected. A <c>Window</c>
/// cannot be expected to do that — the framework holds it and gives it back on its own
/// schedule, which is exactly why every window here drops its pixels by hand on close
/// rather than waiting to be collected. So a window kind sitting at <c>3/3</c> is the
/// normal reading, and what it is worth depends on whether that window let go of what it
/// was holding; the overlay's ratio is the one that answers a question about this code.
/// </para>
/// </remarks>
public sealed class LiveSurfaces
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Watched> _kinds = [];

    /// <summary>
    /// The one every surface reports to. A process-wide fact, like the log it is written
    /// to; an instance exists so that a test can have its own.
    /// </summary>
    public static LiveSurfaces Shared { get; } = new();

    /// <summary>Starts watching one surface, without keeping it alive.</summary>
    public void Watch(string kind, object surface)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(surface);

        lock (_gate)
        {
            if (!_kinds.TryGetValue(kind, out var watched))
            {
                watched = new Watched();
                _kinds[kind] = watched;
            }

            watched.EverMade++;
            watched.Alive.Add(new WeakReference(surface));
        }
    }

    /// <summary>
    /// A census in the form <c>overlay 1/6, thumbnail 0/3</c>, alive out of ever made.
    /// </summary>
    /// <remarks>
    /// Sorted by name so that two runs of the same session can be read against each other
    /// without hunting for the moved column.
    /// </remarks>
    public string Census()
    {
        lock (_gate)
        {
            if (_kinds.Count == 0)
            {
                return "nothing is being watched";
            }

            return string.Join(", ", _kinds.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
            {
                // Pruned rather than counted, so a long session does not carry a reference
                // per capture for the rest of its life.
                pair.Value.Alive.RemoveAll(reference => !reference.IsAlive);
                return $"{pair.Key} {pair.Value.Alive.Count}/{pair.Value.EverMade}";
            }));
        }
    }

    private sealed class Watched
    {
        public List<WeakReference> Alive { get; } = [];

        public int EverMade { get; set; }
    }
}
