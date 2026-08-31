using SceneForge.Media.Domain;
using SceneForge.Media.Extraction;
using SceneForge.Media.Extraction.Scoring;
using SceneForge.Media.Extraction.Signals;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Extraction.Scoring;

public class ClipScorerTests
{
    private static readonly TimeRange Candidate = new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(14));
    private static readonly CleanClipScoringOptions Options = CleanClipScoringOptions.Default;
    private static readonly TimeSpan FarFromAnyExclusion = TimeSpan.FromSeconds(30);

    private static List<ClipFrameMetrics> GoodFrames(int count = 8) =>
        Enumerable.Range(0, count).Select(i => ClipFrameMetricsBuilder.Sample(10 + (i * 0.5))).ToList();

    [Fact]
    public void Score_AllFactorsWithinBounds_AcceptsCandidate()
    {
        var score = ClipScorer.Score(Candidate, GoodFrames(), FarFromAnyExclusion, Options);

        Assert.True(score.Accepted);
        Assert.All(score.Reasons, r => Assert.True(r.Passed));
        Assert.All(score.Reasons, r => Assert.Null(r.Code));
        Assert.Equal(8, score.Reasons.Count);
    }

    [Fact]
    public void Score_LowSharpness_RejectsWithInsufficientSharpness()
    {
        var frames = Enumerable.Range(0, 8).Select(i => ClipFrameMetricsBuilder.Sample(10 + (i * 0.5), sharpness: 1)).ToList();

        var score = ClipScorer.Score(Candidate, frames, FarFromAnyExclusion, Options);

        Assert.False(score.Accepted);
        var reason = score.Reasons.Single(r => r.Factor == "Sharpness");
        Assert.False(reason.Passed);
        Assert.Equal(RejectionReason.InsufficientSharpness, reason.Code);
    }

    [Fact]
    public void Score_HighStructuralDifference_RejectsWithUnstableMotion()
    {
        var frames = Enumerable.Range(0, 8)
            .Select(i => ClipFrameMetricsBuilder.Sample(10 + (i * 0.5), structuralDifferenceFromPrevious: 0.5))
            .ToList();

        var score = ClipScorer.Score(Candidate, frames, FarFromAnyExclusion, Options);

        Assert.False(score.Accepted);
        var reason = score.Reasons.Single(r => r.Factor == "Stability");
        Assert.False(reason.Passed);
        Assert.Equal(RejectionReason.UnstableMotion, reason.Code);
    }

    [Fact]
    public void Score_TooDarkExposure_RejectsWithPoorExposure()
    {
        var frames = Enumerable.Range(0, 8)
            .Select(i => ClipFrameMetricsBuilder.Sample(10 + (i * 0.5), meanLuminance: 0.02, blackScore: 0.95))
            .ToList();

        var score = ClipScorer.Score(Candidate, frames, FarFromAnyExclusion, Options);

        Assert.False(score.Accepted);
        var reason = score.Reasons.Single(r => r.Factor == "Exposure");
        Assert.False(reason.Passed);
        Assert.Equal(RejectionReason.PoorExposure, reason.Code);
    }

    [Fact]
    public void Score_MostlyNearIdenticalFrames_RejectsWithHighFreezeRisk()
    {
        var frames = Enumerable.Range(0, 8)
            .Select(i => ClipFrameMetricsBuilder.Sample(10 + (i * 0.5), structuralDifferenceFromPrevious: 0.0001))
            .ToList();

        var score = ClipScorer.Score(Candidate, frames, FarFromAnyExclusion, Options);

        Assert.False(score.Accepted);
        var reason = score.Reasons.Single(r => r.Factor == "FreezeRisk");
        Assert.False(reason.Passed);
        Assert.Equal(RejectionReason.HighFreezeRisk, reason.Code);
        Assert.Equal(1.0, score.FreezeRisk);
    }

    [Fact]
    public void Score_TouchingAnExclusion_RejectsWithTooCloseToExclusion()
    {
        var score = ClipScorer.Score(Candidate, GoodFrames(), TimeSpan.Zero, Options);

        Assert.False(score.Accepted);
        var reason = score.Reasons.Single(r => r.Factor == "TransitionDistance");
        Assert.False(reason.Passed);
        Assert.Equal(RejectionReason.TooCloseToExclusion, reason.Code);
    }

    // Regression coverage for docs/CLEAN_CLIP_RETENTION_AUDIT.md: a
    // candidate sitting exactly BoundaryGuard (250ms, CleanClipExtractionOptions'
    // own default) away from the nearest excluded interval - i.e. the
    // ClipCandidateGenerator geometry every single-candidate remaining
    // range produces, the common case for any scene not much longer than
    // MaxClipDuration - must PASS TransitionDistance under the current
    // defaults. Under the OLD defaults (TransitionSafeDistance=2s), this
    // same 250ms distance scored 0.125, well under MinAcceptableFactorScore
    // (0.3), so it was rejected regardless of footage quality; verified
    // directly against real ffmpeg that this was the actual mechanism
    // discarding the overwhelming majority of scenes shorter than ~8.5s in
    // a real multi-transition source (docs/CLEAN_CLIP_RETENTION_AUDIT.md).
    [Fact]
    public void Score_ExactlyBoundaryGuardDistanceFromExclusion_PassesTransitionDistanceUnderCurrentDefaults()
    {
        var boundaryGuardDistance = CleanClipScoringOptions.Default.BoundaryGuard;

        var score = ClipScorer.Score(Candidate, GoodFrames(), boundaryGuardDistance, Options);

        var reason = score.Reasons.Single(r => r.Factor == "TransitionDistance");
        Assert.True(reason.Passed, $"expected a candidate {boundaryGuardDistance.TotalMilliseconds}ms from the nearest exclusion to pass TransitionDistance under current defaults - {reason.Detail}");
        Assert.True(score.Accepted);
    }

    [Fact]
    public void Score_ExactlyBoundaryGuardDistanceFromExclusion_WouldHaveFailedUnderThePreviousDefaults()
    {
        var boundaryGuardDistance = CleanClipScoringOptions.Default.BoundaryGuard;
        var previousDefaults = Options with { MinClipDuration = TimeSpan.FromSeconds(3), TransitionSafeDistance = TimeSpan.FromSeconds(2) };

        var score = ClipScorer.Score(Candidate, GoodFrames(), boundaryGuardDistance, previousDefaults);

        var reason = score.Reasons.Single(r => r.Factor == "TransitionDistance");
        Assert.False(reason.Passed);
        Assert.Equal(RejectionReason.TooCloseToExclusion, reason.Code);
        Assert.False(score.Accepted);
    }

    [Fact]
    public void Score_BorderEdgesDominateInterior_RejectsWithSuspectedOverlay()
    {
        var frames = Enumerable.Range(0, 8)
            .Select(i => ClipFrameMetricsBuilder.Sample(10 + (i * 0.5), borderEdgeDensity: 0.6, interiorEdgeDensity: 0.02))
            .ToList();

        var score = ClipScorer.Score(Candidate, frames, FarFromAnyExclusion, Options);

        Assert.False(score.Accepted);
        var reason = score.Reasons.Single(r => r.Factor == "OverlaySuspicion");
        Assert.False(reason.Passed);
        Assert.Equal(RejectionReason.SuspectedOverlay, reason.Code);
    }

    [Fact]
    public void Score_DurationBelowConfiguredMinimum_RejectsWithDurationOutOfRange()
    {
        var tooShort = new TimeRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10.5));

        var score = ClipScorer.Score(tooShort, GoodFrames(), FarFromAnyExclusion, Options);

        Assert.False(score.Accepted);
        var reason = score.Reasons.Single(r => r.Factor == "Duration");
        Assert.False(reason.Passed);
        Assert.Equal(RejectionReason.DurationOutOfRange, reason.Code);
    }

    [Fact]
    public void Score_NoFramesInWindow_RejectsRatherThanThrowing()
    {
        var score = ClipScorer.Score(Candidate, [], FarFromAnyExclusion, Options);

        Assert.False(score.Accepted);
        Assert.Equal(1.0, score.FreezeRisk);
    }

    [Fact]
    public void Score_OverallBelowThresholdDespiteEveryIndividualFactorPassing_RejectsWithLowOverallScore()
    {
        // Every individual factor is nudged just above its own pass
        // threshold but not by much, so the weighted Overall average still
        // falls under AcceptanceThreshold.
        var options = Options with { AcceptanceThreshold = 0.95 };
        var score = ClipScorer.Score(Candidate, GoodFrames(), FarFromAnyExclusion, options);

        Assert.False(score.Accepted);
        var reason = score.Reasons.Single(r => r.Factor == "Overall");
        Assert.False(reason.Passed);
        Assert.Equal(RejectionReason.LowOverallScore, reason.Code);
    }

    [Fact]
    public void Score_SameInputsTwice_ProducesIdenticalScores()
    {
        var frames = GoodFrames();

        var first = ClipScorer.Score(Candidate, frames, FarFromAnyExclusion, Options);
        var second = ClipScorer.Score(Candidate, frames, FarFromAnyExclusion, Options);

        Assert.Equal(first.Duration, second.Duration);
        Assert.Equal(first.Sharpness, second.Sharpness);
        Assert.Equal(first.Stability, second.Stability);
        Assert.Equal(first.Exposure, second.Exposure);
        Assert.Equal(first.FreezeRisk, second.FreezeRisk);
        Assert.Equal(first.TransitionDistance, second.TransitionDistance);
        Assert.Equal(first.OverlaySuspicion, second.OverlaySuspicion);
        Assert.Equal(first.Overall, second.Overall);
        Assert.Equal(first.Accepted, second.Accepted);
        Assert.Equal(
            first.Reasons.Select(r => (r.Factor, r.Passed, r.Code, r.Detail)),
            second.Reasons.Select(r => (r.Factor, r.Passed, r.Code, r.Detail)));
    }
}
