using System.Text.Json.Serialization;

namespace SceneForge.Media.Probing.Json;

internal sealed class FfprobeFormatDto
{
    [JsonPropertyName("filename")]
    public string? Filename { get; init; }

    [JsonPropertyName("format_name")]
    public string? FormatName { get; init; }

    [JsonPropertyName("duration")]
    public string? Duration { get; init; }

    [JsonPropertyName("bit_rate")]
    public string? BitRate { get; init; }

    [JsonPropertyName("nb_streams")]
    public int NbStreams { get; init; }
}
