using System.Globalization;
using SceneForge.Media.Extraction;
using SceneForge.Media.Planning.Internal;

namespace SceneForge.Media.Planning;

// Deterministic constraint-based clip sequencer: turns a pool of
// CleanClip candidates into an ordered TimelinePlan whose total duration
// matches TargetAudioDuration exactly (in whole frames at OutputTimeBase),
// while avoiding immediate duplicates, neighboring clips from the same
// source scene, repeated visual clusters, and (via the same mechanism as
// same-scene neighbors, see TimelinePlanRequest.OriginalNeighborSeparation)
// long stretches of preserved original source order.
//
// Algorithm, per placement:
//   1. Every clip usable up to TimelinePlanRequest.MaximumReuseCount (a
//      hard cap, never relaxed) and satisfying every active placement
//      constraint (MinimumRepeatDistance / OriginalNeighborSeparation /
//      VisualClusterAdjacencyLimit) is "eligible."
//   2. If no clip is eligible at full strictness, constraints are relaxed
//      one at a time - VisualClusterAdjacencyLimit first, then
//      OriginalNeighborSeparation, then MinimumRepeatDistance - stopping at
//      the first relaxation tier that has any eligible clip. Every
//      constraint actually relaxed to make a placement possible is
//      recorded on that placement's TimelinePlanTraceEntry.
//   3. If a clip is eligible whose full duration covers however much of
//      the target remains, the smallest such clip is chosen (minimizing
//      trim overshoot) and trimmed to close out the plan. Otherwise the
//      least-used eligible clip is chosen (unique clips before any reuse,
//      then the seeded shuffle rank as the deterministic tie-break) and
//      placed at its full duration.
//   4. If every clip has reached MaximumReuseCount before the target is
//      reached, the loop stops and TimelinePlan.FeasibilityWarning
//      quantifies the shortfall instead of exceeding the cap.
//
// Because every clip that reaches MaximumReuseCount becomes permanently
// ineligible, the loop always terminates within
// AvailableClips.Count * MaximumReuseCount iterations - no unbounded
// fan-out, consistent with CLAUDE.md rule 6.
public sealed class TimelinePlanner : ITimelinePlanner
{
    public TimelinePlan Plan(TimelinePlanRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.AvailableClips);

        if (request.TargetAudioDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.TargetAudioDuration, "TargetAudioDuration must not be negative.");
        }

        if (!request.OutputTimeBase.IsDefined)
        {
            throw new ArgumentException("OutputTimeBase must be a defined frame rate.", nameof(request));
        }

        var clips = request.AvailableClips;
        var targetFrameCount = request.OutputTimeBase.ToFrameCount(request.TargetAudioDuration);
        var quantizedTarget = request.OutputTimeBase.FromFrameCount(targetFrameCount);

        var placements = new List<TimelinePlacement>();
        var trace = new List<TimelinePlanTraceEntry>();
        var tracker = new PlacementTracker(request);

        var remaining = quantizedTarget;
        var position = 0;

        while (remaining > TimeSpan.Zero)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var selection = tracker.SelectCandidate(position, remaining);
            if (selection is null)
            {
                break;
            }

            var (clipIndex, tier, isFinal) = selection.Value;
            var clip = clips[clipIndex];
            var relaxed = new List<RelaxedConstraint>(TierRelaxations[tier]);
            var usedDuration = isFinal ? remaining : clip.Range.Duration;

            if (isFinal)
            {
                if (remaining < request.DurationBounds.MinFinalClipDuration)
                {
                    relaxed.Add(RelaxedConstraint.FinalClipBelowMinDuration);
                }

                var overshoot = clip.Range.Duration - remaining;
                if (overshoot > request.DurationBounds.MaxOvershoot)
                {
                    relaxed.Add(RelaxedConstraint.FinalClipOvershootExceeded);
                }
            }

            placements.Add(new TimelinePlacement
            {
                Position = position,
                ClipIndex = clipIndex,
                SourceRange = clip.Range,
                UsedDuration = usedDuration,
                IsTrimmed = usedDuration < clip.Range.Duration,
                SourceSceneIndex = clip.SourceSceneIndex,
                ClusterId = clip.ClusterId,
                UsageOrdinal = tracker.UsageCount(clipIndex) + 1,
            });

            trace.Add(new TimelinePlanTraceEntry
            {
                Position = position,
                ClipIndex = clipIndex,
                Explanation = BuildExplanation(clipIndex, tracker.UsageCount(clipIndex), tracker.Rank(clipIndex), clips.Count, clip.Range.Duration, usedDuration, isFinal, relaxed),
                RelaxedConstraints = relaxed,
            });

            tracker.RecordPlacement(clipIndex, position);

            remaining -= usedDuration;
            position++;

            if (isFinal)
            {
                break;
            }
        }

        var plannedDuration = placements.Aggregate(TimeSpan.Zero, (sum, p) => sum + p.UsedDuration);
        var isComplete = plannedDuration == quantizedTarget;

        return new TimelinePlan
        {
            Placements = placements,
            PlannedDuration = plannedDuration,
            TargetDuration = request.TargetAudioDuration,
            QuantizedTargetDuration = quantizedTarget,
            TargetFrameCount = targetFrameCount,
            AudioDurationRoundingError = quantizedTarget - request.TargetAudioDuration,
            IsComplete = isComplete,
            DecisionTrace = trace,
            FeasibilityWarning = isComplete ? null : BuildFeasibilityWarning(quantizedTarget, plannedDuration, clips.Count, request.MaximumReuseCount),
        };
    }

    private static readonly IReadOnlyList<RelaxedConstraint>[] TierRelaxations =
    [
        [],
        [RelaxedConstraint.VisualClusterAdjacencyLimit],
        [RelaxedConstraint.VisualClusterAdjacencyLimit, RelaxedConstraint.OriginalNeighborSeparation],
        [RelaxedConstraint.VisualClusterAdjacencyLimit, RelaxedConstraint.OriginalNeighborSeparation, RelaxedConstraint.MinimumRepeatDistance],
    ];

    private static TimelineFeasibilityWarning BuildFeasibilityWarning(TimeSpan target, TimeSpan achieved, int clipCount, int maximumReuseCount)
    {
        var shortfall = target - achieved;
        var message = string.Format(
            CultureInfo.InvariantCulture,
            "Requested {0}s but only {1}s is achievable from {2} clip(s) at a maximum reuse count of {3} under the active placement constraints (shortfall {4}s).",
            FormatSeconds(target),
            FormatSeconds(achieved),
            clipCount,
            maximumReuseCount,
            FormatSeconds(shortfall));

        return new TimelineFeasibilityWarning
        {
            Message = message,
            TargetDuration = target,
            AchievedDuration = achieved,
            Shortfall = shortfall,
        };
    }

    private static string BuildExplanation(
        int clipIndex,
        int priorUsageCount,
        int shuffleRank,
        int clipCount,
        TimeSpan sourceDuration,
        TimeSpan usedDuration,
        bool isFinal,
        IReadOnlyList<RelaxedConstraint> relaxed)
    {
        var constraintsSummary = relaxed.Count == 0
            ? "satisfied all placement constraints at full strictness"
            : string.Format(CultureInfo.InvariantCulture, "relaxed: {0}", string.Join(", ", relaxed));

        var durationSummary = isFinal && usedDuration < sourceDuration
            ? string.Format(CultureInfo.InvariantCulture, "trimmed from {0}s to {1}s to match the remaining budget exactly.", FormatSeconds(sourceDuration), FormatSeconds(usedDuration))
            : string.Format(CultureInfo.InvariantCulture, "used at full {0}s duration.", FormatSeconds(usedDuration));

        return string.Format(
            CultureInfo.InvariantCulture,
            "Selected clip {0} (used {1} time(s) so far, shuffle rank {2}/{3}); {4}; {5}",
            clipIndex,
            priorUsageCount,
            shuffleRank,
            clipCount,
            constraintsSummary,
            durationSummary);
    }

    private static string FormatSeconds(TimeSpan value) => value.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture);

    // Owns every piece of running state one Plan call accumulates across
    // placements (usage counts, last-seen positions per clip/scene/cluster,
    // the deterministic shuffle rank) and the eligibility/selection logic
    // that reads it - kept as its own type rather than a long parameter
    // list threaded through static methods.
    private sealed class PlacementTracker
    {
        private readonly TimelinePlanRequest _request;
        private readonly IReadOnlyList<CleanClip> _clips;
        private readonly int[] _rank;
        private readonly int[] _usageCount;
        private readonly int?[] _lastPositionByClip;
        private readonly Dictionary<int, int> _lastPositionByScene = [];
        private readonly Dictionary<int, int> _lastPositionByCluster = [];

        public PlacementTracker(TimelinePlanRequest request)
        {
            _request = request;
            _clips = request.AvailableClips;
            _rank = ClipShuffleOrder.ComputeRanks(_clips.Count, request.Seed);
            _usageCount = new int[_clips.Count];
            _lastPositionByClip = new int?[_clips.Count];
        }

        public int UsageCount(int clipIndex) => _usageCount[clipIndex];

        public int Rank(int clipIndex) => _rank[clipIndex];

        public (int ClipIndex, int Tier, bool IsFinal)? SelectCandidate(int position, TimeSpan remaining)
        {
            for (var tier = 0; tier < TierRelaxations.Length; tier++)
            {
                var eligible = new List<int>();
                for (var clipIndex = 0; clipIndex < _clips.Count; clipIndex++)
                {
                    if (IsEligible(clipIndex, tier, position))
                    {
                        eligible.Add(clipIndex);
                    }
                }

                if (eligible.Count == 0)
                {
                    continue;
                }

                var finalCandidate = eligible
                    .Where(c => _clips[c].Range.Duration >= remaining)
                    .OrderBy(c => _clips[c].Range.Duration)
                    .ThenBy(c => _usageCount[c])
                    .ThenBy(c => _rank[c])
                    .Cast<int?>()
                    .FirstOrDefault();

                if (finalCandidate is int chosenFinal)
                {
                    return (chosenFinal, tier, true);
                }

                var chosen = eligible
                    .OrderBy(c => _usageCount[c])
                    .ThenBy(c => _rank[c])
                    .First();

                return (chosen, tier, false);
            }

            return null;
        }

        public void RecordPlacement(int clipIndex, int position)
        {
            var clip = _clips[clipIndex];
            _usageCount[clipIndex]++;
            _lastPositionByClip[clipIndex] = position;
            _lastPositionByScene[clip.SourceSceneIndex] = position;
            if (clip.ClusterId is int clusterId)
            {
                _lastPositionByCluster[clusterId] = position;
            }
        }

        // A constraint value of N means "N intervening placements required,"
        // i.e. the position distance between two occurrences must exceed N
        // (distance > N, equivalently distance <= N is blocked) - N = 0 is
        // therefore always a no-op (distance is never 0 between two
        // distinct placements), and N = 1 is the smallest value that
        // actually forbids literal back-to-back adjacency (distance == 1).
        private bool IsEligible(int clipIndex, int tier, int position)
        {
            if (_usageCount[clipIndex] >= _request.MaximumReuseCount)
            {
                return false;
            }

            var clip = _clips[clipIndex];
            var repeatActive = tier < 3;
            var sceneActive = tier < 2;
            var clusterActive = tier < 1;

            if (repeatActive
                && _lastPositionByClip[clipIndex] is int lastClipPosition
                && position - lastClipPosition <= _request.MinimumRepeatDistance)
            {
                return false;
            }

            if (sceneActive
                && _lastPositionByScene.TryGetValue(clip.SourceSceneIndex, out var lastScenePosition)
                && position - lastScenePosition <= _request.OriginalNeighborSeparation)
            {
                return false;
            }

            if (clusterActive
                && clip.ClusterId is int clusterId
                && _lastPositionByCluster.TryGetValue(clusterId, out var lastClusterPosition)
                && position - lastClusterPosition <= _request.VisualClusterAdjacencyLimit)
            {
                return false;
            }

            return true;
        }
    }
}
