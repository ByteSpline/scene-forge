using SceneForge.Media.Detection.Classification;

namespace SceneForge.Media.Detection.Fusion;

// Resolves the raw candidate stream (which, by design, can contain many
// exact or overlapping duplicates - every classifier re-scans the sliding
// window on every new sample, see ClassifierWindow) into the final,
// non-overlapping TransitionDetection list: exact duplicates collapse via
// value equality, overlapping/near-overlapping (within MergeGapTolerance)
// candidates of possibly different types merge into one detection (the
// highest-confidence candidate's type wins; the rest are folded into the
// diagnostic reason, never silently dropped), and the configured pre/post
// safety buffers are applied last. This is also where BoundaryTimestamp -
// a single recommended splice point, distinct from the [Start, End]
// contaminated interval - is computed; see TransitionDetection's remarks
// for why that distinction is a hard project requirement, not an API
// nicety.
internal static class TransitionFuser
{
    // Small tie-break bonus (not a hard override - a 0.6-confidence Dissolve
    // still loses to a 0.9-confidence FadeToBlack without any bonus at all)
    // applied only when picking which overlapping candidate's type wins a
    // merge, ranked by how specific/hard-to-fake each classifier's evidence
    // is. HardCut/FadeToBlack/FadeFromBlack/Flash reach an extreme, sharply-
    // defined state (an isolated spike, or a peak that actually reaches
    // near-black/near-white) - the strongest evidence. ZoomTransition/
    // DirectionalSwipe/BlurTransition require two corroborating conditions
    // each (magnitude + a coherent motion shape, or a variance drop + an
    // edge-density drop) - moderately specific. Dissolve requires only a
    // sustained elevated StructuralDifference, the broadest, most easily
    // coincidentally satisfied pattern of the seven - no bonus, so it never
    // wins a tie against a more specific alternative. Measured against the
    // synthetic fixture matrix (docs/PHASE_06_REPORT.md): several fixtures
    // built to exercise one specific classifier also produce a plausible
    // (if weaker) Dissolve candidate over the same interval purely because
    // StructuralDifference rises during most kinds of transitions; without
    // this ranking, that generic Dissolve false alarm could occasionally
    // out-score and silently replace the correct, more specific detection.
    private static readonly IReadOnlyDictionary<TransitionType, double> SpecificityBonus = new Dictionary<TransitionType, double>
    {
        [TransitionType.HardCut] = 0.2,
        [TransitionType.FadeToBlack] = 0.2,
        [TransitionType.FadeFromBlack] = 0.2,
        [TransitionType.Flash] = 0.2,
        [TransitionType.ZoomTransition] = 0.1,
        [TransitionType.DirectionalSwipe] = 0.1,
        [TransitionType.BlurTransition] = 0.1,
    };

    public static IReadOnlyList<TransitionDetection> Fuse(
        IReadOnlyList<TransitionCandidate> candidates,
        TransitionDetectionProfile profile,
        TimeSpan? videoDuration = null)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var sorted = candidates.Distinct().OrderBy(c => c.Start).ToList();
        var groups = new List<List<TransitionCandidate>>();

        foreach (var candidate in sorted)
        {
            var lastGroup = groups.Count > 0 ? groups[^1] : null;
            if (lastGroup is not null && CanMergeWithGroup(candidate, lastGroup, profile.MergeGapTolerance))
            {
                lastGroup.Add(candidate);
            }
            else
            {
                groups.Add([candidate]);
            }
        }

        return groups.Select(group => BuildDetection(group, profile, videoDuration)).ToList();
    }

    // Same-type candidates may merge across a small gap (MergeGapTolerance)
    // - this is what collapses the many near-identical duplicates every
    // classifier produces as ClassifierWindow slides (see that type's
    // remarks) into one detection, and absorbs minor timing jitter between
    // repeated re-classifications of the same real event. Different-type
    // candidates only merge when their intervals genuinely overlap (not
    // merely touch at a shared boundary instant): FadeBlackClassifier's
    // FadeToBlack and FadeFromBlack candidates are a real, designed
    // adjacent pair sharing exactly one instant (the black peak) as
    // FadeToBlack's End and FadeFromBlack's Start - gap-tolerant merging
    // would silently collapse that genuinely-two-events pair into one,
    // discarding whichever type lost the confidence tie-break entirely
    // rather than folding it in as corroborating evidence. Requiring
    // strict overlap for cross-type merges preserves that pair as two
    // detections while still merging genuinely-competing alternative
    // classifications of the same interval.
    private static bool CanMergeWithGroup(TransitionCandidate candidate, List<TransitionCandidate> group, TimeSpan gapTolerance)
    {
        var groupEnd = group.Max(c => c.End);
        return group.Any(c => c.Type == candidate.Type)
            ? candidate.Start <= groupEnd + gapTolerance
            : candidate.Start < groupEnd;
    }

    private static TransitionDetection BuildDetection(
        List<TransitionCandidate> group,
        TransitionDetectionProfile profile,
        TimeSpan? videoDuration)
    {
        var winner = group.OrderByDescending(c => c.Confidence + SpecificityScore(c.Type)).First();
        var start = group.Min(c => c.Start);
        var end = group.Max(c => c.End);

        var bufferedStart = Max(start - profile.PreBufferDuration, TimeSpan.Zero);
        var bufferedEnd = end + profile.PostBufferDuration;
        if (videoDuration.HasValue)
        {
            bufferedEnd = Min(bufferedEnd, videoDuration.Value);
        }

        var boundary = Clamp(winner.Peak, bufferedStart, bufferedEnd);

        var contributingSignals = new Dictionary<string, double>();
        foreach (var kv in winner.ContributingSignals)
        {
            contributingSignals[kv.Key] = kv.Value;
        }

        var others = group.Where(c => !ReferenceEquals(c, winner) && c != winner).ToList();
        foreach (var other in others)
        {
            foreach (var kv in other.ContributingSignals)
            {
                contributingSignals[$"{other.Type}.{kv.Key}"] = kv.Value;
            }
        }

        var reason = others.Count == 0
            ? winner.DiagnosticReason
            : $"{winner.DiagnosticReason} Merged with {others.Count} overlapping candidate(s): " +
              string.Join("; ", others.Select(c => $"{c.Type} ({c.DiagnosticReason})"));

        return new TransitionDetection
        {
            Type = winner.Type,
            Start = bufferedStart,
            Peak = winner.Peak,
            End = bufferedEnd,
            BoundaryTimestamp = boundary,
            Confidence = winner.Confidence,
            ContributingSignals = contributingSignals,
            DiagnosticReason = reason,
        };
    }

    private static double SpecificityScore(TransitionType type) => SpecificityBonus.GetValueOrDefault(type, 0.0);

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max) => value < min ? min : value > max ? max : value;
}
