using SceneForge.Media.Domain;
using SceneForge.Media.Planning;

namespace SceneForge.Media.Tests.TestSupport;

// Hand-builds a TimelinePlan directly (rather than via TimelinePlanner) for
// Rendering tests, which only ever read TimelinePlan.Placements/PlannedDuration -
// the same "construct the downstream fact directly" shape CleanClipBuilder
// already established for Planning tests.
internal static class TimelinePlanBuilder
{
    public static TimelinePlacement CreatePlacement(
        int position,
        int clipIndex,
        double sourceStartSeconds,
        double sourceDurationSeconds,
        double? usedDurationSeconds = null,
        bool isTrimmed = false,
        int sourceSceneIndex = 0,
        int? clusterId = null,
        int usageOrdinal = 1)
    {
        var used = TimeSpan.FromSeconds(usedDurationSeconds ?? sourceDurationSeconds);
        return new TimelinePlacement
        {
            Position = position,
            ClipIndex = clipIndex,
            SourceRange = new TimeRange(TimeSpan.FromSeconds(sourceStartSeconds), TimeSpan.FromSeconds(sourceStartSeconds + sourceDurationSeconds)),
            UsedDuration = used,
            IsTrimmed = isTrimmed,
            SourceSceneIndex = sourceSceneIndex,
            ClusterId = clusterId,
            UsageOrdinal = usageOrdinal,
        };
    }

    public static TimelinePlan CreatePlan(IReadOnlyList<TimelinePlacement> placements, RationalFrameRate? outputTimeBase = null)
    {
        var plannedDuration = placements.Aggregate(TimeSpan.Zero, (sum, p) => sum + p.UsedDuration);
        var timeBase = outputTimeBase ?? new RationalFrameRate(25, 1);

        return new TimelinePlan
        {
            Placements = placements,
            PlannedDuration = plannedDuration,
            TargetDuration = plannedDuration,
            QuantizedTargetDuration = plannedDuration,
            TargetFrameCount = timeBase.ToFrameCount(plannedDuration),
            AudioDurationRoundingError = TimeSpan.Zero,
            IsComplete = true,
            DecisionTrace = placements.Select(p => new TimelinePlanTraceEntry
            {
                Position = p.Position,
                ClipIndex = p.ClipIndex,
                Explanation = "test fixture",
                RelaxedConstraints = [],
            }).ToList(),
            FeasibilityWarning = null,
        };
    }
}
