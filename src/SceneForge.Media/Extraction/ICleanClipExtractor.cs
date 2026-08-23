namespace SceneForge.Media.Extraction;

public interface ICleanClipExtractor
{
    Task<CleanClipExtractionResult> ExtractAsync(
        string filePath,
        CleanClipExtractionOptions options,
        IProgress<CleanClipExtractionProgress>? progress,
        CancellationToken cancellationToken = default);
}
