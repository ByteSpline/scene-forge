using SceneForge.Media.Domain;
using SceneForge.Media.Extraction;
using SceneForge.Media.Planning;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Planning;

// Phase 16: the product requirement that the planned timeline must always
// match the target audio duration exactly, no matter how little clean
// footage is available, as long as at least one clip carries positive
// duration - never a silently short output (see TimelinePlanner's algorithm
// doc comment and docs/PHASE_16_REPORT.md). These tests cover the guarantee
// itself across a wide range of source-footage-duration-to-target-audio-duration
// ratios (including the extreme end - a target far longer than the entire
// available pool combined, matching the brief's own "1-minute source against
// a 20-minute audio target" example) and the realistic heavy-transition
// scenario that motivated this phase, using the same hand-rolled
// thousands-of-seeds convention TimelinePlannerPropertyTests established in
// Phase 8 (see that file's remarks on property-based tests without a new
// dependency) rather than a hand-picked single case.
public class TimelinePlannerDurationGuaranteeTests
{
    private static readonly RationalFrameRate TwentyFiveFps = new(25, 1);
    private readonly TimelinePlanner _planner = new();

    // Every scenario uses TimelinePlanRequest's own field defaults
    // (MaximumReuseCount / MinimumRepeatDistance / OriginalNeighborSeparation
    // / VisualClusterAdjacencyLimit all 1) unless overridden - the same
    // defaults SceneForge.App's TimelineSummaryViewModel actually plans
    // with, so this exercises the guarantee under real production settings,
    // not only artificially permissive test-only constraints.
    public static TheoryData<string, int, double, int, int, double> RatioScenarios()
    {
        var data = new TheoryData<string, int, double, int, int, double>();

        // name, clipCount, clipDurationSeconds, sceneModulo, clusterModulo, targetSeconds

        // Target already fits within one pass over the pool - the baseline
        // "no relaxation should even be needed" case, included so the
        // property sweep also documents that the guarantee holds trivially
        // here, not only under pressure.
        data.Add("EqualRatio_NoRelaxationNeeded", 15, 4, 5, 4, 60);

        // Target is a small multiple of total pool duration.
        data.Add("ModerateRatio_3x", 15, 4, 5, 4, 180);

        // Target is an order of magnitude beyond total pool duration.
        data.Add("HeavyRatio_10x", 15, 4, 5, 4, 600);

        // The brief's own extreme example: about one minute of source
        // footage (15 clips x 4s = 60s) against a 20-minute audio target -
        // a 20x ratio.
        data.Add("ExtremeRatio_20x_OneMinuteSourceVsTwentyMinuteTarget", 15, 4, 5, 4, 1200);

        // Same 20-minute target, but the pool is a single clip - the
        // absolute worst case for reuse relaxation (every placement must
        // reuse the one available clip, and every spacing constraint that
        // could ever apply to it must relax immediately on its second use).
        data.Add("ExtremeRatio_SingleClipPool", 1, 3, 1, 1, 1200);

        // A larger, still-heavy ratio closer to the realistic scenario
        // below, at a different pool shape (more clips, shorter target) to
        // vary which relaxation tiers actually get exercised.
        data.Add("HeavyRatio_LargerPool", 60, 3.5, 12, 8, 900);

        return data;
    }

    [Theory]
    [MemberData(nameof(RatioScenarios))]
    public void Plan_AcrossWideSourceToTargetRatios_AlwaysReachesTargetExactly(
        string _,
        int clipCount,
        double clipDurationSeconds,
        int sceneModulo,
        int clusterModulo,
        double targetSeconds)
    {
        var clips = BuildPool(clipCount, clipDurationSeconds, sceneModulo, clusterModulo);

        for (var seed = 0; seed < 200; seed++)
        {
            var request = new TimelinePlanRequest
            {
                AvailableClips = clips,
                TargetAudioDuration = TimeSpan.FromSeconds(targetSeconds),
                OutputTimeBase = TwentyFiveFps,
                Seed = seed,
            };

            var plan = _planner.Plan(request);

            // The core guarantee: every clip here has positive duration, so
            // the target must always be reached exactly (within the one
            // frame the target itself was quantized to - see
            // TimelinePlan.QuantizedTargetDuration/AudioDurationRoundingError),
            // never merely approximated and never short.
            Assert.True(plan.IsComplete, $"seed {seed}: expected IsComplete, but achieved only {plan.PlannedDuration} of {plan.QuantizedTargetDuration}.");
            Assert.Equal(plan.QuantizedTargetDuration, plan.PlannedDuration);

            TimelinePlanAssertions.AssertMaximumReuseCountRespectedOrRelaxed(plan, request.MaximumReuseCount);
            TimelinePlanAssertions.AssertMinimumRepeatDistanceRespectedOrRelaxed(plan, request.MinimumRepeatDistance);
            TimelinePlanAssertions.AssertOriginalNeighborSeparationRespectedOrRelaxed(plan, request.OriginalNeighborSeparation);
            TimelinePlanAssertions.AssertVisualClusterAdjacencyLimitRespectedOrRelaxed(plan, request.VisualClusterAdjacencyLimit);
            TimelinePlanAssertions.AssertOnlyLastPlacementIsTrimmed(plan);
            TimelinePlanAssertions.AssertDurationInvariants(plan);
            TimelinePlanAssertions.AssertDecisionTraceMatchesPlacements(plan);

            var replanned = _planner.Plan(request);
            Assert.Equal(plan.Placements, replanned.Placements);
        }
    }

    [Fact]
    public void Plan_RealisticHeavyTransitionSource_TwentyFourMinuteSourceAgainstTwentyTwoMinuteTarget_ReachesTargetExactly()
    {
        // Models the scenario this phase was written to fix: a 24-minute
        // (1440s) source video whose heavy transitions (and the scoring
        // rejections they cause - see CleanClipExtractor) leave roughly
        // 800s (~13.3 minutes) of accepted clean footage - 200 clips
        // averaging 4s, cycling 3.0-5.0s in 0.5s steps the same way
        // CleanClipExtractor's own MinClipDuration..MaxClipDuration default
        // range (3-5s) would - against a 22-minute (1320s) target audio
        // track. Under TimelinePlanRequest's own field defaults
        // (MaximumReuseCount = 1, matching what SceneForge.App actually
        // plans with), the old, pre-Phase-16 behavior could reach at most
        // 800s and would report a 520s shortfall no relaxation could close
        // (MaximumReuseCount was a hard, never-relaxed cap - see
        // docs/PHASE_08_REPORT.md). This test proves that scenario now
        // succeeds.
        var clips = BuildPool(count: 200, baseDurationSeconds: 3.0, sceneModulo: 40, clusterModulo: 15);
        var totalCleanFootage = clips.Aggregate(TimeSpan.Zero, (sum, c) => sum + c.Range.Duration);
        Assert.True(totalCleanFootage < TimeSpan.FromMinutes(22), "test setup sanity check: clean footage must be less than the target to actually exercise relaxation.");

        var request = new TimelinePlanRequest
        {
            AvailableClips = clips,
            TargetAudioDuration = TimeSpan.FromMinutes(22),
            OutputTimeBase = TwentyFiveFps,
            Seed = 42,
        };

        var plan = _planner.Plan(request);

        Assert.True(plan.IsComplete);
        Assert.Equal(plan.QuantizedTargetDuration, plan.PlannedDuration);
        Assert.Equal(TimeSpan.FromMinutes(22), plan.PlannedDuration);

        Assert.NotNull(plan.FeasibilityWarning);
        Assert.Equal(TimelineFeasibilityWarningKind.SignificantRepetition, plan.FeasibilityWarning!.Kind);
        Assert.Equal(1, plan.FeasibilityWarning.RequestedMaximumReuseCount);
        Assert.True(plan.FeasibilityWarning.EffectiveMaximumReuseCount > 1);

        TimelinePlanAssertions.AssertMaximumReuseCountRespectedOrRelaxed(plan, request.MaximumReuseCount);
        TimelinePlanAssertions.AssertDurationInvariants(plan);
        TimelinePlanAssertions.AssertDecisionTraceMatchesPlacements(plan);
    }

    [Fact]
    public void Plan_HeavyRepetition_RotatesTieBreakOrder_SoConsecutiveFullPassesOverThePoolDiffer()
    {
        // "Loop the available clip sequence with continued shuffling, not
        // identical repeated order" (the brief's tier 4): once
        // MaximumReuseCount has been relaxed enough that every clip is used
        // several times, verify consecutive full passes over the pool are
        // not simply the exact same relative order replayed - see
        // PlacementTracker.RotatingTieBreakKey.
        var clips = Enumerable.Range(0, 6)
            .Select(i => CleanClipBuilder.Create(i * 10, 2, sourceSceneIndex: i))
            .ToList();

        // 6 clips x 2s = 12s per full pass; a 72s target forces exactly 6
        // full passes (36 placements) with every spacing constraint fully
        // permissive, isolating the tie-break rotation as the only thing
        // that could vary pass-to-pass order.
        var request = new TimelinePlanRequest
        {
            AvailableClips = clips,
            TargetAudioDuration = TimeSpan.FromSeconds(72),
            OutputTimeBase = TwentyFiveFps,
            Seed = 7,
            MinimumRepeatDistance = 0,
            OriginalNeighborSeparation = 0,
            VisualClusterAdjacencyLimit = 0,
        };

        var plan = _planner.Plan(request);

        Assert.True(plan.IsComplete);
        Assert.Equal(36, plan.Placements.Count);

        var passes = plan.Placements
            .Select(p => p.ClipIndex)
            .Chunk(6)
            .ToList();

        Assert.Equal(6, passes.Count);
        Assert.All(passes, pass => Assert.Equal(6, pass.Distinct().Count()));
        Assert.Contains(passes.Skip(1), pass => !pass.SequenceEqual(passes[0]));
    }

    private static List<CleanClip> BuildPool(int count, double baseDurationSeconds, int sceneModulo, int clusterModulo)
    {
        var clips = new List<CleanClip>(count);
        for (var i = 0; i < count; i++)
        {
            var duration = baseDurationSeconds + (i % 5) * 0.5;
            clips.Add(CleanClipBuilder.Create(
                startSeconds: i * 100,
                durationSeconds: duration,
                sourceSceneIndex: i % sceneModulo,
                clusterId: i % clusterModulo));
        }

        return clips;
    }
}
