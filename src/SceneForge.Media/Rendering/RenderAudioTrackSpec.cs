namespace SceneForge.Media.Rendering;

// The one audio track that ends up in the rendered output. Source video
// audio is never mapped (see FFmpegRenderService: the source file's audio
// stream is simply never referenced by any -map, not merely muted) - the
// phase brief's "remove source audio and map only the supplied audio" is
// satisfied structurally, not by an extra "delete track" step.
public sealed record RenderAudioTrackSpec
{
    private readonly int _sampleRateHz = 48_000;
    private readonly int _channels = 2;

    public required string FilePath { get; init; }

    // Where in FilePath the used audio begins. Defaults to the start of the
    // file.
    public TimeSpan TrimStart { get; init; } = TimeSpan.Zero;

    // How much of FilePath (from TrimStart) to use. Should normally equal
    // the TimelinePlan.PlannedDuration this audio track drove
    // (TimelinePlanRequest.TargetAudioDuration) - RenderPlanBuilder does not
    // enforce this since a caller may deliberately render a shorter preview,
    // but RenderPlan.PlannedVideoDuration always reflects the video side so
    // a mismatch is visible to the caller, not hidden.
    public required TimeSpan TrimDuration { get; init; }

    public string Codec { get; init; } = "aac";

    // Null lets ffmpeg's encoder choose a default bitrate.
    public int? BitRateBitsPerSecond { get; init; }

    public int SampleRateHz
    {
        get => _sampleRateHz;
        init => _sampleRateHz = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Sample rate must be positive.");
    }

    public int Channels
    {
        get => _channels;
        init => _channels = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Channel count must be positive.");
    }
}
