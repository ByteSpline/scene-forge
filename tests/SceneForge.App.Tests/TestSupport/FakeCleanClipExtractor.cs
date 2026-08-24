using SceneForge.Media.Extraction;

namespace SceneForge.App.Tests.TestSupport;

internal sealed class FakeCleanClipExtractor : ICleanClipExtractor
{
    public CleanClipExtractionResult Result { get; set; } = new()
    {
        RemainingCleanRanges = [],
        AcceptedClips = [],
        RejectedClips = [],
        Clusters = [],
    };

    public Task<CleanClipExtractionResult> ExtractAsync(
        string filePath,
        CleanClipExtractionOptions options,
        IProgress<CleanClipExtractionProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new CleanClipExtractionProgress
        {
            FramesAnalyzed = 1,
            CandidatesScoredSoFar = Result.AcceptedClips.Count + Result.RejectedClips.Count,
            ClipsAcceptedSoFar = Result.AcceptedClips.Count,
            LastSourceTimestamp = TimeSpan.Zero,
            Elapsed = TimeSpan.Zero,
        });
        return Task.FromResult(Result);
    }
}
