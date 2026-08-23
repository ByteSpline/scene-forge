using System.Text.Json;
using System.Text.Json.Serialization;
using SceneForge.Media.Validation;

namespace SceneForge.Media.Detection.Export;

// Diagnostic JSON export - the full TransitionDetection list, camelCase,
// indented, TimeSpans serialized as seconds (a double) for portability
// (matches TransitionDetectionCsvWriter's *Seconds columns). Same
// never-silently-overwrite policy as the CSV writer (CLAUDE.md rule 12).
public static class TransitionDetectionJsonWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new TimeSpanSecondsConverter(), new JsonStringEnumConverter() },
    };

    public static async Task WriteAsync(
        IReadOnlyList<TransitionDetection> detections,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(detections);
        ArgumentNullException.ThrowIfNull(stream);

        await JsonSerializer.SerializeAsync(stream, detections, Options, cancellationToken).ConfigureAwait(false);
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
            await WriteAsync(detections, stream, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            throw new TransitionDetectionException(
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
