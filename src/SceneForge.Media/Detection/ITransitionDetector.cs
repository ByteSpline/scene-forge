using SceneForge.Media.Domain;

namespace SceneForge.Media.Detection;

public interface ITransitionDetector
{
    // Streams filePath through IFrameSampler, computes all seven signals per
    // consecutive frame pair, classifies against all seven transition types
    // over a bounded sliding window, and fuses the results into a final,
    // non-overlapping detection list. Honors cancellationToken throughout -
    // frame sampling, signal computation, and classification all check it.
    // Probes filePath via ffprobe internally; callers that have already
    // probed the file for their own purposes should use the
    // DetectAsync(string, MediaInfo, ...) overload instead to avoid a
    // second, redundant ffprobe process per file.
    Task<IReadOnlyList<TransitionDetection>> DetectAsync(
        string filePath,
        TransitionDetectionOptions options,
        IProgress<TransitionDetectionProgress>? progress,
        CancellationToken cancellationToken);

    // Same as above, but uses the caller-supplied mediaInfo directly instead
    // of probing filePath itself. mediaInfo must describe filePath (the
    // caller's responsibility).
    Task<IReadOnlyList<TransitionDetection>> DetectAsync(
        string filePath,
        MediaInfo mediaInfo,
        TransitionDetectionOptions options,
        IProgress<TransitionDetectionProgress>? progress,
        CancellationToken cancellationToken);
}
