using System.Text.Json;
using SceneForge.Accuracy.Json;

namespace SceneForge.Accuracy.Evaluation;

public static class RegressionBaselineJson
{
    public static async Task WriteAsync(RegressionBaseline baseline, string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, baseline, AccuracyJsonOptions.Options, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<RegressionBaseline> ReadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var baseline = await JsonSerializer.DeserializeAsync<RegressionBaseline>(stream, AccuracyJsonOptions.Options, cancellationToken).ConfigureAwait(false);
        return baseline ?? throw new InvalidOperationException($"'{path}' did not contain a valid regression baseline.");
    }
}
