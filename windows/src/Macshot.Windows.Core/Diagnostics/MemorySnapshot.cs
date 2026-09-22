using System.Diagnostics;

namespace Macshot.Windows.Core.Diagnostics;

/// <summary>
/// What the process weighed at one moment, in the terms a memory report is argued in.
/// </summary>
/// <remarks>
/// <para>
/// Every memory question this app has had was asked about a machine nobody here can
/// reach — a VDI where "it does not give the memory back for twenty minutes" is the
/// entire available evidence. Task Manager shows the private bytes and nothing behind
/// them, so a one-time high-water mark and a leak look identical, and the reply is a
/// guess. Written into the log, the same moment answers which it was.
/// </para>
/// <para>
/// Private bytes is the number the report is about; the other three say where it went.
/// Managed is what is alive, committed is what the collector is holding on the app's
/// behalf against roughly that, and the large object heap is where a screen-sized
/// buffer lands — 85KB is the threshold and a 2038x1588 frame is 12.3MB, so a capture's
/// cost is entirely there. Committed far above managed, with the large object heap
/// mostly unused, is fragmentation rather than retention.
/// </para>
/// </remarks>
public readonly record struct MemorySnapshot(
    long PrivateBytes,
    long ManagedBytes,
    long CommittedBytes,
    long LargeObjectBytes,
    long LargeObjectUnusedBytes,
    bool AfterACollection)
{
    /// <summary>The generation the runtime files the large object heap under.</summary>
    private const int LargeObjectGeneration = 3;

    /// <summary>Above this much alive, compacting costs more than it returns.</summary>
    /// <remarks>
    /// Set from the measured side of a gap an order of magnitude wide, not from theory,
    /// and it has already had to move once. Ordinary use reaches further up than the
    /// first measurements suggested: a field machine's captures arrive with 16.9-18MB
    /// alive and its recordings with 4.3-5.2MB, and a minute of full-screen recording on
    /// the VM — a process peaking at 625MB — with 43MB, but a region recording taken
    /// after a screenshot in the same session arrives with <em>88-105MB</em>. A ceiling
    /// at 128MB would have stopped compacting on that, which is the ordinary case and
    /// exactly the one compaction is for.
    ///
    /// The one run where compacting was ruinous, a recording that settled at 2691MB with
    /// it against 845-994MB without, was a heap hundreds of megabytes large and is a heap
    /// this app no longer builds: it predates the frame pool, which took that same
    /// measurement to 175MB by stopping the per-frame large-object churn that fragmented
    /// it. So this guards a shape that has been designed out, and sits five times above
    /// the heaviest ordinary heap ever measured and well below the one that misbehaved.
    /// There is no evidence between the two — <see cref="Since"/> prints the figure this
    /// was compared against precisely so that the next report can move it again.
    /// </remarks>
    private const long CompactionCeiling = 512L * 1024 * 1024;

    /// <summary>
    /// Whether a collection taken at this moment should compact the large object heap.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked of <see cref="ManagedBytes"/> and not of <see cref="LargeObjectBytes"/>,
    /// although the second is the heap in question: everything
    /// <c>GC.GetGCMemoryInfo</c> reports describes the <em>previous</em> collection, so
    /// on the path this decides — collect now, having just dropped tens of megabytes —
    /// it is exactly the stale number. <c>GC.GetTotalMemory(false)</c> is the only live
    /// one, and a process whose whole cost is screen-sized buffers is a process where
    /// nearly all of it is the large object heap anyway.
    /// </para>
    /// <para>
    /// This used to be the caller's to declare, on the reasoning that a capture and a
    /// recording are different in kind. They are not: a recording that kept its frames
    /// and a capture of a 4K screen are the same shape of heap, and a field log settled
    /// the point — three recordings on a machine whose compositor delivered nothing had
    /// 4.3-5.2MB alive apiece, well under any capture, and each left 21MB of large-object
    /// holes that nothing reclaimed until the next capture. The call site knew "this is a
    /// recording" and that was the wrong thing to know.
    /// </para>
    /// </remarks>
    public bool IsWorthCompacting => ManagedBytes < CompactionCeiling;

    /// <remarks>
    /// <see cref="Process.PrivateMemorySize64"/> answers 0 on macOS, where this type's
    /// tests also run. That is not worth working around — the product is Windows-only
    /// and the number is the Windows one — but a test asserting it is positive would
    /// pass on the VM and fail on the machine it is written on.
    /// </remarks>
    public static MemorySnapshot Take()
    {
        var info = GC.GetGCMemoryInfo();
        var large = info.GenerationInfo.Length > LargeObjectGeneration
            ? info.GenerationInfo[LargeObjectGeneration]
            : default;

        using var process = Process.GetCurrentProcess();
        return new MemorySnapshot(
            process.PrivateMemorySize64,
            GC.GetTotalMemory(forceFullCollection: false),
            info.TotalCommittedBytes,
            large.SizeAfterBytes,
            large.FragmentationAfterBytes,

            // Everything from GetGCMemoryInfo describes the last collection, so before
            // the first one it is all zero — which would read as a process holding
            // nothing rather than as a question not yet answerable.
            info.Index > 0);
    }

    public string Describe() => $"private {Mb(PrivateBytes)}, managed {Mb(ManagedBytes)}" + Collected();

    /// <summary>The same numbers against an earlier moment, which is what a fix shows up in.</summary>
    public string Since(MemorySnapshot before) =>
        $"private {Mb(before.PrivateBytes)} -> {Mb(PrivateBytes)}, "
            + $"managed {Mb(before.ManagedBytes)} -> {Mb(ManagedBytes)}"
            + Collected();

    private static string Mb(long bytes) => $"{bytes / (1024.0 * 1024.0):0.#}MB";

    private string Collected() => AfterACollection
        ? $", committed {Mb(CommittedBytes)}, "
            + $"large objects {Mb(LargeObjectBytes)} with {Mb(LargeObjectUnusedBytes)} unused"
        : string.Empty;
}
