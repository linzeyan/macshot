using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Tests.Capture;

/// <summary>
/// The arithmetic that carries a recording's sound through a re-timed export.
/// </summary>
[TestClass]
public sealed class AudioRetimeTests
{
    private const int Rate = 48_000;

    /// <summary>
    /// An untouched recording reads its own frames in order at one apiece. If this ever
    /// stopped holding, every export would be re-timed whether or not anything asked for
    /// it, and the common case is the one nobody would think to check.
    /// </summary>
    [TestMethod]
    public void Spans_LeaveAnUntouchedRecordingReadingItselfOneForOne()
    {
        var spans = AudioRetime.Spans(
            VideoTimeline.Pieces([new VideoTimeRange(0, 10)], [], []),
            Rate);

        Assert.AreEqual(1, spans.Count);
        Assert.AreEqual(0, spans[0].OutputFrame);
        Assert.AreEqual(0, spans[0].SourceFrame);
        Assert.AreEqual(10 * Rate, spans[0].Frames);
        Assert.AreEqual(1, spans[0].Rate, 1e-9);
    }

    /// <summary>
    /// Spans abut exactly — each one starts where the last ended, with no gap and no
    /// overlap. A gap is a click at every cut and an overlap is samples written twice;
    /// both are only audible, which is why the invariant is asserted rather than trusted.
    /// </summary>
    [TestMethod]
    public void Spans_LieEndToEndWithNoGapAndNoOverlap()
    {
        var pieces = VideoTimeline.Pieces(
            [new VideoTimeRange(0, 4), new VideoTimeRange(6, 10)],
            [new VideoSpeedSegment(1, 2.5, 3)],
            [new VideoFreezeSegment(7, 1.5)]);

        var spans = AudioRetime.Spans(pieces, Rate);

        Assert.IsTrue(spans.Count > 3, "this arrangement should produce several spans");

        for (var index = 1; index < spans.Count; index++)
        {
            Assert.AreEqual(
                spans[index - 1].OutputEnd,
                spans[index].OutputFrame,
                $"span {index} does not start where span {index - 1} ended");
        }

        Assert.AreEqual(spans[^1].OutputEnd, AudioRetime.TotalFrames(spans));
    }

    /// <summary>
    /// A sped-up stretch reads its source faster rather than going silent. This is the
    /// whole point of the file: macOS re-times the track with the picture, and an export
    /// that dropped the sound instead would be a different product on that stretch.
    /// </summary>
    [TestMethod]
    public void Spans_ReadASpedUpStretchFasterRatherThanSilencingIt()
    {
        var pieces = VideoTimeline.Pieces(
            [new VideoTimeRange(0, 6)],
            [new VideoSpeedSegment(2, 4, 2)],
            []);

        var spans = AudioRetime.Spans(pieces, Rate);
        var sped = spans.Single(span => Math.Abs(span.Rate - 2) < 1e-9);

        // Two source seconds delivered in one output second.
        Assert.AreEqual(Rate, sped.Frames);
        Assert.AreEqual(2 * Rate, sped.SourceFrame);
        Assert.IsFalse(sped.IsSilence);
    }

    /// <summary>
    /// A freeze is silence, because there is no source to read: it holds one instant, and
    /// the audio of one instant is nothing. macOS does the same, so this is the one
    /// temporal effect where both products agree the sound stops.
    /// </summary>
    [TestMethod]
    public void Spans_MakeAFreezeSilentBecauseItCoversNoSource()
    {
        var pieces = VideoTimeline.Pieces([new VideoTimeRange(0, 4)], [], [new VideoFreezeSegment(2, 1)]);
        var spans = AudioRetime.Spans(pieces, Rate);
        var held = spans.Single(span => span.IsSilence);

        Assert.AreEqual(Rate, held.Frames);
    }

    /// <summary>
    /// What follows a cut reads the source from after the cut while playing immediately
    /// after what preceded it. Getting this wrong is the failure that sounds like the
    /// audio is fine and the picture has drifted.
    /// </summary>
    [TestMethod]
    public void Spans_PullWhatFollowsACutForwardInTheOutputButNotInTheSource()
    {
        var kept = VideoCuts.KeptRanges(0, 10, [new VideoCutSegment(3, 5)]);
        var spans = AudioRetime.Spans(VideoTimeline.Pieces(kept, [], []), Rate);

        Assert.AreEqual(2, spans.Count);
        Assert.AreEqual(3 * Rate, spans[0].Frames);

        // Straight after the first in the export...
        Assert.AreEqual(3 * Rate, spans[1].OutputFrame);

        // ...but two seconds further into the recording.
        Assert.AreEqual(5 * Rate, spans[1].SourceFrame);
    }

    /// <summary>
    /// The mapping is computed from the span's start, so a fractional rate cannot
    /// accumulate error. A stepped cursor drifts by the rounding of every frame before it
    /// — at 1.7× over an hour that is a quarter of a second of slip against the picture,
    /// which arrives as lip-sync that is fine at the start and wrong at the end.
    /// </summary>
    [TestMethod]
    public void Read_DoesNotDriftAcrossAnHourAtAFractionalRate()
    {
        var frames = 3600L * Rate;
        var span = new AudioSpan(0, frames, 0, 1.7);
        var last = frames - 1;

        var expected = (long)(last * 1.7);
        var actual = AudioRetime.Read(span, last, long.MaxValue);

        Assert.AreEqual(expected, actual);

        // And the error against exact arithmetic is under one frame, not under one frame
        // per second elapsed.
        Assert.IsTrue(Math.Abs(actual - (last * 1.7)) < 1, $"drifted to {actual}");
    }

    /// <summary>
    /// A span reading past the end of the recording gets silence rather than the last
    /// frame repeated. A clamp would hold one frame for as long as the overrun lasts,
    /// which is a tone; silence is what a recording that has ended sounds like.
    /// </summary>
    [TestMethod]
    public void Read_AnswersSilenceRatherThanHoldingTheLastFramePastTheEnd()
    {
        var span = new AudioSpan(0, 100, 90, 1);

        Assert.AreEqual(95, AudioRetime.Read(span, 5, 96));
        Assert.AreEqual(-1, AudioRetime.Read(span, 6, 96));
    }

    /// <summary>
    /// A silent span answers silence for every frame in it, whatever it claims its source
    /// is. A freeze carries a source position so the picture knows what to hold, and an
    /// audio reader that used it would play the recording during the hold.
    /// </summary>
    [TestMethod]
    public void Read_AnswersSilenceThroughoutASpanWithNoRate()
    {
        var span = new AudioSpan(10, 50, 4_000, 0);

        Assert.AreEqual(-1, AudioRetime.Read(span, 10, long.MaxValue));
        Assert.AreEqual(-1, AudioRetime.Read(span, 59, long.MaxValue));
    }

    /// <summary>
    /// A frame outside the span belongs to a different span. The export walks spans in
    /// order and asks each only about its own frames, and a reader that answered for a
    /// neighbour's would hide a bug in that walk rather than surfacing it.
    /// </summary>
    [TestMethod]
    public void Read_RefusesAFrameThatBelongsToAnotherSpan()
    {
        var span = new AudioSpan(100, 10, 0, 1);

        Assert.AreEqual(-1, AudioRetime.Read(span, 99, long.MaxValue));
        Assert.AreEqual(-1, AudioRetime.Read(span, 110, long.MaxValue));
        Assert.AreEqual(5, AudioRetime.Read(span, 105, long.MaxValue));
    }
}
