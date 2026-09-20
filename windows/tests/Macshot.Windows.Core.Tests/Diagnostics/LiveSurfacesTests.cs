using System.Runtime.CompilerServices;

using Macshot.Windows.Core.Diagnostics;

namespace Macshot.Windows.Core.Tests.Diagnostics;

[TestClass]
public sealed class LiveSurfacesTests
{
    [TestMethod]
    public void Census_CountsASurfaceNothingHoldsAsGone()
    {
        // The whole point: a surface the collector could take must stop being counted, or
        // the census says every capture leaked and nobody would read it twice.
        var surfaces = new LiveSurfaces();
        Add(surfaces, "overlay");
        Collect();

        Assert.AreEqual("overlay 0/1", surfaces.Census());
    }

    [TestMethod]
    public void Census_KeepsCountingASurfaceSomethingStillHolds()
    {
        // And the other half, which is the one that finds a leak: a timer's handler, a
        // static, an event nobody unsubscribed. Held here by a local, which is the same
        // shape from the collector's point of view.
        var surfaces = new LiveSurfaces();
        var held = new object();
        surfaces.Watch("overlay", held);
        Add(surfaces, "overlay");
        Collect();

        Assert.AreEqual("overlay 1/2", surfaces.Census());
        GC.KeepAlive(held);
    }

    [TestMethod]
    public void Census_NamesEveryKindInTheSameOrderEveryTime()
    {
        // Two censuses from one session get read against each other, and a column that
        // moves between them is a column nobody can compare.
        var surfaces = new LiveSurfaces();
        var overlay = new object();
        var thumbnail = new object();
        surfaces.Watch("thumbnail", thumbnail);
        surfaces.Watch("overlay", overlay);

        Assert.AreEqual("overlay 1/1, thumbnail 1/1", surfaces.Census());
        GC.KeepAlive(overlay);
        GC.KeepAlive(thumbnail);
    }

    [TestMethod]
    public void Watch_DoesNotItselfKeepASurfaceAlive()
    {
        // A diagnostic that causes the leak it reports would be worse than none, and a
        // strong reference here is the obvious way to write it by accident.
        var surfaces = new LiveSurfaces();
        var reference = Add(surfaces, "overlay");
        Collect();

        Assert.IsFalse(reference.IsAlive);
    }

    /// <summary>
    /// Kept out of the caller, and out of the caller's frame: a Debug build reports no
    /// local as dead before its method returns, so a surface made inline in a test is
    /// still rooted when the collection runs and every expectation here inverts.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Add(LiveSurfaces surfaces, string kind)
    {
        var surface = new object();
        surfaces.Watch(kind, surface);
        return new WeakReference(surface);
    }

    private static void Collect()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
    }
}
