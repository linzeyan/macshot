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
