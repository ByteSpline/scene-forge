using System.Text.Json.Serialization;

namespace SceneForge.Media.Probing.Json;

internal sealed class FfprobeSideDataDto
{
    [JsonPropertyName("side_data_type")]
    public string? SideDataType { get; init; }

    [JsonPropertyName("rotation")]
    public double? Rotation { get; init; }
}
