using System.Windows.Media.Imaging;

namespace SceneForge.App.Services;

// Small, disk-cached preview frames for Scene Review's clip list. Never
// returns a full-resolution bitmap or opens a media player per row (CLAUDE.md
// rule 6/7) - every image is decoded at a fixed small width and the
// generating ffmpeg process count is bounded (see ThumbnailCacheService).
// Returns null (never throws for a routine failure) when generation fails,
// since a missing thumbnail is a cosmetic degradation, never a reason to
// block the review workflow.
public interface IThumbnailCacheService
{
    Task<BitmapSource?> GetThumbnailAsync(string sourceVideoPath, TimeSpan timestamp, CancellationToken cancellationToken);
}
