using System.Windows.Media.Imaging;
using SceneForge.App.Services;

namespace SceneForge.App.Tests.TestSupport;

internal sealed class FakeThumbnailCacheService : IThumbnailCacheService
{
    public Task<BitmapSource?> GetThumbnailAsync(string sourceVideoPath, TimeSpan timestamp, CancellationToken cancellationToken) =>
        Task.FromResult<BitmapSource?>(null);
}
