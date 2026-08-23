using SceneForge.Media.Domain;

namespace SceneForge.Media.Extraction.Intervals;

// Turns remaining (post-subtraction) ranges into 3-5 second clip candidate
// windows: trims a boundary guard off both ends first (absorbing any
// residual soft edge right at an exclusion boundary), then slides a
// fixed-duration window across what is left. Pure TimeSpan arithmetic (no
// floating-point seconds anywhere), so results are exact and deterministic -
// every candidate this produces is exactly options.MaxClipDuration long
// where the guarded range allows it, down to options.MinClipDuration where
// it does not; a guarded range shorter than MinClipDuration yields no
// candidate at all rather than a too-short partial clip.
internal static class ClipCandidateGenerator
{
    public static IReadOnlyList<IndexedTimeRange> Generate(
        IReadOnlyList<IndexedTimeRange> remainingRanges,
        CleanClipScoringOptions options)
    {
        ArgumentNullException.ThrowIfNull(remainingRanges);
        ArgumentNullException.ThrowIfNull(options);

        var candidates = new List<IndexedTimeRange>();

        foreach (var remaining in remainingRanges)
        {
            candidates.AddRange(GenerateForRange(remaining, options));
        }

        return candidates.OrderBy(c => c.Range.Start).ToList();
    }

    private static IEnumerable<IndexedTimeRange> GenerateForRange(IndexedTimeRange remaining, CleanClipScoringOptions options)
    {
        var guardedStart = remaining.Range.Start + options.BoundaryGuard;
        var guardedEnd = remaining.Range.End - options.BoundaryGuard;

        if (guardedEnd <= guardedStart)
        {
            yield break;
        }

        var guardedLength = guardedEnd - guardedStart;
        if (guardedLength < options.MinClipDuration)
        {
            yield break;
        }

        var clipDuration = guardedLength < options.MaxClipDuration ? guardedLength : options.MaxClipDuration;
        var stride = TimeSpan.FromTicks((long)(clipDuration.Ticks * (1.0 - options.OverlapFraction)));
        if (stride <= TimeSpan.Zero)
        {
            stride = clipDuration;
        }

        var cursor = guardedStart;
        while (cursor + clipDuration <= guardedEnd)
        {
            yield return new IndexedTimeRange(remaining.SourceSceneIndex, new TimeRange(cursor, cursor + clipDuration));
            cursor += stride;
        }
    }
}
