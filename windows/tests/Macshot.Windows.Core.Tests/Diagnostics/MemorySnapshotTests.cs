using Macshot.Windows.Core.Diagnostics;

namespace Macshot.Windows.Core.Tests.Diagnostics;

[TestClass]
public sealed class MemorySnapshotTests
{
    [TestMethod]
    public void Since_ShowsWhatACollectionActuallyReturned()
    {
        // The line this produces is the whole reason the type exists: a user's log has
        // to be able to answer "is macshot holding this, or has the collector simply
        // not run" without anyone reaching their machine.
        var before = new MemorySnapshot(180 * Mb, 21 * Mb, 59 * Mb, 36 * Mb, 30 * Mb, AfterACollection: true);
        var after = new MemorySnapshot(151 * Mb, 6 * Mb, 44 * Mb, 15 * Mb, 0, AfterACollection: true);

        Assert.AreEqual(
            "private 180MB -> 151MB, managed 21MB -> 6MB, committed 44MB, large objects 15MB with 0MB unused",
            after.Since(before));
    }

    [TestMethod]
    public void Describe_SaysNothingAboutTheHeapBeforeTheFirstCollection()
    {
        // Everything GetGCMemoryInfo reports describes the last collection, so before
        // there has been one it is all zero. Printing "committed 0MB" would read as a
        // process holding nothing, which is the opposite of what it means.
        var fresh = new MemorySnapshot(66 * Mb, 15 * Mb, 0, 0, 0, AfterACollection: false);

        Assert.AreEqual("private 66MB, managed 15MB", fresh.Describe());
    }

    [TestMethod]
    public void Describe_KeepsAFractionOfAMegabyteVisible()
    {
        // A frame is 12.3MB at 2038x1588 and the swatch decode is under one, so
        // rounding to whole megabytes would report the cheap half of every fix as free.
        var small = new MemorySnapshot(12945400, 786432, 0, 0, 0, AfterACollection: false);

        Assert.AreEqual("private 12.3MB, managed 0.8MB", small.Describe());
    }

    [TestMethod]
    public void Take_AnswersWithAProcessThatHasAtLeastSomeMemory()
    {
        // The sampling half, which the formatting tests cannot reach: it must return
        // live numbers rather than a default struct, or every log line above would be
        // an elaborate way of writing zero.
        //
        // Managed bytes rather than private, because this suite also runs on macOS,
        // where PrivateMemorySize64 answers 0 — see the remarks on Take.
        var now = MemorySnapshot.Take();

        Assert.IsTrue(now.ManagedBytes > 0, "the running process has a managed heap, this test being on it");
    }

    [TestMethod]
    public void IsWorthCompacting_SaysYesToEveryHeapACaptureOrAStarvedRecordingLeaves()
    {
        // These are the two shapes that reach CollectWhenIdle on a real machine, and the
        // decision used to be declared by which of them called rather than measured. A
        // field log then showed a recording arriving lighter than a capture, so the
        // recording's opt-out cost it 21MB of large-object holes per run for nothing.
        var capture = new MemorySnapshot(0, 18 * Mb, 0, 0, 0, AfterACollection: true);
        var starvedRecording = new MemorySnapshot(0, 5 * Mb, 0, 0, 0, AfterACollection: true);

        Assert.IsTrue(capture.IsWorthCompacting);
        Assert.IsTrue(starvedRecording.IsWorthCompacting);
    }

    [TestMethod]
    public void IsWorthCompacting_SaysYesToARecordingTakenAfterAScreenshotInTheSameSession()
    {
        // The case that moved the ceiling. Measured on the VM, a region recording in a
        // session that had already taken a screenshot arrives here with 88-105MB alive,
        // an order of magnitude above a recording measured on its own — and the first
        // ceiling, set from that lighter run, would have declined the ordinary case.
        var afterAScreenshot = new MemorySnapshot(0, 105 * Mb, 0, 0, 0, AfterACollection: true);

        Assert.IsTrue(afterAScreenshot.IsWorthCompacting);
    }

    [TestMethod]
    public void IsWorthCompacting_SaysNoToTheHeapThatMadeCompactingRuinous()
    {
        // A full-screen recording that keeps its frames settled at 845-994MB without
        // compaction and 2691MB with it — the compaction climbing after the recording
        // had stopped, which is this decision running. Nothing above a few hundred
        // megabytes may ask for it again.
        var heavyRecording = new MemorySnapshot(0, 900 * Mb, 0, 0, 0, AfterACollection: true);

        Assert.IsFalse(heavyRecording.IsWorthCompacting);
    }

    [TestMethod]
    public void IsWorthCompacting_ReadsTheLiveFigureAndNotTheLastCollectionsLargeObjectHeap()
    {
        // The whole point of deciding from ManagedBytes: everything GetGCMemoryInfo
        // reports is the *previous* collection's, so on this path — collect now, having
        // just dropped a recording's worth of frames — the large-object figure is the
        // stale one. A snapshot whose two numbers disagree must follow the live one.
        var stale = new MemorySnapshot(0, 900 * Mb, 0, LargeObjectBytes: 12 * Mb, 0, AfterACollection: true);

        Assert.IsFalse(stale.IsWorthCompacting, "the 12MB is what the heap was at the last collection, not now");
    }

    private const long Mb = 1024 * 1024;
}
