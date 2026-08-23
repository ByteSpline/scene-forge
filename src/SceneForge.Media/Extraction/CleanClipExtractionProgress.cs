namespace SceneForge.Media.Extraction;

public sealed record CleanClipExtractionProgress
{
    public required int FramesAnalyzed { get; init; }

    public required int CandidatesScoredSoFar { get; init; }

    public required int ClipsAcceptedSoFar { get; init; }

    public required TimeSpan LastSourceTimestamp { get; init; }

    public required TimeSpan Elapsed { get; init; }
}
