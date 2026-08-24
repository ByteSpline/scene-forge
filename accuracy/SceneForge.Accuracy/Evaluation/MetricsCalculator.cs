namespace SceneForge.Accuracy.Evaluation;

// Pure precision/recall/F1/boundary-error/false-positives-per-minute math,
// per FixtureGroup and aggregated across all of them. No ffmpeg, no file
// I/O, no OpenCvSharp - MetricsCalculatorTests exercises this directly with
// hand-built match data, so it always runs in CI regardless of whether real
// ffmpeg is available (CLAUDE.md rule 8: test-first for new algorithmic
// behavior).
public static class MetricsCalculator
{
    public static IReadOnlyList<GroupMetrics> Compute(IReadOnlyList<GroupEvaluationInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var results = new List<GroupMetrics>(inputs.Count + 1);
        foreach (var input in inputs)
        {
            results.Add(ComputeFor(input.Group, input.Matches, input.FalsePositiveCount, input.TotalSourceSeconds));
        }

        var allMatches = inputs.SelectMany(i => i.Matches).ToList();
        var totalFalsePositives = inputs.Sum(i => i.FalsePositiveCount);
        var totalSeconds = inputs.Sum(i => i.TotalSourceSeconds);
        results.Add(ComputeFor(null, allMatches, totalFalsePositives, totalSeconds));

        return results;
    }

    private static GroupMetrics ComputeFor(
        Fixtures.FixtureGroup? group,
        IReadOnlyList<TransitionMatchOutcome> matches,
        int falsePositiveCount,
        double totalSourceSeconds)
    {
        var truePositives = matches.Count(m => m.TruePositive);
        var falseNegatives = matches.Count - truePositives;

        // Recall is "not applicable" (not zero) for a distractor group -
        // there were no expected transitions to recall in the first place.
        var recall = matches.Count == 0 ? double.NaN : (double)truePositives / matches.Count;

        // Precision is "not applicable" only when nothing was ever
        // detected (TP + FP == 0). A distractor group with false positives
        // and zero true positives still has a well-defined precision of 0.
        var precision = truePositives + falsePositiveCount == 0
            ? double.NaN
            : (double)truePositives / (truePositives + falsePositiveCount);

        var f1 = double.IsNaN(precision) || double.IsNaN(recall) || precision + recall == 0
            ? double.NaN
            : 2 * precision * recall / (precision + recall);

        var boundaryErrors = matches
            .Where(m => m.TruePositive && m.BoundaryErrorMs.HasValue)
            .Select(m => m.BoundaryErrorMs!.Value)
            .ToList();
        var meanBoundaryErrorMs = boundaryErrors.Count == 0 ? double.NaN : boundaryErrors.Average();

        var falsePositivesPerMinute = totalSourceSeconds <= 0
            ? double.NaN
            : falsePositiveCount / (totalSourceSeconds / 60.0);

        return new GroupMetrics(
            group,
            truePositives,
            falseNegatives,
            falsePositiveCount,
            recall,
            precision,
            f1,
            meanBoundaryErrorMs,
            falsePositivesPerMinute);
    }
}
