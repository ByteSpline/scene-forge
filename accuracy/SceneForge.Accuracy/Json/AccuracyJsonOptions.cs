using System.Text.Json;
using System.Text.Json.Serialization;

namespace SceneForge.Accuracy.Json;

// Same convention as every other JSON writer in this repo (e.g.
// TransitionDetectionJsonWriter): camelCase, indented, TimeSpans as seconds.
// Additionally allows the named floating-point literals ("NaN") System.Text.Json
// rejects by default - GroupMetrics uses NaN deliberately as "not
// applicable" (see its own remarks), which must round-trip through JSON
// exactly, not be forced into a fake 0 or throw.
public static class AccuracyJsonOptions
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new TimeSpanSecondsConverter(), new JsonStringEnumConverter() },
    };

    private sealed class TimeSpanSecondsConverter : JsonConverter<TimeSpan>
    {
        public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            TimeSpan.FromSeconds(reader.GetDouble());

        public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value.TotalSeconds);
    }
}
