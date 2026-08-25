using SceneForge.Media.Detection;
using SceneForge.Media.Domain;

namespace SceneForge.App.Tests.TestSupport;

internal sealed class FakeTransitionDetector : ITransitionDetector
{
    public IReadOnlyList<TransitionDetection> Result { get; set; } = [];

    // When set, DetectAsync awaits this before returning - lets a test hold
    // the operation open long enough to call a Cancel command and observe
    // cooperative cancellation (CLAUDE.md rule 5), something a fully
    // synchronous fake cannot exercise.
    public TaskCompletionSource<bool>? Gate { get; set; }

    // AnalysisProgressViewModel always has an already-probed MediaInfo by
    // the time it calls the detector, so it must always use the
    // MediaInfo-accepting overload below, never re-probe via this one - a
    // regression here would mean a redundant ffprobe process per analysis
    // run.
    public Task<IReadOnlyList<TransitionDetection>> DetectAsync(
        string filePath,
        TransitionDetectionOptions options,
        IProgress<TransitionDetectionProgress>? progress,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            "AnalysisProgressViewModel must call the MediaInfo-accepting DetectAsync overload, not re-probe internally.");

    public async Task<IReadOnlyList<TransitionDetection>> DetectAsync(
        string filePath,
        MediaInfo mediaInfo,
        TransitionDetectionOptions options,
        IProgress<TransitionDetectionProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new TransitionDetectionProgress
        {
            FramesAnalyzed = 1,
            RawCandidatesSoFar = Result.Count,
            LastSourceTimestamp = TimeSpan.Zero,
            Elapsed = TimeSpan.Zero,
        });

        if (Gate is not null)
        {
            await Gate.Task;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Result;
    }
}
