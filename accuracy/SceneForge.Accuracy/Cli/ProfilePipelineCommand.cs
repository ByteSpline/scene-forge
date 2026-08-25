using SceneForge.Accuracy.Profiling;
using SceneForge.Accuracy.Reporting;
using SceneForge.Media.Sampling;

namespace SceneForge.Accuracy.Cli;

// Full end-to-end pipeline resource profiling (throughput/CPU/memory/disk),
// distinct from evaluate/gate (32-fixture correctness scoring). Defaults to
// building/reusing one cached ~30-minute 1920x1080 synthetic source (see
// SyntheticProfilingSourceBuilder) rather than requiring the caller to
// supply real footage, so this is runnable without any real video on hand -
// pass --input to point at a real file instead.
public static class ProfilePipelineCommand
{
    public static async Task<int> RunAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        var applicationBaseDirectory = options.GetOrDefault("ffmpeg-base-dir") ?? AppContext.BaseDirectory;
        var ffmpegPath = Path.Combine(applicationBaseDirectory, "tools", "ffmpeg", "ffmpeg.exe");
        if (!File.Exists(ffmpegPath))
        {
            throw new InvalidOperationException(
                $"ffmpeg.exe was not found at '{ffmpegPath}'. Pass --ffmpeg-base-dir <dir> pointing at a directory containing tools/ffmpeg/ffmpeg.exe.");
        }

        var inputPath = options.GetOrDefault("input");
        if (inputPath is null)
        {
            var cachePath = options.GetOrDefault("source-cache")
                ?? Path.Combine(Path.GetTempPath(), "sceneforge-profiling", "profiling-source.mp4");
            var regenerate = options.GetOrDefault("regenerate-source") is not null;
            var builder = new SyntheticProfilingSourceBuilder(ffmpegPath);
            Console.WriteLine(File.Exists(cachePath) && !regenerate
                ? $"Reusing cached synthetic profiling source at '{cachePath}'."
                : $"Building synthetic profiling source at '{cachePath}' (this takes several minutes)...");
            inputPath = await builder.BuildAsync(cachePath, regenerate, cancellationToken).ConfigureAwait(false);
        }

        var profiles = ParseProfiles(options.GetOrDefault("profile"));
        var reports = new List<PipelineProfileReport>();

        foreach (var profile in profiles)
        {
            Console.WriteLine();
            Console.WriteLine($"=== Profiling pipeline at {profile} ===");
            var report = await PipelineProfiler.RunAsync(applicationBaseDirectory, inputPath, profile, cancellationToken).ConfigureAwait(false);
            PipelineProfileConsolePrinter.Print(report);
            reports.Add(report);
        }

        var reportPath = options.GetOrDefault("report");
        if (reportPath is not null)
        {
            await PipelineProfileReportJsonWriter.WriteAsync(reports, reportPath, cancellationToken).ConfigureAwait(false);
            Console.WriteLine();
            Console.WriteLine($"Wrote full report to '{reportPath}'.");
        }

        return 0;
    }

    private static IReadOnlyList<AnalysisProfile> ParseProfiles(string? value) => value switch
    {
        null or "All" or "all" => [AnalysisProfile.Fast, AnalysisProfile.Balanced, AnalysisProfile.Accurate],
        _ => [Enum.Parse<AnalysisProfile>(value, ignoreCase: true)],
    };
}
