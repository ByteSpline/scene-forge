namespace SceneForge.Media.Planning.Internal;

// A deterministic tie-break rank per clip index, derived from a seeded
// Fisher-Yates shuffle - the only source of randomness anywhere in
// TimelinePlanner. Computed once per Plan call, never re-seeded mid-run, so
// every tie-break throughout one plan (and, given the same seed and clip
// count, across repeated runs) is decided consistently. Kept as its own
// pure, independently testable unit rather than inlined into TimelinePlanner,
// mirroring how Extraction.Clustering.PerceptualDistance is factored out of
// VisualClusterer.
internal static class ClipShuffleOrder
{
    // Returns rank[i] = this clip index's position in a seed-driven random
    // shuffle of [0, clipCount) - a permutation, so every rank in
    // [0, clipCount) appears exactly once. Two calls with the same seed and
    // clipCount always return an identical array (System.Random with an
    // explicit seed produces the same sequence for a given .NET major
    // version - see docs/PHASE_08_REPORT.md, Design summary, for the
    // determinism scope this implies).
    public static int[] ComputeRanks(int clipCount, int seed)
    {
        if (clipCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(clipCount), clipCount, "Clip count must not be negative.");
        }

        var order = new int[clipCount];
        for (var i = 0; i < clipCount; i++)
        {
            order[i] = i;
        }

        var random = new Random(seed);
        for (var i = clipCount - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        var rank = new int[clipCount];
        for (var position = 0; position < clipCount; position++)
        {
            rank[order[position]] = position;
        }

        return rank;
    }
}
