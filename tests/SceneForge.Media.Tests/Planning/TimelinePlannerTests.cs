using SceneForge.Media.Domain;
using SceneForge.Media.Extraction;
using SceneForge.Media.Planning;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Planning;

public class TimelinePlannerTests
{
    private static readonly RationalFrameRate TwentyFiveFps = new(25, 1);

    private readonly TimelinePlanner _planner = new();

    private static TimelinePlanRequest CreateRequest(
        IReadOnlyList<CleanClip> clips,
        double targetSeconds,
        int seed = 1,
        int minimumRepeatDistance = 1,
        int maximumReuseCount = 1,
        int originalNeighborSeparation = 1,
        int visualClusterAdjacencyLimit = 1,
        TimelineDurationBounds? bounds = null,
        RationalFrameRate? outputTimeBase = null) => new()
        {
            AvailableClips = clips,
            TargetAudioDuration = TimeSpan.FromSeconds(targetSeconds),
            OutputTimeBase = outputTimeBase ?? TwentyFiveFps,
            Seed = seed,
            MinimumRepeatDistance = minimumRepeatDistance,
            MaximumReuseCount = maximumReuseCount,
            OriginalNeighborSeparation = originalNeighborSeparation,
            VisualClusterAdjacencyLimit = visualClusterAdjacencyLimit,
            DurationBounds = bounds ?? TimelineDurationBounds.Default,
        };

    [Fact]
    public void Plan_NullRequest_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _planner.Plan(null!));
    }

    [Fact]
    public void Plan_NegativeTargetDuration_Throws()
    {
        var request = CreateRequest([CleanClipBuilder.Create(0, 5)], targetSeconds: 5) with
        {
            TargetAudioDuration = TimeSpan.FromSeconds(-1),
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => _planner.Plan(request));
    }

    [Fact]
    public void Plan_UndefinedOutputTimeBase_Throws()
    {
        var request = CreateRequest([CleanClipBuilder.Create(0, 5)], targetSeconds: 5, outputTimeBase: RationalFrameRate.Undefined);

        Assert.Throws<ArgumentException>(() => _planner.Plan(request));
    }

    [Fact]
    public void Plan_ZeroTargetDuration_ReturnsEmptyCompletePlan()
    {
        var request = CreateRequest([CleanClipBuilder.Create(0, 5)], targetSeconds: 0);

        var plan = _planner.Plan(request);

        Assert.Empty(plan.Placements);
        Assert.True(plan.IsComplete);
        Assert.Null(plan.FeasibilityWarning);
        Assert.Equal(TimeSpan.Zero, plan.PlannedDuration);
    }

    [Fact]
    public void Plan_EmptyClipPool_PositiveTarget_ReturnsFeasibilityWarning()
    {
        var request = CreateRequest([], targetSeconds: 5);

        var plan = _planner.Plan(request);

        Assert.Empty(plan.Placements);
        Assert.False(plan.IsComplete);
        Assert.NotNull(plan.FeasibilityWarning);
        Assert.Equal(plan.QuantizedTargetDuration, plan.FeasibilityWarning!.Shortfall);
        Assert.Equal(TimeSpan.Zero, plan.FeasibilityWarning.AchievedDuration);
    }

    [Fact]
    public void Plan_SingleClipExactlyMatchesTarget_NoTrim()
    {
        var clip = CleanClipBuilder.Create(startSeconds: 10, durationSeconds: 5);
        var request = CreateRequest([clip], targetSeconds: 5);

        var plan = _planner.Plan(request);

        var placement = Assert.Single(plan.Placements);
        Assert.False(placement.IsTrimmed);
        Assert.Equal(TimeSpan.FromSeconds(5), placement.UsedDuration);
        Assert.True(plan.IsComplete);
        Assert.Null(plan.FeasibilityWarning);
    }

    [Fact]
    public void Plan_TargetShorterThanSingleClip_TrimsIt()
    {
        var clip = CleanClipBuilder.Create(startSeconds: 0, durationSeconds: 5);
        var request = CreateRequest([clip], targetSeconds: 2);

        var plan = _planner.Plan(request);

        var placement = Assert.Single(plan.Placements);
        Assert.True(placement.IsTrimmed);
        Assert.Equal(TimeSpan.FromSeconds(2), placement.UsedDuration);
        Assert.True(plan.IsComplete);
    }

    [Fact]
    public void Plan_InsufficientFootage_RelaxesMaximumReuseCount_AndStillReachesTargetExactly()
    {
        // Two 3s clips can cover only 6s at MaximumReuseCount = 1 - the
        // scenario that used to leave the plan permanently 4s short (see
        // docs/PHASE_08_REPORT.md). The hard product requirement is that the
        // output must never be shorter than requested when relaxation can
        // close the gap, so the planner must now keep going instead of
        // stopping at the originally requested cap.
        var clips = new[]
        {
            CleanClipBuilder.Create(0, 3, sourceSceneIndex: 0),
            CleanClipBuilder.Create(10, 3, sourceSceneIndex: 1),
        };
        var request = CreateRequest(clips, targetSeconds: 10, maximumReuseCount: 1, minimumRepeatDistance: 0, originalNeighborSeparation: 0, visualClusterAdjacencyLimit: 0);

        var plan = _planner.Plan(request);

        Assert.True(plan.IsComplete);
        Assert.Equal(TimeSpan.FromSeconds(10), plan.PlannedDuration);
        TimelinePlanAssertions.AssertMaximumReuseCountRespectedOrRelaxed(plan, request.MaximumReuseCount);

        Assert.NotNull(plan.FeasibilityWarning);
        Assert.Equal(TimelineFeasibilityWarningKind.SignificantRepetition, plan.FeasibilityWarning!.Kind);
        Assert.Equal(TimeSpan.Zero, plan.FeasibilityWarning.Shortfall);
        Assert.Contains(plan.Placements.Select(p => p.ClipIndex).GroupBy(c => c), g => g.Count() > request.MaximumReuseCount);
        Assert.Contains(plan.DecisionTrace, t => t.RelaxedConstraints.Contains(RelaxedConstraint.MaximumReuseCount));
    }

    [Fact]
    public void Plan_ZeroDurationOnlyPool_CannotBeRelaxedIntoReachingTarget_ReportsShortfall()
    {
        // No amount of reuse-count relaxation, spacing relaxation, or
        // repetition can manufacture duration from a pool that has none -
        // the one case ComputeGuaranteedSufficientReuseCap deliberately
        // leaves alone, and IsComplete's one legitimate false outcome.
        var clips = new[]
        {
            CleanClipBuilder.Create(0, 0, sourceSceneIndex: 0),
            CleanClipBuilder.Create(5, 0, sourceSceneIndex: 1),
        };
        var request = CreateRequest(clips, targetSeconds: 10, maximumReuseCount: 1, minimumRepeatDistance: 0, originalNeighborSeparation: 0, visualClusterAdjacencyLimit: 0);

        var plan = _planner.Plan(request);

        // Zero-duration clips can still be placed (they just never advance
        // the budget, the same "never hangs, never regresses remaining"
        // behavior TimelinePlannerPropertyTests already covers), but they
        // can never sum to anything - the plan stays incomplete regardless.
        Assert.False(plan.IsComplete);
        Assert.Equal(TimeSpan.Zero, plan.PlannedDuration);
        Assert.NotNull(plan.FeasibilityWarning);
        Assert.Equal(TimelineFeasibilityWarningKind.Shortfall, plan.FeasibilityWarning!.Kind);
        Assert.Equal(plan.QuantizedTargetDuration, plan.FeasibilityWarning.Shortfall);
        Assert.Equal(TimeSpan.Zero, plan.FeasibilityWarning.AchievedDuration);
    }

    [Fact]
    public void Plan_SameSeedAndInputs_ProducesIdenticalPlan()
    {
        var clips = Enumerable.Range(0, 12)
            .Select(i => CleanClipBuilder.Create(i * 10, 3 + (i % 3), sourceSceneIndex: i % 4, clusterId: i % 5))
            .ToList();
        var request = CreateRequest(clips, targetSeconds: 30, seed: 987, maximumReuseCount: 3, minimumRepeatDistance: 2, originalNeighborSeparation: 1, visualClusterAdjacencyLimit: 1);

        var first = _planner.Plan(request);
        var second = _planner.Plan(request);

        Assert.Equal(first.Placements, second.Placements);
        Assert.Equal(first.DecisionTrace.Select(t => t.RelaxedConstraints), second.DecisionTrace.Select(t => t.RelaxedConstraints));
        Assert.Equal(first.PlannedDuration, second.PlannedDuration);
        Assert.Equal(first.IsComplete, second.IsComplete);
    }

    [Fact]
    public void Plan_UsesEveryUniqueClipBeforeReusingAny_WhenPracticalAndConstraintsAllow()
    {
        var clips = Enumerable.Range(0, 5)
            .Select(i => CleanClipBuilder.Create(i * 10, 2, sourceSceneIndex: i))
            .ToList();
        // 5 unique clips at 2s each = 10s; ask for 14s so reuse is required afterward.
        var request = CreateRequest(clips, targetSeconds: 14, maximumReuseCount: 3, minimumRepeatDistance: 0, originalNeighborSeparation: 0, visualClusterAdjacencyLimit: 0);

        var plan = _planner.Plan(request);

        var firstFiveClipIndices = plan.Placements.Take(5).Select(p => p.ClipIndex).ToHashSet();
        Assert.Equal(5, firstFiveClipIndices.Count);
        Assert.True(plan.IsComplete);
    }

    [Fact]
    public void Plan_PrefersLeastUsedClipWhenReuseIsNecessary()
    {
        var clips = new[]
        {
            CleanClipBuilder.Create(0, 1, sourceSceneIndex: 0),
            CleanClipBuilder.Create(10, 1, sourceSceneIndex: 1),
        };
        var request = CreateRequest(clips, targetSeconds: 6, maximumReuseCount: 10, minimumRepeatDistance: 0, originalNeighborSeparation: 0, visualClusterAdjacencyLimit: 0);

        var plan = _planner.Plan(request);

        var usageCounts = plan.Placements.GroupBy(p => p.ClipIndex).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(2, usageCounts.Count);
        // Balanced reuse: with 2 clips and 6 placements, each is used exactly 3 times -
        // never one clip 5 times while the other sits at 1, since least-used is always preferred.
        Assert.All(usageCounts.Values, count => Assert.Equal(3, count));
    }

    [Fact]
    public void Plan_RespectsMinimumRepeatDistance_WhenFeasible()
    {
        var clips = Enumerable.Range(0, 3)
            .Select(i => CleanClipBuilder.Create(i * 10, 1, sourceSceneIndex: i))
            .ToList();
        var request = CreateRequest(clips, targetSeconds: 6, maximumReuseCount: 5, minimumRepeatDistance: 2, originalNeighborSeparation: 0, visualClusterAdjacencyLimit: 0);

        var plan = _planner.Plan(request);

        TimelinePlanAssertions.AssertMinimumRepeatDistanceRespectedOrRelaxed(plan, request.MinimumRepeatDistance);
        TimelinePlanAssertions.AssertDurationInvariants(plan);
    }

    [Fact]
    public void Plan_RespectsOriginalNeighborSeparation_ForSameSourceScene()
    {
        var clips = new[]
        {
            CleanClipBuilder.Create(0, 1, sourceSceneIndex: 0),
            CleanClipBuilder.Create(5, 1, sourceSceneIndex: 0),
            CleanClipBuilder.Create(10, 1, sourceSceneIndex: 1),
            CleanClipBuilder.Create(15, 1, sourceSceneIndex: 1),
        };
        var request = CreateRequest(clips, targetSeconds: 4, maximumReuseCount: 1, minimumRepeatDistance: 0, originalNeighborSeparation: 1, visualClusterAdjacencyLimit: 0);

        var plan = _planner.Plan(request);

        TimelinePlanAssertions.AssertOriginalNeighborSeparationRespectedOrRelaxed(plan, request.OriginalNeighborSeparation);
    }

    [Fact]
    public void Plan_RespectsVisualClusterAdjacencyLimit()
    {
        var clips = new[]
        {
            CleanClipBuilder.Create(0, 1, sourceSceneIndex: 0, clusterId: 0),
            CleanClipBuilder.Create(5, 1, sourceSceneIndex: 1, clusterId: 0),
            CleanClipBuilder.Create(10, 1, sourceSceneIndex: 2, clusterId: 1),
            CleanClipBuilder.Create(15, 1, sourceSceneIndex: 3, clusterId: 1),
        };
        var request = CreateRequest(clips, targetSeconds: 4, maximumReuseCount: 1, minimumRepeatDistance: 0, originalNeighborSeparation: 0, visualClusterAdjacencyLimit: 1);

        var plan = _planner.Plan(request);

        TimelinePlanAssertions.AssertVisualClusterAdjacencyLimitRespectedOrRelaxed(plan, request.VisualClusterAdjacencyLimit);
    }

    [Fact]
    public void Plan_WhenAllClipsShareOneScene_MustRelaxOriginalNeighborSeparation_AndRecordsIt()
    {
        var clips = Enumerable.Range(0, 3)
            .Select(i => CleanClipBuilder.Create(i * 2, 1, sourceSceneIndex: 0))
            .ToList();
        var request = CreateRequest(clips, targetSeconds: 4, maximumReuseCount: 4, minimumRepeatDistance: 0, originalNeighborSeparation: 2, visualClusterAdjacencyLimit: 0);

        var plan = _planner.Plan(request);

        Assert.True(plan.IsComplete);
        Assert.Contains(plan.DecisionTrace, t => t.RelaxedConstraints.Contains(RelaxedConstraint.OriginalNeighborSeparation));
        TimelinePlanAssertions.AssertOriginalNeighborSeparationRespectedOrRelaxed(plan, request.OriginalNeighborSeparation);
    }

    [Fact]
    public void Plan_SingleClipMustReuseImmediately_RelaxesAllThreePlacementConstraints()
    {
        // A single clip, alone in its own scene and cluster: MinimumRepeatDistance
        // alone already blocks every tier below full relaxation (it is the same
        // physical clip every time), so the very first reuse jumps straight from
        // full strictness to relaxing all three constraints at once.
        var clip = CleanClipBuilder.Create(0, 1, sourceSceneIndex: 0, clusterId: 0);
        var request = CreateRequest([clip], targetSeconds: 3, maximumReuseCount: 5, minimumRepeatDistance: 5, originalNeighborSeparation: 5, visualClusterAdjacencyLimit: 5);

        var plan = _planner.Plan(request);

        Assert.True(plan.IsComplete);
        Assert.Equal(3, plan.Placements.Count);
        Assert.Empty(plan.DecisionTrace[0].RelaxedConstraints);

        var second = plan.DecisionTrace[1].RelaxedConstraints;
        Assert.Equal(
            [RelaxedConstraint.VisualClusterAdjacencyLimit, RelaxedConstraint.OriginalNeighborSeparation, RelaxedConstraint.MinimumRepeatDistance],
            second);
    }

    [Fact]
    public void Plan_ClusterAdjacencyAloneCanBlock_WithoutForcingSceneOrRepeatRelaxation()
    {
        // Three clips, each its own scene (so OriginalNeighborSeparation never
        // applies) and each usable only once (so MinimumRepeatDistance never
        // applies either), but all sharing one visual cluster. The only
        // possible blocker at any step is VisualClusterAdjacencyLimit, so any
        // relaxation recorded must be exactly that constraint alone.
        var clips = Enumerable.Range(0, 3)
            .Select(i => CleanClipBuilder.Create(i * 2, 1, sourceSceneIndex: i, clusterId: 0))
            .ToList();
        var request = CreateRequest(clips, targetSeconds: 3, maximumReuseCount: 1, minimumRepeatDistance: 0, originalNeighborSeparation: 0, visualClusterAdjacencyLimit: 2);

        var plan = _planner.Plan(request);

        Assert.True(plan.IsComplete);
        Assert.Equal(3, plan.Placements.Count);

        foreach (var entry in plan.DecisionTrace.Where(t => t.RelaxedConstraints.Count > 0))
        {
            Assert.Equal([RelaxedConstraint.VisualClusterAdjacencyLimit], entry.RelaxedConstraints);
        }

        Assert.Contains(plan.DecisionTrace, t => t.RelaxedConstraints.Count > 0);
    }

    [Fact]
    public void Plan_OnlyTheLastPlacementIsEverTrimmed()
    {
        var clips = Enumerable.Range(0, 4)
            .Select(i => CleanClipBuilder.Create(i * 10, 3, sourceSceneIndex: i))
            .ToList();
        var request = CreateRequest(clips, targetSeconds: 10, maximumReuseCount: 1, minimumRepeatDistance: 0, originalNeighborSeparation: 0, visualClusterAdjacencyLimit: 0);

        var plan = _planner.Plan(request);

        TimelinePlanAssertions.AssertOnlyLastPlacementIsTrimmed(plan);
        TimelinePlanAssertions.AssertDurationInvariants(plan);
    }

    [Fact]
    public void Plan_DecisionTraceHasOneEntryPerPlacement_InSamePositionOrder()
    {
        var clips = Enumerable.Range(0, 4)
            .Select(i => CleanClipBuilder.Create(i * 10, 3, sourceSceneIndex: i))
            .ToList();
        var request = CreateRequest(clips, targetSeconds: 9, maximumReuseCount: 1, minimumRepeatDistance: 0, originalNeighborSeparation: 0, visualClusterAdjacencyLimit: 0);

        var plan = _planner.Plan(request);

        TimelinePlanAssertions.AssertDecisionTraceMatchesPlacements(plan);
    }

    [Fact]
    public void Plan_ShortfallWarningMessage_ContainsQuantifiedNumbers()
    {
        // A zero-duration-only pool is the one case reuse relaxation cannot
        // fix (see Plan_ZeroDurationOnlyPool_CannotBeRelaxedIntoReachingTarget_ReportsShortfall),
        // so it is the scenario that still legitimately exercises the
        // Shortfall message format.
        var clips = new[] { CleanClipBuilder.Create(0, 0, sourceSceneIndex: 0) };
        var request = CreateRequest(clips, targetSeconds: 5, maximumReuseCount: 1);

        var plan = _planner.Plan(request);

        Assert.NotNull(plan.FeasibilityWarning);
        Assert.Equal(TimelineFeasibilityWarningKind.Shortfall, plan.FeasibilityWarning!.Kind);
        Assert.Contains("1 clip(s)", plan.FeasibilityWarning.Message);
        Assert.Contains("5.00s", plan.FeasibilityWarning.Message);
        Assert.Contains("0.00s", plan.FeasibilityWarning.Message);
    }

    [Fact]
    public void Plan_SignificantRepetitionWarningMessage_ContainsQuantifiedNumbers()
    {
        var clips = new[] { CleanClipBuilder.Create(0, 2, sourceSceneIndex: 0) };
        var request = CreateRequest(clips, targetSeconds: 5, maximumReuseCount: 1);

        var plan = _planner.Plan(request);

        Assert.True(plan.IsComplete);
        Assert.NotNull(plan.FeasibilityWarning);
        Assert.Equal(TimelineFeasibilityWarningKind.SignificantRepetition, plan.FeasibilityWarning!.Kind);
        Assert.Equal(1, plan.FeasibilityWarning.RequestedMaximumReuseCount);
        Assert.True(plan.FeasibilityWarning.EffectiveMaximumReuseCount > 1);
        Assert.Contains("1 clip(s)", plan.FeasibilityWarning.Message);
        Assert.Contains("requested maximum of 1", plan.FeasibilityWarning.Message);
        Assert.Contains("5.00s", plan.FeasibilityWarning.Message);
    }

    [Fact]
    public void Plan_ExternalCancellation_ThrowsOperationCanceledException()
    {
        var clips = new[] { CleanClipBuilder.Create(0, 1, sourceSceneIndex: 0) };
        var request = CreateRequest(clips, targetSeconds: 5, maximumReuseCount: 10, minimumRepeatDistance: 0);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => _planner.Plan(request, cts.Token));
    }

    [Fact]
    public void Plan_TargetNotAlignedToFrameBoundary_QuantizesAndReportsRoundingError()
    {
        // 25fps -> one frame is exactly 40ms; 1.005s is not a whole number of frames.
        var clip = CleanClipBuilder.Create(0, 5);
        var request = CreateRequest([clip], targetSeconds: 1.005);

        var plan = _planner.Plan(request);

        Assert.NotEqual(TimeSpan.Zero, plan.AudioDurationRoundingError);
        Assert.Equal(plan.QuantizedTargetDuration, request.OutputTimeBase.FromFrameCount(plan.TargetFrameCount));
        Assert.Equal(plan.QuantizedTargetDuration - plan.TargetDuration, plan.AudioDurationRoundingError);
        Assert.True(plan.IsComplete);
        Assert.Equal(plan.QuantizedTargetDuration, plan.PlannedDuration);
    }
}
