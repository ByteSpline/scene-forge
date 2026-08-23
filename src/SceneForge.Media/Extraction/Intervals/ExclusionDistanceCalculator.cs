using SceneForge.Media.Domain;

namespace SceneForge.Media.Extraction.Intervals;

// Pure helper feeding ClipScorer's TransitionDistance factor: the gap from
// a candidate to its single nearest excluded interval. Candidates are
// generated only from post-subtraction remainders, so this should never
// see a genuine overlap - the TimeSpan.Zero fallback is a defensive floor,
// not an expected path.
internal static class ExclusionDistanceCalculator
{
    // No exclusions at all is the "infinitely safe" case - represented by a
    // large-but-finite TimeSpan so callers never have to special-case
    // TimeSpan.MaxValue arithmetic overflowing when added/subtracted.
    public static readonly TimeSpan NoExclusionsDistance = TimeSpan.FromDays(1);

    public static TimeSpan NearestDistance(TimeRange candidate, IReadOnlyList<TimeRange> exclusions)
    {
        ArgumentNullException.ThrowIfNull(exclusions);

        if (exclusions.Count == 0)
        {
            return NoExclusionsDistance;
        }

        var nearest = NoExclusionsDistance;
        foreach (var exclusion in exclusions)
        {
            TimeSpan gap;
            if (exclusion.End <= candidate.Start)
            {
                gap = candidate.Start - exclusion.End;
            }
            else if (exclusion.Start >= candidate.End)
            {
                gap = exclusion.Start - candidate.End;
            }
            else
            {
                gap = TimeSpan.Zero;
            }

            if (gap < nearest)
            {
                nearest = gap;
            }
        }

        return nearest;
    }
}
