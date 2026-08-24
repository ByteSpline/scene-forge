using SceneForge.Accuracy.Evaluation;

namespace SceneForge.Accuracy.Reporting;

public static class ConsoleReportPrinter
{
    public static void Print(EvaluationReport report)
    {
        Console.WriteLine($"SceneForge.Accuracy report - profile={report.Profile}, fixtures={report.FixtureCount}, capturedAtUtc={report.CapturedAtUtc:u}, commit={report.CommitSha ?? "unknown"}");
        Console.WriteLine();
        Console.WriteLine($"{"Group",-20} {"TP",4} {"FN",4} {"FP",4} {"Recall",8} {"Precision",9} {"F1",8} {"BoundaryErrMs",13} {"FP/min",7}");

        foreach (var row in report.Metrics.Where(m => m.Group is not null).OrderBy(m => m.Group))
        {
            PrintRow(row.Group!.ToString()!, row);
        }

        Console.WriteLine(new string('-', 90));
        PrintRow("AGGREGATE", report.Aggregate);
        Console.WriteLine();

        Console.WriteLine("Resource usage / throughput (never gates CI - see RegressionGate remarks):");
        Console.WriteLine($"  Throughput:            {report.ThroughputSourceSecondsPerWallClockSecond:F2} source-seconds analyzed per wall-clock second");
        Console.WriteLine($"  Total wall clock:      {report.TotalWallClockSeconds:F2} s");
        Console.WriteLine($"  Peak managed memory:   {FormatBytes(report.PeakManagedMemoryBytes)}");
        Console.WriteLine($"  Peak working set:      {FormatBytes(report.PeakWorkingSetBytes)}");
    }

    public static void PrintGateResult(RegressionGateResult result)
    {
        Console.WriteLine();
        Console.WriteLine(result.Passed ? "Regression gate: PASSED (no correctness regression vs. baseline)." : "Regression gate: FAILED - correctness regression(s) found:");
        foreach (var failure in result.CorrectnessFailures)
        {
            Console.WriteLine($"  - {failure}");
        }

        if (result.PerformanceNotes.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Performance notes (informational only, never gate CI):");
            foreach (var note in result.PerformanceNotes)
            {
                Console.WriteLine($"  - {note}");
            }
        }
    }

    private static void PrintRow(string label, GroupMetrics metrics)
    {
        Console.WriteLine(
            $"{label,-20} {metrics.TruePositives,4} {metrics.FalseNegatives,4} {metrics.FalsePositives,4} " +
            $"{FormatRate(metrics.Recall),8} {FormatRate(metrics.Precision),9} {FormatRate(metrics.F1),8} " +
            $"{FormatMs(metrics.MeanBoundaryErrorMs),13} {FormatRate(metrics.FalsePositivesPerMinute, isCount: true),7}");
    }

    private static string FormatRate(double value, bool isCount = false) =>
        double.IsNaN(value) ? "n/a" : isCount ? value.ToString("F2") : value.ToString("P0");

    private static string FormatMs(double value) => double.IsNaN(value) ? "n/a" : value.ToString("F1");

    private static string FormatBytes(long bytes) => $"{bytes / 1024.0 / 1024.0:F1} MB";
}
