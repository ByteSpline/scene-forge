using SceneForge.Accuracy.Evaluation;
using SceneForge.Accuracy.Reporting;

namespace SceneForge.Accuracy.Cli;

// The sanctioned, explicit way to move the committed baseline forward after
// an intentional accuracy change - never done implicitly by evaluate/gate.
public static class UpdateBaselineCommand
{
    public static async Task<int> RunAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        var outputPath = options.Require("output");

        var report = await EvaluateCommand.RunEvaluationAsync(options, cancellationToken).ConfigureAwait(false);
        ConsoleReportPrinter.Print(report);

        var baseline = new RegressionBaseline(await HardwareDescriber.DescribeAsync(cancellationToken).ConfigureAwait(false), report);
        await RegressionBaselineJson.WriteAsync(baseline, outputPath, cancellationToken).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine($"Wrote regression baseline to '{outputPath}' (hardware: {baseline.Hardware.CpuName}, {baseline.Hardware.LogicalProcessorCount} logical processors, {baseline.Hardware.TotalMemoryGigabytes:F1} GB).");
        return 0;
    }
}
