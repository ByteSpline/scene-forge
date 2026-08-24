using SceneForge.Accuracy.Evaluation;
using SceneForge.Accuracy.Reporting;
using SceneForge.Media.Sampling;

namespace SceneForge.Accuracy.Cli;

public static class EvaluateCommand
{
    public static async Task<int> RunAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        var report = await RunEvaluationAsync(options, cancellationToken).ConfigureAwait(false);
        ConsoleReportPrinter.Print(report);

        var reportPath = options.GetOrDefault("report");
        if (reportPath is not null)
        {
            await EvaluationReportJsonWriter.WriteAsync(report, reportPath, cancellationToken).ConfigureAwait(false);
            Console.WriteLine();
            Console.WriteLine($"Wrote full report to '{reportPath}'.");
        }

        return 0;
    }

    // Shared by gate/update-baseline - both need the same freshly-run
    // report before doing something different with it.
    public static async Task<EvaluationReport> RunEvaluationAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        var applicationBaseDirectory = options.GetOrDefault("ffmpeg-base-dir") ?? AppContext.BaseDirectory;
        var profile = ParseProfile(options.GetOrDefault("profile"));

        var ffmpegPath = Path.Combine(applicationBaseDirectory, "tools", "ffmpeg", "ffmpeg.exe");
        if (!File.Exists(ffmpegPath))
        {
            throw new InvalidOperationException(
                $"ffmpeg.exe was not found at '{ffmpegPath}'. Pass --ffmpeg-base-dir <dir> pointing at a directory containing tools/ffmpeg/ffmpeg.exe.");
        }

        return await FixtureEvaluationRunner.RunAsync(applicationBaseDirectory, profile, cancellationToken).ConfigureAwait(false);
    }

    private static AnalysisProfile ParseProfile(string? value) =>
        value is null ? AnalysisProfile.Accurate : Enum.Parse<AnalysisProfile>(value, ignoreCase: true);
}
