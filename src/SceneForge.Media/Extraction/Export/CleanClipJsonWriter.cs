using System.Text.Json;
using System.Text.Json.Serialization;
using SceneForge.Media.Validation;

namespace SceneForge.Media.Extraction.Export;

// Diagnostic JSON export of a full CleanClipExtractionResult - camelCase,
// indented, TimeSpans as seconds, enums as strings (same conventions as
// Detection.Export.TransitionDetectionJsonWriter). Exports
// RemainingCleanRanges, both AcceptedClips and RejectedClips (each carrying
// its full ClipScore.Reasons - rejections are never opaque), and Clusters,
// so the whole result is inspectable without re-running extraction. Same
// never-silently-overwrite policy as every other writer in this codebase
// (CLAUDE.md rule 12).
public static class CleanClipJsonWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new TimeSpanSecondsConverter(), new JsonStringEnumConverter() },
    };

    public static async Task WriteAsync(
        CleanClipExtractionResult result,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(stream);

        await JsonSerializer.SerializeAsync(stream, result, Options, cancellationToken).ConfigureAwait(false);
    }

    public static async Task WriteToFileAsync(
        CleanClipExtractionResult result,
        string outputFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outputFilePath);

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputFilePath))
            ?? throw new CleanClipExtractionException($"'{outputFilePath}' does not resolve to a valid file path.");
        OutputDirectoryValidator.EnsureWritable(directory);

        try
        {
            await using var stream = new FileStream(outputFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await WriteAsync(result, stream, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            throw new CleanClipExtractionException(
                $"Could not write to '{outputFilePath}': {ex.Message}. Choose a new output path rather than overwriting an existing file.", ex);
        }
    }

    private sealed class TimeSpanSecondsConverter : JsonConverter<TimeSpan>
    {
        public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            TimeSpan.FromSeconds(reader.GetDouble());

        public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value.TotalSeconds);
    }
}
