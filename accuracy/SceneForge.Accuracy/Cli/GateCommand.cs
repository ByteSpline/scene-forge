using SceneForge.Accuracy.Evaluation;
using SceneForge.Accuracy.Reporting;

namespace SceneForge.Accuracy.Cli;

// What CI calls. Exit code reflects correctness only - see RegressionGate's
// own remarks for why performance/resource drift never fails this.
public static class GateCommand
{
    public static async Task<int> RunAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        var baselinePath = options.Require("baseline");
        var baseline = await RegressionBaselineJson.ReadAsync(baselinePath, cancellationToken).ConfigureAwait(false);

        var report = await EvaluateCommand.RunEvaluationAsync(options, cancellationToken).ConfigureAwait(false);
        ConsoleReportPrinter.Print(report);

        var result = RegressionGate.Evaluate(report, baseline);
        ConsoleReportPrinter.PrintGateResult(result);

        var reportPath = options.GetOrDefault("report");
        if (reportPath is not null)
        {
            await EvaluationReportJsonWriter.WriteAsync(report, reportPath, cancellationToken).ConfigureAwait(false);
        }

        return result.Passed ? 0 : 1;
    }
}
