using SceneForge.Accuracy.Profiling;

namespace SceneForge.Accuracy.Reporting;

public static class PipelineProfileConsolePrinter
{
    public static void Print(PipelineProfileReport report)
    {
        Console.WriteLine($"Profile: {report.Profile}");
        Console.WriteLine($"Input:   {report.InputFilePath} ({report.InputDurationSeconds:F1}s)");
        Console.WriteLine();
        Console.WriteLine($"  Detect:  {report.Detect.WallClockSeconds,8:F2} s  ({report.Detect.Notes})");
        Console.WriteLine($"  Extract: {report.Extract.WallClockSeconds,8:F2} s  ({report.Extract.Notes})");
        if (report.Render is { } render)
        {
            Console.WriteLine($"  Render:  {render.WallClockSeconds,8:F2} s  ({render.Notes})");
        }
        else
        {
            Console.WriteLine("  Render:  skipped (no accepted clips to render)");
        }

        Console.WriteLine();
        Console.WriteLine($"  Detections found:      {report.DetectionsFound}");
        Console.WriteLine($"  Clips accepted/rejected: {report.ClipsAccepted}/{report.ClipsRejected}");
        Console.WriteLine($"  Render output valid:   {(report.RenderOutputValid is { } valid ? valid.ToString() : "n/a")}");
        Console.WriteLine();
        Console.WriteLine($"  Total wall clock:      {report.TotalWallClockSeconds:F2} s");
        Console.WriteLine($"  Throughput:            {report.ThroughputSourceSecondsPerWallClockSecond:F2} source-seconds analyzed per wall-clock second");
        Console.WriteLine($"  Process CPU time:      {report.ProcessCpuTimeSeconds:F2} s (this process only - see PipelineProfileReport remarks)");
        Console.WriteLine($"  Peak managed memory:   {report.PeakManagedMemoryBytes / 1024.0 / 1024.0:F1} MB");
        Console.WriteLine($"  Peak working set:      {report.PeakWorkingSetBytes / 1024.0 / 1024.0:F1} MB");
        Console.WriteLine($"  Free disk before/after: {report.FreeDiskBytesBeforeOutputDrive / 1024.0 / 1024.0:F0} MB / {report.FreeDiskBytesAfterOutputDrive / 1024.0 / 1024.0:F0} MB");
    }
}
