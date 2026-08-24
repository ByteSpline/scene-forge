using System.Text.Json;
using System.Text.Json.Serialization;
using SceneForge.Media.Domain;

namespace SceneForge.Infrastructure.Persistence;

// Reads and writes SceneForgeProjectDocument as versioned JSON - camelCase,
// indented, TimeSpans as seconds and enums as strings (the same conventions
// SceneForge.Media's own diagnostic JSON writers already use - see
// Detection.Export.TransitionDetectionJsonWriter). Saves always go through
// AtomicFileWriter (CLAUDE.md rule 12's "atomic write" requirement); loads
// never trust ffmpeg-adjacent assumptions - a missing required field or a
// mismatched schema version both fail loudly rather than silently
// substituting a default.
public sealed class ProjectStore : IProjectStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new TimeSpanSecondsConverter(), new RationalFrameRateConverter(), new JsonStringEnumConverter() },
    };

    public async Task SaveAsync(SceneForgeProjectDocument document, string projectFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);

        await AtomicFileWriter.WriteAsync(
            projectFilePath,
            (stream, ct) => JsonSerializer.SerializeAsync(stream, document, Options, ct),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<SceneForgeProjectDocument> LoadAsync(string projectFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);

        var fullPath = Path.GetFullPath(projectFilePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Project file '{fullPath}' does not exist.", fullPath);
        }

        SceneForgeProjectDocument? document;
        try
        {
            await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            document = await JsonSerializer.DeserializeAsync<SceneForgeProjectDocument>(stream, Options, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new ProjectCorruptedException(
                $"Project file '{fullPath}' is corrupted and could not be read: {ex.Message}", ex);
        }

        if (document is null)
        {
            throw new ProjectCorruptedException($"Project file '{fullPath}' is corrupted: it parsed to an empty document.");
        }

        if (document.SchemaVersion != SceneForgeProjectDocument.CurrentSchemaVersion)
        {
            throw new ProjectSchemaVersionException(document.SchemaVersion, SceneForgeProjectDocument.CurrentSchemaVersion);
        }

        return document;
    }

    private sealed class TimeSpanSecondsConverter : JsonConverter<TimeSpan>
    {
        public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            TimeSpan.FromSeconds(reader.GetDouble());

        public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value.TotalSeconds);
    }

    // RationalFrameRate is a readonly record struct whose Numerator/
    // Denominator are get-only (set only via its constructor). System.Text.Json's
    // reflection-based converter never invokes a struct's parameterized
    // constructor - it always starts from default(T) and assigns settable
    // properties, which here are none - so without this converter every
    // round trip silently deserializes to RationalFrameRate.Undefined (0/0)
    // instead of throwing, a genuine corruption bug this project's own test
    // suite caught (see docs/PHASE_11_REPORT.md, Self-review findings).
    // RationalFrameRate.ToString()/Parse already define exactly the
    // "numerator/denominator" text form, so this converter is a thin JSON-
    // string wrapper over them rather than a second parsing implementation.
    private sealed class RationalFrameRateConverter : JsonConverter<RationalFrameRate>
    {
        public override RationalFrameRate Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            RationalFrameRate.Parse(reader.GetString());

        public override void Write(Utf8JsonWriter writer, RationalFrameRate value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
