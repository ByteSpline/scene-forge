using System.Text.Json;
using SceneForge.Accuracy.Json;
using SceneForge.Accuracy.Profiling;

namespace SceneForge.Accuracy.Reporting;

public static class PipelineProfileReportJsonWriter
{
    public static async Task WriteAsync(IReadOnlyList<PipelineProfileReport> reports, string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, reports, AccuracyJsonOptions.Options, cancellationToken).ConfigureAwait(false);
    }
}
