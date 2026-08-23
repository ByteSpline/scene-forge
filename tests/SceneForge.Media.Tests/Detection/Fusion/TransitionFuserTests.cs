using SceneForge.Media.Detection;
using SceneForge.Media.Detection.Classification;
using SceneForge.Media.Detection.Fusion;

namespace SceneForge.Media.Tests.Detection.Fusion;

public class TransitionFuserTests
{
    private readonly TransitionDetectionProfile _profile = TransitionDetectionProfiles.GetDefaults(TransitionDetectionProfileVersion.V1) with
    {
        MergeGapTolerance = TimeSpan.FromMilliseconds(200),
        PreBufferDuration = TimeSpan.FromMilliseconds(100),
        PostBufferDuration = TimeSpan.FromMilliseconds(100),
    };

    private static TransitionCandidate Candidate(
        TransitionType type,
        double start,
        double peak,
        double end,
        double confidence = 0.8,
        string reason = "test reason") => new()
        {
            Type = type,
            Start = TimeSpan.FromSeconds(start),
            Peak = TimeSpan.FromSeconds(peak),
            End = TimeSpan.FromSeconds(end),
            Confidence = confidence,
            ContributingSignals = new Dictionary<string, double> { ["Signal"] = confidence },
            DiagnosticReason = reason,
        };

    [Fact]
    public void Fuse_NoCandidates_ReturnsEmpty()
    {
        var result = TransitionFuser.Fuse([], _profile);

        Assert.Empty(result);
    }

    [Fact]
    public void Fuse_SingleCandidate_AppliesBuffersAndKeepsBoundaryWithinInterval()
    {
        var candidate = Candidate(TransitionType.Dissolve, 5.0, 5.5, 6.0);

        var result = TransitionFuser.Fuse([candidate], _profile);

        var detection = Assert.Single(result);
        Assert.Equal(TimeSpan.FromSeconds(4.9), detection.Start);
        Assert.Equal(TimeSpan.FromSeconds(6.1), detection.End);
        Assert.Equal(TimeSpan.FromSeconds(5.5), detection.BoundaryTimestamp);
        Assert.True(detection.Start < detection.End);
        Assert.InRange(detection.BoundaryTimestamp, detection.Start, detection.End);
    }

    [Fact]
    public void Fuse_OverlappingCandidates_MergeIntoOneDetection()
    {
        var candidateA = Candidate(TransitionType.Dissolve, 5.0, 5.5, 6.0, confidence: 0.6);
        var candidateB = Candidate(TransitionType.Dissolve, 5.8, 6.2, 6.5, confidence: 0.9);

        var result = TransitionFuser.Fuse([candidateA, candidateB], _profile);

        var detection = Assert.Single(result);
        Assert.Equal(0.9, detection.Confidence);
    }

    [Fact]
    public void Fuse_CandidatesWithinMergeGapTolerance_MergeIntoOneDetection()
    {
        // candidateB starts 0.15s after candidateA ends - within the 0.2s tolerance.
        var candidateA = Candidate(TransitionType.HardCut, 5.0, 5.1, 5.2, confidence: 0.7);
        var candidateB = Candidate(TransitionType.HardCut, 5.35, 5.45, 5.5, confidence: 0.7);

        var result = TransitionFuser.Fuse([candidateA, candidateB], _profile);

        Assert.Single(result);
    }

    [Fact]
    public void Fuse_CandidatesBeyondMergeGapTolerance_RemainSeparateDetections()
    {
        var candidateA = Candidate(TransitionType.HardCut, 5.0, 5.1, 5.2, confidence: 0.7);
        var candidateB = Candidate(TransitionType.HardCut, 10.0, 10.1, 10.2, confidence: 0.7);

        var result = TransitionFuser.Fuse([candidateA, candidateB], _profile);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Fuse_ExactDuplicateCandidates_DedupesWithNoMergeNoteInReason()
    {
        var candidate = Candidate(TransitionType.HardCut, 5.0, 5.1, 5.2, reason: "original reason");
        var duplicate = candidate with { }; // value-equal record copy

        var result = TransitionFuser.Fuse([candidate, duplicate], _profile);

        var detection = Assert.Single(result);
        Assert.Equal("original reason", detection.DiagnosticReason);
        Assert.DoesNotContain("Merged with", detection.DiagnosticReason);
    }

    [Fact]
    public void Fuse_DifferentTypeOverlappingCandidates_WinnerTypeUsedAndLoserFoldedIntoReason()
    {
        var strongDissolve = Candidate(TransitionType.Dissolve, 5.0, 5.5, 6.0, confidence: 0.9, reason: "dissolve reason");
        var weakZoom = Candidate(TransitionType.ZoomTransition, 5.1, 5.4, 5.9, confidence: 0.4, reason: "zoom reason");

        var result = TransitionFuser.Fuse([strongDissolve, weakZoom], _profile);

        var detection = Assert.Single(result);
        Assert.Equal(TransitionType.Dissolve, detection.Type);
        Assert.Contains("Merged with", detection.DiagnosticReason);
        Assert.Contains("zoom reason", detection.DiagnosticReason);
        Assert.Contains("ZoomTransition.Signal", detection.ContributingSignals.Keys);
    }

    [Fact]
    public void Fuse_StartNearTimelineOrigin_PreBufferClampsToZero()
    {
        var candidate = Candidate(TransitionType.HardCut, 0.05, 0.1, 0.15);

        var result = TransitionFuser.Fuse([candidate], _profile);

        Assert.Equal(TimeSpan.Zero, Assert.Single(result).Start);
    }

    [Fact]
    public void Fuse_EndNearVideoDuration_PostBufferClampsToVideoDuration()
    {
        var videoDuration = TimeSpan.FromSeconds(10.0);
        var candidate = Candidate(TransitionType.HardCut, 9.85, 9.9, 9.95);

        var result = TransitionFuser.Fuse([candidate], _profile, videoDuration);

        Assert.Equal(videoDuration, Assert.Single(result).End);
    }
}
