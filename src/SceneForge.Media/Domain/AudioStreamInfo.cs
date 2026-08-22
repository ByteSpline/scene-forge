namespace SceneForge.Media.Domain;

public sealed record AudioStreamInfo
{
    public required int Index { get; init; }

    public required string CodecName { get; init; }

    public required int SampleRateHz { get; init; }

    public required int Channels { get; init; }

    public string? ChannelLayout { get; init; }

    public long? BitRateBitsPerSecond { get; init; }

    public TimeSpan? Duration { get; init; }
}
