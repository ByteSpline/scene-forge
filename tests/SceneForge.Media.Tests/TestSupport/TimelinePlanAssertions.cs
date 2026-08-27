using SceneForge.Media.Planning;

namespace SceneForge.Media.Tests.TestSupport;

// Shared invariant checks reused by both TimelinePlannerTests (hand-picked
// scenarios) and TimelinePlannerPropertyTests (thousands of randomized
// seeds/pools) - every check either holds outright or is accompanied by the
// matching RelaxedConstraint on that placement's trace entry, never a
// silent violation.
internal static class TimelinePlanAssertions
{
    // MaximumReuseCount is a preference, not a hard cap (see
    // TimelinePlanRequest.MaximumReuseCount and RelaxedConstraint.MaximumReuseCount):
    // a clip may be used more times than requested, but only when every
    // placement that pushed it past the requested count is tagged
    // accordingly, the same "respected or relaxed" pattern the other three
    // placement-spacing assertions below already use.
    public static void AssertMaximumReuseCountRespectedOrRelaxed(TimelinePlan plan, int maximumReuseCount)
    {
        var usageOrdinalByClip = new Dictionary<int, int>();
        foreach (var placement in plan.Placements)
        {
            usageOrdinalByClip.TryGetValue(placement.ClipIndex, out var priorOrdinal);
            var thisOrdinal = priorOrdinal + 1;
            usageOrdinalByClip[placement.ClipIndex] = thisOrdinal;

            if (thisOrdinal > maximumReuseCount)
            {
                AssertRelaxed(plan, placement.Position, RelaxedConstraint.MaximumReuseCount);
            }
        }
    }

    public static void AssertMinimumRepeatDistanceRespectedOrRelaxed(TimelinePlan plan, int minimumRepeatDistance)
    {
        var lastPositionByClip = new Dictionary<int, int>();
        foreach (var placement in plan.Placements)
        {
            if (lastPositionByClip.TryGetValue(placement.ClipIndex, out var lastPosition)
                && placement.Position - lastPosition <= minimumRepeatDistance)
            {
                AssertRelaxed(plan, placement.Position, RelaxedConstraint.MinimumRepeatDistance);
            }

            lastPositionByClip[placement.ClipIndex] = placement.Position;
        }
    }

    public static void AssertOriginalNeighborSeparationRespectedOrRelaxed(TimelinePlan plan, int separation)
    {
        var lastPositionByScene = new Dictionary<int, int>();
        foreach (var placement in plan.Placements)
        {
            if (lastPositionByScene.TryGetValue(placement.SourceSceneIndex, out var lastPosition)
                && placement.Position - lastPosition <= separation)
            {
                AssertRelaxed(plan, placement.Position, RelaxedConstraint.OriginalNeighborSeparation);
            }

            lastPositionByScene[placement.SourceSceneIndex] = placement.Position;
        }
    }

    public static void AssertVisualClusterAdjacencyLimitRespectedOrRelaxed(TimelinePlan plan, int limit)
    {
        var lastPositionByCluster = new Dictionary<int, int>();
        foreach (var placement in plan.Placements)
        {
            if (placement.ClusterId is not int clusterId)
            {
                continue;
            }

            if (lastPositionByCluster.TryGetValue(clusterId, out var lastPosition)
                && placement.Position - lastPosition <= limit)
            {
                AssertRelaxed(plan, placement.Position, RelaxedConstraint.VisualClusterAdjacencyLimit);
            }

            lastPositionByCluster[clusterId] = placement.Position;
        }
    }

    public static void AssertOnlyLastPlacementIsTrimmed(TimelinePlan plan)
    {
        for (var i = 0; i < plan.Placements.Count - 1; i++)
        {
            Assert.False(plan.Placements[i].IsTrimmed, $"Placement at position {i} was trimmed but is not the last placement.");
        }
    }

    public static void AssertDurationInvariants(TimelinePlan plan)
    {
        var sum = plan.Placements.Aggregate(TimeSpan.Zero, (total, p) => total + p.UsedDuration);
        Assert.Equal(sum, plan.PlannedDuration);

        if (plan.IsComplete)
        {
            Assert.Equal(plan.QuantizedTargetDuration, plan.PlannedDuration);

            // A complete plan can still carry a warning: reaching the target
            // exactly by relaxing MaximumReuseCount is informational only
            // (TimelineFeasibilityWarningKind.SignificantRepetition), never a
            // shortfall - see TimelinePlan.FeasibilityWarning.
            if (plan.FeasibilityWarning is not null)
            {
                Assert.Equal(TimelineFeasibilityWarningKind.SignificantRepetition, plan.FeasibilityWarning.Kind);
                Assert.Equal(TimeSpan.Zero, plan.FeasibilityWarning.Shortfall);
            }
        }
        else
        {
            Assert.NotNull(plan.FeasibilityWarning);
            Assert.Equal(TimelineFeasibilityWarningKind.Shortfall, plan.FeasibilityWarning!.Kind);
            Assert.True(plan.PlannedDuration < plan.QuantizedTargetDuration);
            Assert.Equal(plan.QuantizedTargetDuration - plan.PlannedDuration, plan.FeasibilityWarning.Shortfall);
        }
    }

    public static void AssertDecisionTraceMatchesPlacements(TimelinePlan plan)
    {
        Assert.Equal(plan.Placements.Count, plan.DecisionTrace.Count);
        for (var i = 0; i < plan.Placements.Count; i++)
        {
            Assert.Equal(plan.Placements[i].Position, plan.DecisionTrace[i].Position);
            Assert.Equal(plan.Placements[i].ClipIndex, plan.DecisionTrace[i].ClipIndex);
            Assert.Equal(i, plan.Placements[i].Position);
        }
    }

    private static void AssertRelaxed(TimelinePlan plan, int position, RelaxedConstraint expected)
    {
        var trace = plan.DecisionTrace.Single(t => t.Position == position);
        Assert.Contains(expected, trace.RelaxedConstraints);
    }
}
