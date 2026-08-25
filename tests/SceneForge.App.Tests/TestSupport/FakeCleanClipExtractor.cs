using SceneForge.Media.Domain;
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

    // AnalysisProgressViewModel always has an already-probed MediaInfo by
    // the time it calls the extractor, so it must always use the
    // MediaInfo-accepting overload below, never re-probe via this one.
    public Task<CleanClipExtractionResult> ExtractAsync(
        string filePath,
        CleanClipExtractionOptions options,
        IProgress<CleanClipExtractionProgress>? progress,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "AnalysisProgressViewModel must call the MediaInfo-accepting ExtractAsync overload, not re-probe internally.");

    public Task<CleanClipExtractionResult> ExtractAsync(
        string filePath,
        MediaInfo mediaInfo,
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
