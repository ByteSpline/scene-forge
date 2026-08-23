using SceneForge.Media.Domain;

namespace SceneForge.Media.Extraction.Intervals;

// Pure interval math: subtracts a set of excluded intervals from a set of
// scene ranges, producing the "clean" remainder. No I/O, no OpenCvSharp -
// deterministic and independently testable against hand-built TimeRanges.
// This is where CLAUDE.md's "never attempt to reconstruct frames hidden by
// an effect" rule is structurally enforced: every downstream candidate is
// generated only from what this method returns, so an excluded interval can
// never leak into a clip candidate by construction.
internal static class IntervalSubtractor
{
    // Subtracts the union of exclusions from every scene range. Exclusions
    // may be unsorted and may overlap each other (including exactly
    // duplicate or touching intervals) - they are merged first. A scene
    // range entirely covered by exclusions yields no remainder for that
    // range; boundaries are treated as inclusive-touching-is-not-overlap
    // (same semantics as TimeRange.Overlaps), so an exclusion that ends
    // exactly where a scene range begins consumes none of it.
    public static IReadOnlyList<IndexedTimeRange> Subtract(
        IReadOnlyList<TimeRange> sceneRanges,
        IReadOnlyList<TimeRange> exclusions)
    {
        ArgumentNullException.ThrowIfNull(sceneRanges);
        ArgumentNullException.ThrowIfNull(exclusions);

        var mergedExclusions = MergeOverlapping(exclusions);
        var result = new List<IndexedTimeRange>();

        for (var sceneIndex = 0; sceneIndex < sceneRanges.Count; sceneIndex++)
        {
            foreach (var remainder in SubtractFromRange(sceneRanges[sceneIndex], mergedExclusions))
            {
                result.Add(new IndexedTimeRange(sceneIndex, remainder));
            }
        }

        return result;
    }

    // Sorts by Start and merges any exclusions that overlap OR touch
    // (next.Start <= currentEnd) into one contiguous interval, so the
    // single-pass sweep in SubtractFromRange never has to reason about
    // multiple exclusions covering the same instant.
    private static List<TimeRange> MergeOverlapping(IReadOnlyList<TimeRange> exclusions)
    {
        if (exclusions.Count == 0)
        {
            return [];
        }

        var sorted = exclusions.OrderBy(e => e.Start).ToList();
        var merged = new List<TimeRange> { sorted[0] };

        for (var i = 1; i < sorted.Count; i++)
        {
            var current = sorted[i];
            var last = merged[^1];

            if (current.Start <= last.End)
            {
                if (current.End > last.End)
                {
                    merged[^1] = new TimeRange(last.Start, current.End);
                }
            }
            else
            {
                merged.Add(current);
            }
        }

        return merged;
    }

    private static IEnumerable<TimeRange> SubtractFromRange(TimeRange sceneRange, IReadOnlyList<TimeRange> mergedExclusions)
    {
        var cursor = sceneRange.Start;

        foreach (var exclusion in mergedExclusions)
        {
            // Strict comparisons match TimeRange.Overlaps: an exclusion
            // that only touches sceneRange at a shared boundary instant
            // does not consume any of it (off-by-one safety - this is what
            // stops a boundary-touching exclusion from producing a
            // zero-length remainder).
            if (exclusion.End <= sceneRange.Start || exclusion.Start >= sceneRange.End)
            {
                continue;
            }

            var clippedStart = Max(exclusion.Start, sceneRange.Start);
            var clippedEnd = Min(exclusion.End, sceneRange.End);

            if (clippedStart > cursor)
            {
                yield return new TimeRange(cursor, clippedStart);
            }

            cursor = Max(cursor, clippedEnd);
        }

        if (cursor < sceneRange.End)
        {
            yield return new TimeRange(cursor, sceneRange.End);
        }
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
}
