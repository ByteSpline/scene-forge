using SceneForge.Media.Domain;

namespace SceneForge.Media.Extraction;

public interface ICleanClipExtractor
{
    // Probes filePath via ffprobe internally; callers that have already
    // probed the file for their own purposes should use the
    // ExtractAsync(string, MediaInfo, ...) overload instead to avoid a
    // second, redundant ffprobe process per file.
    Task<CleanClipExtractionResult> ExtractAsync(
        string filePath,
        CleanClipExtractionOptions options,
        IProgress<CleanClipExtractionProgress>? progress,
        CancellationToken cancellationToken = default);

    // Same as above, but uses the caller-supplied mediaInfo directly instead
    // of probing filePath itself. mediaInfo must describe filePath (the
    // caller's responsibility).
    Task<CleanClipExtractionResult> ExtractAsync(
        string filePath,
        MediaInfo mediaInfo,
        CleanClipExtractionOptions options,
        IProgress<CleanClipExtractionProgress>? progress,
        CancellationToken cancellationToken = default);
}
