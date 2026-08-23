using SceneForge.Media.Domain;
using SceneForge.Media.Extraction;
using SceneForge.Media.Planning;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Planning;

// Property-based coverage: rather than pull in an external PBT framework
// for a single algorithm, these tests directly iterate thousands of
// TimelinePlanRequest.Seed values against a handful of representative clip
// pools (varied size, duration, scene/cluster distribution, and
// feasibility) and assert the same structural invariants
// TimelinePlannerTests checks by hand for individual scenarios - "same seed
// and inputs must always create the same plan" and every placement
// constraint (repeat distance / original-neighbor separation / visual-
// cluster adjacency / max reuse) either holds or is recorded as relaxed,
// for every one of those seeds.
public class TimelinePlannerPropertyTests
{
    private static readonly RationalFrameRate TwentyFiveFps = new(25, 1);
    private readonly TimelinePlanner _planner = new();

    public static TheoryData<string, IReadOnlyList<CleanClip>, double, int, int, int, int> PoolConfigurations()
    {
        var data = new TheoryData<string, IReadOnlyList<CleanClip>, double, int, int, int, int>();

        data.Add(
            "UniformSmall",
            BuildPool(count: 6, durationSeconds: 2, sceneModulo: 3, clusterModulo: 2),
            20,
            4,
            1,
            1,
            1);

        data.Add(
            "ManyClipsVariedDuration",
            BuildPool(count: 25, durationSeconds: -1, sceneModulo: 6, clusterModulo: 4),
            45,
            2,
            2,
            2,
            2);

        data.Add(
            "SingleScene",
            BuildPool(count: 8, durationSeconds: 1, sceneModulo: 1, clusterModulo: 3),
            15,
            3,
            1,
            3,
            1);

        data.Add(
            "InsufficientFootage",
            BuildPool(count: 3, durationSeconds: 2, sceneModulo: 3, clusterModulo: 3),
            100,
            1,
            0,
            0,
            0);

        data.Add(
            "TinyOverAbundant",
            BuildPool(count: 40, durationSeconds: 3, sceneModulo: 10, clusterModulo: 5),
            5,
            1,
            1,
            1,
            1);

        return data;
    }

    [Theory]
    [MemberData(nameof(PoolConfigurations))]
    public void Plan_ThousandsOfSeeds_AlwaysHoldsInvariants(
        string _,
        IReadOnlyList<CleanClip> clips,
        double targetSeconds,
        int maximumReuseCount,
        int minimumRepeatDistance,
        int originalNeighborSeparation,
        int visualClusterAdjacencyLimit)
    {
        for (var seed = 0; seed < 2000; seed++)
        {
            var request = new TimelinePlanRequest
            {
                AvailableClips = clips,
                TargetAudioDuration = TimeSpan.FromSeconds(targetSeconds),
                OutputTimeBase = TwentyFiveFps,
                Seed = seed,
                MaximumReuseCount = maximumReuseCount,
                MinimumRepeatDistance = minimumRepeatDistance,
                OriginalNeighborSeparation = originalNeighborSeparation,
                VisualClusterAdjacencyLimit = visualClusterAdjacencyLimit,
            };

            var plan = _planner.Plan(request);

            TimelinePlanAssertions.AssertNeverExceedsMaximumReuseCount(plan, maximumReuseCount);
            TimelinePlanAssertions.AssertMinimumRepeatDistanceRespectedOrRelaxed(plan, minimumRepeatDistance);
            TimelinePlanAssertions.AssertOriginalNeighborSeparationRespectedOrRelaxed(plan, originalNeighborSeparation);
            TimelinePlanAssertions.AssertVisualClusterAdjacencyLimitRespectedOrRelaxed(plan, visualClusterAdjacencyLimit);
            TimelinePlanAssertions.AssertOnlyLastPlacementIsTrimmed(plan);
            TimelinePlanAssertions.AssertDurationInvariants(plan);
            TimelinePlanAssertions.AssertDecisionTraceMatchesPlacements(plan);

            // Determinism: replanning the exact same request must reproduce
            // the exact same plan for every one of these seeds, not just a
            // hand-picked one.
            var replanned = _planner.Plan(request);
            Assert.Equal(plan.Placements, replanned.Placements);
            Assert.Equal(plan.IsComplete, replanned.IsComplete);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Plan_EdgeCase_EmptyAvailableClips_NeverThrows(double targetSeconds)
    {
        var request = new TimelinePlanRequest
        {
            AvailableClips = [],
            TargetAudioDuration = TimeSpan.FromSeconds(targetSeconds),
            OutputTimeBase = TwentyFiveFps,
            Seed = 1,
        };

        var plan = _planner.Plan(request);

        Assert.Empty(plan.Placements);
        Assert.Equal(targetSeconds == 0, plan.IsComplete);
    }

    [Fact]
    public void Plan_EdgeCase_SingleClipSingleSlot_NeverThrowsAcrossManySeeds()
    {
        var clips = new[] { CleanClipBuilder.Create(0, 4, sourceSceneIndex: 0) };

        for (var seed = 0; seed < 1000; seed++)
        {
            var request = new TimelinePlanRequest
            {
                AvailableClips = clips,
                TargetAudioDuration = TimeSpan.FromSeconds(4),
                OutputTimeBase = TwentyFiveFps,
                Seed = seed,
                MaximumReuseCount = 1,
            };

            var plan = _planner.Plan(request);

            Assert.True(plan.IsComplete);
            var placement = Assert.Single(plan.Placements);
            Assert.False(placement.IsTrimmed);
        }
    }

    [Fact]
    public void Plan_EdgeCase_ZeroDurationClipsAreUsedButNeverProgressTheBudgetIncorrectly()
    {
        // A degenerate but structurally valid pool: some clips have zero
        // duration (e.g. an upstream candidate whose Range collapsed to a
        // single instant). These are never a good placement choice but must
        // not cause a hang, a negative remaining budget, or a crash.
        var clips = new List<CleanClip>
        {
            CleanClipBuilder.Create(0, 0, sourceSceneIndex: 0),
            CleanClipBuilder.Create(1, 2, sourceSceneIndex: 1),
        };

        for (var seed = 0; seed < 500; seed++)
        {
            var request = new TimelinePlanRequest
            {
                AvailableClips = clips,
                TargetAudioDuration = TimeSpan.FromSeconds(2),
                OutputTimeBase = TwentyFiveFps,
                Seed = seed,
                MaximumReuseCount = 3,
                MinimumRepeatDistance = 0,
                OriginalNeighborSeparation = 0,
                VisualClusterAdjacencyLimit = 0,
            };

            var plan = _planner.Plan(request);

            TimelinePlanAssertions.AssertDurationInvariants(plan);
            TimelinePlanAssertions.AssertNeverExceedsMaximumReuseCount(plan, request.MaximumReuseCount);
        }
    }

    private static List<CleanClip> BuildPool(int count, double durationSeconds, int sceneModulo, int clusterModulo)
    {
        var clips = new List<CleanClip>(count);
        for (var i = 0; i < count; i++)
        {
            var duration = durationSeconds > 0 ? durationSeconds : 1 + (i % 5) * 0.5;
            clips.Add(CleanClipBuilder.Create(
                startSeconds: i * 100,
                durationSeconds: duration,
                sourceSceneIndex: i % sceneModulo,
                clusterId: i % clusterModulo));
        }

        return clips;
    }
}
