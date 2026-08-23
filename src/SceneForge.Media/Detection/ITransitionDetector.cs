namespace SceneForge.Media.Detection;

public interface ITransitionDetector
{
    // Streams filePath through IFrameSampler, computes all seven signals per
    // consecutive frame pair, classifies against all seven transition types
    // over a bounded sliding window, and fuses the results into a final,
    // non-overlapping detection list. Honors cancellationToken throughout -
    // frame sampling, signal computation, and classification all check it.
    Task<IReadOnlyList<TransitionDetection>> DetectAsync(
        string filePath,
        TransitionDetectionOptions options,
        IProgress<TransitionDetectionProgress>? progress,
        CancellationToken cancellationToken);
}
