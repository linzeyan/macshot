using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Annotations;

/// <summary>
/// The curve a mark bent through anchors is drawn along. Everything downstream — what is
/// stroked, what can be grabbed, what a ruler reports — is this same polyline, so the
/// properties pinned here are the ones all three share.
/// </summary>
[TestClass]
public sealed class SmoothPathTests
{
    [TestMethod]
    public void Through_LeavesTwoPointsAsTheStraightLineTheyAlreadyAre()
    {
        // A spline fitted to two points would round off ends nobody bent, and the same
        // call answers for every unbent line, arrow and ruler in the document.
        var path = SmoothPath.Through([new CapturePoint(0, 0), new CapturePoint(40, 20)]);

        CollectionAssert.AreEqual(
            new[] { new CapturePoint(0, 0), new CapturePoint(40, 20) },
            path);
    }

    [TestMethod]
    public void Through_PassesExactlyThroughEveryAnchorItWasGiven()
    {
        // The whole promise of an anchor is that the mark goes where it was put. A curve
        // that merely approached them would leave the grip sitting off the line it belongs
        // to, and dragging it would not put the mark under the pointer.
        CapturePoint[] anchors =
        [
            new CapturePoint(0, 0),
            new CapturePoint(20, 40),
            new CapturePoint(60, 10),
            new CapturePoint(80, 50),
        ];

        var path = SmoothPath.Through(anchors);

        foreach (var anchor in anchors)
        {
            Assert.IsTrue(
                path.Any(sample => Math.Abs(sample.X - anchor.X) < 1e-9 && Math.Abs(sample.Y - anchor.Y) < 1e-9),
                $"the curve must reach {anchor}");
        }
    }

    [TestMethod]
    public void Through_KeepsTheAnchorsInTheOrderTheyWereGiven()
    {
        // The chain is a route, not a set: reordering it would swap which span a new
        // anchor is inserted into and fold the mark back over itself.
        var path = SmoothPath.Through(
            [new CapturePoint(0, 0), new CapturePoint(20, 40), new CapturePoint(60, 10)]);

        var firstAnchor = Array.FindIndex(path, sample => sample == new CapturePoint(20, 40));
        var lastAnchor = Array.FindIndex(path, sample => sample == new CapturePoint(60, 10));

        Assert.IsTrue(firstAnchor > 0 && firstAnchor < lastAnchor);
        Assert.AreEqual(path.Length - 1, lastAnchor, "the chain must end at its last anchor");
    }

    [TestMethod]
    public void Through_CurvesRatherThanElbowsThroughTheAnchorItPasses()
    {
        // A jointed polyline would put every sample of the first span exactly on the chord
        // to the anchor. The tool being ported bends smoothly through its waypoints, and a
        // version that turned corners at them would read as a different tool.
        var path = SmoothPath.Through(
            [new CapturePoint(0, 0), new CapturePoint(50, 0), new CapturePoint(100, 50)]);

        // Everything on the first span is on the line y = 0 unless the curve is bowed by
        // the anchor beyond it.
        Assert.IsTrue(
            path.Any(sample => sample.X is > 0 and < 50 && Math.Abs(sample.Y) > 0.5),
            "the first span must lean towards the anchor after it");
    }

    [TestMethod]
    public void Length_IsLongerThanTheStraightLineBetweenTheEnds()
    {
        // What the ruler reports. A bent rule that still gave the chord would be writing a
        // number on the capture that does not describe the line drawn beside it.
        var chain = new[]
        {
            new CapturePoint(0, 0),
            new CapturePoint(50, 40),
            new CapturePoint(100, 0),
        };

        Assert.IsTrue(SmoothPath.Length(chain) > 100 + 1);
    }

    [TestMethod]
    public void Length_IsTheStraightDistanceWhenNothingIsBent()
    {
        // The unbent case has to stay exact: every ruler drawn without an anchor reports
        // through here, and a sampled approximation would round its reading.
        Assert.AreEqual(
            50,
            SmoothPath.Length([new CapturePoint(10, 10), new CapturePoint(40, 50)]),
            1e-9);
    }
}
