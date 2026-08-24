using System.Text.Json;
using SceneForge.Accuracy.Evaluation;
using SceneForge.Accuracy.Json;

namespace SceneForge.Accuracy.Reporting;

// Plain overwrite (FileMode.Create), unlike the app's own export writers
// (e.g. TransitionDetectionJsonWriter's FileMode.CreateNew never-overwrite
// policy). CLAUDE.md rules 11/12 protect a *user's own media files* from
// silent loss - this is a dev tool's own diagnostic report, regenerated
// fresh on every run by design (evaluate/gate always rebuild the fixture
// matrix from scratch), so overwriting it is the expected, intended
// behavior, not a risk to guard against.
public static class EvaluationReportJsonWriter
{
    public static async Task WriteAsync(EvaluationReport report, string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, report, AccuracyJsonOptions.Options, cancellationToken).ConfigureAwait(false);
    }
}
