using System.Text.Json.Serialization;

namespace SceneForge.Media.Probing.Json;

internal sealed class FfprobeStreamDto
{
    [JsonPropertyName("index")]
    public int Index { get; init; }

    [JsonPropertyName("codec_name")]
    public string? CodecName { get; init; }

    [JsonPropertyName("codec_type")]
    public string? CodecType { get; init; }

    [JsonPropertyName("width")]
    public int? Width { get; init; }

    [JsonPropertyName("height")]
    public int? Height { get; init; }

    [JsonPropertyName("pix_fmt")]
    public string? PixelFormat { get; init; }

    [JsonPropertyName("r_frame_rate")]
    public string? RFrameRate { get; init; }

    [JsonPropertyName("avg_frame_rate")]
    public string? AvgFrameRate { get; init; }

    [JsonPropertyName("time_base")]
    public string? TimeBase { get; init; }

    [JsonPropertyName("duration_ts")]
    public long? DurationTimestamps { get; init; }

    [JsonPropertyName("duration")]
    public string? Duration { get; init; }

    [JsonPropertyName("bit_rate")]
    public string? BitRate { get; init; }

    [JsonPropertyName("sample_rate")]
    public string? SampleRate { get; init; }

    [JsonPropertyName("channels")]
    public int? Channels { get; init; }

    [JsonPropertyName("channel_layout")]
    public string? ChannelLayout { get; init; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; init; }

    [JsonPropertyName("side_data_list")]
    public List<FfprobeSideDataDto>? SideDataList { get; init; }
}
