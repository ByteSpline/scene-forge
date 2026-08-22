namespace SceneForge.Media.Domain;

public sealed record MediaInfo
{
    public required string FilePath { get; init; }

    public required string FormatName { get; init; }

    public required TimeSpan Duration { get; init; }

    public long? BitRateBitsPerSecond { get; init; }

    public required IReadOnlyList<VideoStreamInfo> VideoStreams { get; init; }

    public required IReadOnlyList<AudioStreamInfo> AudioStreams { get; init; }

    public VideoStreamInfo? PrimaryVideoStream => VideoStreams.Count > 0 ? VideoStreams[0] : null;

    public AudioStreamInfo? PrimaryAudioStream => AudioStreams.Count > 0 ? AudioStreams[0] : null;
}
