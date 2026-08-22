namespace SceneForge.Media.Domain;

public sealed record VideoStreamInfo
{
    public required int Index { get; init; }

    public required string CodecName { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required RationalFrameRate AverageFrameRate { get; init; }

    public required RationalFrameRate RealBaseFrameRate { get; init; }

    // Heuristic only: true when the container's real-base and average frame
    // rates disagree, which ffprobe reports separately precisely because a
    // single nominal fps cannot describe the stream. Never treat this as a
    // guarantee of exact per-frame timing.
    public required bool IsVariableFrameRate { get; init; }

    public required int RotationDegrees { get; init; }

    public string? PixelFormat { get; init; }

    public long? BitRateBitsPerSecond { get; init; }

    public TimeSpan? Duration { get; init; }
}
