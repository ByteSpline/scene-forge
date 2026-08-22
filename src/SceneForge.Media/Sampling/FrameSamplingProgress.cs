namespace SceneForge.Media.Sampling;

public sealed record FrameSamplingProgress
{
    public required int FramesEmitted { get; init; }

    // Source timestamp of the most recently emitted frame.
    public required TimeSpan LastSourceTimestamp { get; init; }

    // Wall-clock time since sampling started.
    public required TimeSpan Elapsed { get; init; }
}
