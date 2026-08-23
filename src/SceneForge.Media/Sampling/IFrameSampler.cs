using SceneForge.Media.Domain;

namespace SceneForge.Media.Sampling;

public interface IFrameSampler
{
    // Streams downscaled, timestamped frames from filePath. Nothing is
    // decoded until enumeration begins (standard IAsyncEnumerable
    // semantics), and stopping enumeration early - by breaking out of an
    // `await foreach` or disposing the enumerator - promptly tears down the
    // underlying ffmpeg process rather than letting it run to completion.
    // Probes filePath via ffprobe internally to resolve the video stream's
    // dimensions; callers that have already probed the file for their own
    // purposes should use the SampleAsync(string, MediaInfo, ...) overload
    // instead to avoid a second, redundant ffprobe process per file.
    IAsyncEnumerable<FrameSample> SampleAsync(
        string filePath,
        FrameSamplingOptions options,
        IProgress<FrameSamplingProgress>? progress,
        CancellationToken cancellationToken);

    // Same as above, but uses the caller-supplied mediaInfo directly
    // instead of probing filePath itself. mediaInfo must describe filePath
    // (the caller's responsibility - this is purely an internal-probing
    // optimization, not a way to sample one file using another's metadata).
    IAsyncEnumerable<FrameSample> SampleAsync(
        string filePath,
        MediaInfo mediaInfo,
        FrameSamplingOptions options,
        IProgress<FrameSamplingProgress>? progress,
        CancellationToken cancellationToken);
}
