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

    private const long Mb = 1024 * 1024;
}
