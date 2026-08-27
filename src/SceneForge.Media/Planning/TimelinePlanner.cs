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
// Reaching TargetAudioDuration exactly is a hard product requirement that
// outranks every constraint below, including the requested MaximumReuseCount:
// the output must never be shorter than requested when reasonable
// relaxation could close the gap. Plan makes up to two attempts:
//
//   Attempt 1 - exactly as requested. Per placement:
//     1. Every clip usable up to TimelinePlanRequest.MaximumReuseCount and
//        satisfying every active placement constraint (MinimumRepeatDistance
//        / OriginalNeighborSeparation / VisualClusterAdjacencyLimit) is
//        "eligible."
//     2. If no clip is eligible at full strictness, spacing constraints are
//        relaxed one at a time - VisualClusterAdjacencyLimit first, then
//        OriginalNeighborSeparation, then MinimumRepeatDistance (the most
//        noticeable repetition, relaxed last) - stopping at the first tier
//        with any eligible clip. Every constraint actually relaxed for a
//        placement is recorded on that placement's TimelinePlanTraceEntry.
//     3. If a clip is eligible whose full duration covers however much of
//        the target remains, the smallest such clip is chosen (minimizing
//        trim overshoot) and trimmed to close out the plan. Otherwise the
//        least-used eligible clip is chosen (unique clips before any reuse,
//        then the seeded shuffle rank as the deterministic tie-break) and
//        placed at its full duration.
//     4. If every clip has reached MaximumReuseCount under every spacing
//        tier before the target is reached, attempt 1 stops short.
//
//   Attempt 2 (only runs if attempt 1 stopped short) - MaximumReuseCount is
//   relaxed FIRST, ahead of anything else: ComputeGuaranteedSufficientReuseCap
//   computes the smallest reuse cap that provably lets the shortest
//   available clip alone cover the whole target, and the entire placement
//   process above re-runs from scratch with that higher cap (still
//   preferring least-used clips first, still relaxing spacing constraints
//   per placement exactly as before - only the hard cap itself moved).
//   Because the pool always contains at least one positive-duration clip
//   whenever attempt 1 placed anything at all, this second attempt always
//   reaches the target exactly - the only way IsComplete can still be false
//   afterward is a pool with no usable positive-duration content at all
//   (empty, or every clip collapsed to zero duration), which no amount of
//   reuse, spacing relaxation, or repetition could ever cover regardless.
//   Every placement whose use of its clip exceeded the originally requested
//   MaximumReuseCount is tagged RelaxedConstraint.MaximumReuseCount, and
//   TimelinePlan.FeasibilityWarning reports a
//   TimelineFeasibilityWarningKind.SignificantRepetition warning for
//   transparency even though IsComplete is true.
//
// Because every clip that reaches the cap in effect for a given attempt
// becomes permanently ineligible within that attempt, each attempt's loop
// always terminates within AvailableClips.Count * effectiveMaximumReuseCount
// iterations - no unbounded fan-out (CLAUDE.md rule 6). The relaxed cap
// itself is bounded by MaxReuseRelaxationHeadroom, a generous but finite
// safety ceiling - see ComputeGuaranteedSufficientReuseCap.
public sealed class TimelinePlanner : ITimelinePlanner
{
    // Finite backstop on how far MaximumReuseCount can be relaxed in one
    // attempt 2 retry. Realistic footage (CleanClipExtractor's own minimum
    // clip duration is 1s, default 3s) needs nowhere near this many uses of
    // a single clip even against an extreme target/source ratio (e.g. a
    // 20-minute target from 1 minute of 3-5s source clips needs at most a
    // few hundred), so this exists only to keep a pathological synthetic
    // input (e.g. a sub-millisecond test clip against a very long target)
    // from turning one Plan call into an unbounded amount of work - not a
    // limit expected to bind in practice.
    private const long MaxReuseRelaxationHeadroom = 2_000_000;

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

        var plan = PlanWithReuseCap(request, request.MaximumReuseCount, quantizedTarget, targetFrameCount, cancellationToken);

        if (!plan.IsComplete)
        {
            var relaxedCap = ComputeGuaranteedSufficientReuseCap(request.MaximumReuseCount, quantizedTarget, clips);
            if (relaxedCap > request.MaximumReuseCount)
            {
                plan = PlanWithReuseCap(request, relaxedCap, quantizedTarget, targetFrameCount, cancellationToken);
            }
        }

        return plan;
    }

    // The smallest reuse cap that provably suffices to reach quantizedTarget
    // using only the single shortest positive-duration clip in the pool,
    // repeated on its own with every spacing constraint relaxed (tier 3,
    // always available once any clip's cap has headroom) - i.e. a true
    // worst-case upper bound, since spreading placements across more than
    // one clip (which the least-used-first policy always does when
    // possible) only ever needs less of any single clip than this. Returns
    // currentCap unchanged (no relaxation is possible) when the pool is
    // empty, the target is already zero, or every clip has collapsed to
    // zero duration - no cap could add duration that does not exist.
    private static int ComputeGuaranteedSufficientReuseCap(int currentCap, TimeSpan quantizedTarget, IReadOnlyList<CleanClip> clips)
    {
        if (quantizedTarget <= TimeSpan.Zero || clips.Count == 0)
        {
            return currentCap;
        }

        var shortestPositiveDuration = TimeSpan.MaxValue;
        foreach (var clip in clips)
        {
            if (clip.Range.Duration > TimeSpan.Zero && clip.Range.Duration < shortestPositiveDuration)
            {
                shortestPositiveDuration = clip.Range.Duration;
            }
        }

        if (shortestPositiveDuration == TimeSpan.MaxValue)
        {
            return currentCap;
        }

        var neededUses = (long)Math.Ceiling(quantizedTarget / shortestPositiveDuration) + 1;
        var bounded = Math.Min(neededUses, MaxReuseRelaxationHeadroom);
        return (int)Math.Max(currentCap, bounded);
    }

    private static TimelinePlan PlanWithReuseCap(
        TimelinePlanRequest request,
        int effectiveMaximumReuseCount,
        TimeSpan quantizedTarget,
        long targetFrameCount,
        CancellationToken cancellationToken)
    {
        var clips = request.AvailableClips;
        var placements = new List<TimelinePlacement>();
        var trace = new List<TimelinePlanTraceEntry>();
        var tracker = new PlacementTracker(request, effectiveMaximumReuseCount);

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
            var usageOrdinal = tracker.UsageCount(clipIndex) + 1;

            if (usageOrdinal > request.MaximumReuseCount)
            {
                relaxed.Add(RelaxedConstraint.MaximumReuseCount);
            }

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
                UsageOrdinal = usageOrdinal,
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
        var maxUsageOrdinal = placements.Count == 0 ? 0 : placements.Max(p => p.UsageOrdinal);

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
            FeasibilityWarning = BuildFeasibilityWarning(isComplete, quantizedTarget, plannedDuration, clips.Count, request.MaximumReuseCount, effectiveMaximumReuseCount, maxUsageOrdinal),
        };
    }

    private static readonly IReadOnlyList<RelaxedConstraint>[] TierRelaxations =
    [
        [],
        [RelaxedConstraint.VisualClusterAdjacencyLimit],
        [RelaxedConstraint.VisualClusterAdjacencyLimit, RelaxedConstraint.OriginalNeighborSeparation],
        [RelaxedConstraint.VisualClusterAdjacencyLimit, RelaxedConstraint.OriginalNeighborSeparation, RelaxedConstraint.MinimumRepeatDistance],
    ];

    private static TimelineFeasibilityWarning? BuildFeasibilityWarning(
        bool isComplete,
        TimeSpan target,
        TimeSpan achieved,
        int clipCount,
        int requestedMaximumReuseCount,
        int effectiveMaximumReuseCount,
        int maxUsageOrdinal)
    {
        if (isComplete)
        {
            if (maxUsageOrdinal <= requestedMaximumReuseCount)
            {
                return null;
            }

            var repetitionMessage = string.Format(
                CultureInfo.InvariantCulture,
                "Target duration {0}s was reached exactly, but only by allowing clips to repeat up to {1} time(s) - {2} more than the requested maximum of {3} - because {4} clip(s) were not enough to cover it otherwise. Significant repetition was needed to match audio length.",
                FormatSeconds(target),
                maxUsageOrdinal,
                maxUsageOrdinal - requestedMaximumReuseCount,
                requestedMaximumReuseCount,
                clipCount);

            return new TimelineFeasibilityWarning
            {
                Kind = TimelineFeasibilityWarningKind.SignificantRepetition,
                Message = repetitionMessage,
                TargetDuration = target,
                AchievedDuration = achieved,
                Shortfall = TimeSpan.Zero,
                RequestedMaximumReuseCount = requestedMaximumReuseCount,
                EffectiveMaximumReuseCount = effectiveMaximumReuseCount,
            };
        }

        var shortfall = target - achieved;
        var shortfallMessage = string.Format(
            CultureInfo.InvariantCulture,
            "Requested {0}s but only {1}s is achievable from {2} clip(s) even after relaxing the maximum reuse count to {3} (from a requested {4}) and every placement-spacing constraint (shortfall {5}s).",
            FormatSeconds(target),
            FormatSeconds(achieved),
            clipCount,
            effectiveMaximumReuseCount,
            requestedMaximumReuseCount,
            FormatSeconds(shortfall));

        return new TimelineFeasibilityWarning
        {
            Kind = TimelineFeasibilityWarningKind.Shortfall,
            Message = shortfallMessage,
            TargetDuration = target,
            AchievedDuration = achieved,
            Shortfall = shortfall,
            RequestedMaximumReuseCount = requestedMaximumReuseCount,
            EffectiveMaximumReuseCount = effectiveMaximumReuseCount,
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
        private readonly int _effectiveMaximumReuseCount;
        private readonly IReadOnlyList<CleanClip> _clips;
        private readonly int[] _rank;
        private readonly int[] _usageCount;
        private readonly int?[] _lastPositionByClip;
        private readonly Dictionary<int, int> _lastPositionByScene = [];
        private readonly Dictionary<int, int> _lastPositionByCluster = [];

        public PlacementTracker(TimelinePlanRequest request, int effectiveMaximumReuseCount)
        {
            _request = request;
            _effectiveMaximumReuseCount = effectiveMaximumReuseCount;
            _clips = request.AvailableClips;
            _rank = ClipShuffleOrder.ComputeRanks(_clips.Count, request.Seed);
            _usageCount = new int[_clips.Count];
            _lastPositionByClip = new int?[_clips.Count];
        }

        public int UsageCount(int clipIndex) => _usageCount[clipIndex];

        public int Rank(int clipIndex) => _rank[clipIndex];

        // The least-used-first tie-break among clips at equal usage count
        // uses this instead of the raw seeded rank directly, so that later
        // "laps" through the pool (once every clip has been used the same
        // number of times, the point at which the whole eligible set ties
        // on usage count again) pick a different relative order than the
        // first lap did, rather than replaying the identical rank order
        // every time reuse becomes necessary. Rotating by usageCount is
        // still a pure function of the one seeded shuffle computed for this
        // plan (no extra randomness, so Plan_SameSeedAndInputs_ProducesIdenticalPlan
        // still holds) and is a no-op on every clip's very first use
        // (usageCount 0 => key == rank[c] exactly), so it changes nothing
        // about which unique clip is placed first - only how repeated,
        // heavily-relaxed reuse is ordered.
        private int RotatingTieBreakKey(int clipIndex) => (_rank[clipIndex] + _usageCount[clipIndex]) % _clips.Count;

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
                    .ThenBy(c => RotatingTieBreakKey(c))
                    .Cast<int?>()
                    .FirstOrDefault();

                if (finalCandidate is int chosenFinal)
                {
                    return (chosenFinal, tier, true);
                }

                var chosen = eligible
                    .OrderBy(c => _usageCount[c])
                    .ThenBy(c => RotatingTieBreakKey(c))
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
            if (_usageCount[clipIndex] >= _effectiveMaximumReuseCount)
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
