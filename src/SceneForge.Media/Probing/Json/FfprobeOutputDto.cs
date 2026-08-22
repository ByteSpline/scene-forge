using System.Text.Json.Serialization;

namespace SceneForge.Media.Probing.Json;

// Mirrors only the subset of `ffprobe -print_format json -show_format
// -show_streams` fields SceneForge actually consumes. Verified against real
// ffprobe 9.0.1 output (see docs/PHASE_04_REPORT.md); unknown fields are
// ignored by System.Text.Json by default.
internal sealed class FfprobeOutputDto
{
    [JsonPropertyName("streams")]
    public List<FfprobeStreamDto>? Streams { get; init; }

    [JsonPropertyName("format")]
    public FfprobeFormatDto? Format { get; init; }
}
