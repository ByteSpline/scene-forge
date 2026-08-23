namespace SceneForge.Media.Detection.Classification;

// Shared "find every maximal run of consecutive indices satisfying a
// predicate, at least minLength long" scan used by the motion-based
// classifiers (Zoom, DirectionalSwipe). Kept as one small, independently
// testable helper rather than duplicated inline in each classifier.
internal static class ContiguousRunFinder
{
    public static IReadOnlyList<(int Start, int End)> FindRuns(int count, Func<int, bool> predicate, int minLength)
    {
        var runs = new List<(int Start, int End)>();
        var runStart = -1;

        for (var i = 0; i < count; i++)
        {
            if (predicate(i))
            {
                if (runStart < 0)
                {
                    runStart = i;
                }
            }
            else if (runStart >= 0)
            {
                AddIfLongEnough(runs, runStart, i - 1, minLength);
                runStart = -1;
            }
        }

        if (runStart >= 0)
        {
            AddIfLongEnough(runs, runStart, count - 1, minLength);
        }

        return runs;
    }

    private static void AddIfLongEnough(List<(int Start, int End)> runs, int start, int end, int minLength)
    {
        if (end - start + 1 >= minLength)
        {
            runs.Add((start, end));
        }
    }
}
