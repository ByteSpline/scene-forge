using SceneForge.Media.Processes;
using SceneForge.Media.Tests.TestSupport;
using SceneForge.Media.Tooling;
using Xunit;

namespace SceneForge.Media.Tests.Tooling;

public class FfmpegToolLocatorIntegrationTests
{
    [SkippableFact]
    public async Task LocateAsync_RealBinaries_ResolvesAndPassesVersionCheck()
    {
        Skip.IfNot(RealFfmpegAvailability.IsAvailable, RealFfmpegAvailability.SkipReason);

        var locator = new FfmpegToolLocator(new ProcessRunner());

        var paths = await locator.LocateAsync(CancellationToken.None);

        Assert.Equal(RealFfmpegAvailability.FfprobePath, paths.FfprobePath);
        Assert.Equal(RealFfmpegAvailability.FfmpegPath, paths.FfmpegPath);
    }
}
