using System.Globalization;
using SceneForge.Media.Validation;

namespace SceneForge.Media.Detection.Export;

// Diagnostic CSV export - every field of TransitionDetection, one row per
// detection, InvariantCulture throughout so the file is portable and
// re-parseable regardless of the machine's locale. Never writes to an
// existing file silently: FileMode.CreateNew means an accidental re-run
// against the same output path fails loudly instead of clobbering a
// previous report (CLAUDE.md rule 12 - outputs go to a user-selected *new*
// path).
public static class TransitionDetectionCsvWriter
{
    private const string Header = "Type,StartSeconds,PeakSeconds,EndSeconds,BoundaryTimestampSeconds,DurationSeconds,Confidence,ContributingSignals,DiagnosticReason";

    public static async Task WriteAsync(
        IReadOnlyList<TransitionDetection> detections,
        TextWriter writer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(detections);
        ArgumentNullException.ThrowIfNull(writer);

        await writer.WriteLineAsync(Header).ConfigureAwait(false);

        foreach (var detection in detections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(FormatRow(detection)).ConfigureAwait(false);
        }
    }

    public static async Task WriteToFileAsync(
        IReadOnlyList<TransitionDetection> detections,
        string outputFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outputFilePath);

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputFilePath))
            ?? throw new TransitionDetectionException($"'{outputFilePath}' does not resolve to a valid file path.");
        OutputDirectoryValidator.EnsureWritable(directory);

        try
        {
            await using var stream = new FileStream(outputFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await using var writer = new StreamWriter(stream);
            await WriteAsync(detections, writer, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            throw new TransitionDetectionException(
                $"Could not write to '{outputFilePath}': {ex.Message}. Choose a new output path rather than overwriting an existing file.", ex);
        }
    }

    private static string FormatRow(TransitionDetection detection)
    {
        var contributingSignals = string.Join(
            ";",
            detection.ContributingSignals.Select(kv => $"{kv.Key}={kv.Value.ToString("F4", CultureInfo.InvariantCulture)}"));

        var fields = new[]
        {
            detection.Type.ToString(),
            detection.Start.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture),
            detection.Peak.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture),
            detection.End.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture),
            detection.BoundaryTimestamp.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture),
            detection.Duration.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture),
            detection.Confidence.ToString("F4", CultureInfo.InvariantCulture),
            contributingSignals,
            detection.DiagnosticReason,
        };

        return string.Join(",", fields.Select(EscapeCsvField));
    }

    private static string EscapeCsvField(string field)
    {
        if (field.IndexOfAny([',', '"', '\n', '\r']) < 0)
        {
            return field;
        }

        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }
}
