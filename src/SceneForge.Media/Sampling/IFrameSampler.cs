namespace SceneForge.Media.Sampling;

public interface IFrameSampler
{
    // Streams downscaled, timestamped frames from filePath. Nothing is
    // decoded until enumeration begins (standard IAsyncEnumerable
    // semantics), and stopping enumeration early - by breaking out of an
    // `await foreach` or disposing the enumerator - promptly tears down the
    // underlying ffmpeg process rather than letting it run to completion.
    IAsyncEnumerable<FrameSample> SampleAsync(
        string filePath,
        FrameSamplingOptions options,
        IProgress<FrameSamplingProgress>? progress,
        CancellationToken cancellationToken);
}
