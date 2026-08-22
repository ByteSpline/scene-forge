using SceneForge.Media.Processes;
using SceneForge.Media.Tests.TestSupport;
using SceneForge.Media.Tooling;

namespace SceneForge.Media.Tests.Tooling;

public sealed class FfmpegToolLocatorTests : IDisposable
{
    private readonly DirectoryInfo _appDirectory = Directory.CreateTempSubdirectory("sceneforge-tools-");

    public void Dispose() => _appDirectory.Delete(recursive: true);

    [Fact]
    public async Task LocateAsync_NeitherBinaryPresent_ThrowsWithBothPaths()
    {
        var locator = new FfmpegToolLocator(FakeProcessRunner.ReturningResult(VersionResult("ffprobe version 9.0")), _appDirectory.FullName);

        var exception = await Assert.ThrowsAsync<FfmpegToolsNotFoundException>(() => locator.LocateAsync(CancellationToken.None));

        Assert.Equal(2, exception.MissingPaths.Count);
        Assert.Contains(exception.MissingPaths, p => p.EndsWith("ffprobe.exe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(exception.MissingPaths, p => p.EndsWith("ffmpeg.exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LocateAsync_OnlyFfprobePresent_ThrowsListingOnlyFfmpeg()
    {
        CreateToolsFile("ffprobe.exe");
        var locator = new FfmpegToolLocator(FakeProcessRunner.ReturningResult(VersionResult("ffprobe version 9.0")), _appDirectory.FullName);

        var exception = await Assert.ThrowsAsync<FfmpegToolsNotFoundException>(() => locator.LocateAsync(CancellationToken.None));

        var missing = Assert.Single(exception.MissingPaths);
        Assert.EndsWith("ffmpeg.exe", missing, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LocateAsync_BothPresentAndVersionChecksPass_ReturnsResolvedPaths()
    {
        CreateToolsFile("ffprobe.exe");
        CreateToolsFile("ffmpeg.exe");
        var runner = new FakeProcessRunner((request, _) => Task.FromResult(
            request.FileName.EndsWith("ffprobe.exe", StringComparison.OrdinalIgnoreCase)
                ? VersionResult("ffprobe version 9.0.1-full_build")
                : VersionResult("ffmpeg version 9.0.1-full_build")));
        var locator = new FfmpegToolLocator(runner, _appDirectory.FullName);

        var paths = await locator.LocateAsync(CancellationToken.None);

        Assert.EndsWith("ffprobe.exe", paths.FfprobePath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("ffmpeg.exe", paths.FfmpegPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LocateAsync_VersionCheckExitsNonZero_ThrowsIncompatible()
    {
        CreateToolsFile("ffprobe.exe");
        CreateToolsFile("ffmpeg.exe");
        var runner = FakeProcessRunner.ReturningResult(new ProcessExecutionResult
        {
            ExitCode = 1,
            StandardOutput = string.Empty,
            StandardError = "not a real binary",
            Elapsed = TimeSpan.FromMilliseconds(5),
        });
        var locator = new FfmpegToolLocator(runner, _appDirectory.FullName);

        await Assert.ThrowsAsync<FfmpegToolsIncompatibleException>(() => locator.LocateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LocateAsync_VersionBannerUnrecognized_ThrowsIncompatible()
    {
        CreateToolsFile("ffprobe.exe");
        CreateToolsFile("ffmpeg.exe");
        var locator = new FfmpegToolLocator(FakeProcessRunner.ReturningResult(VersionResult("not the banner you are looking for")), _appDirectory.FullName);

        await Assert.ThrowsAsync<FfmpegToolsIncompatibleException>(() => locator.LocateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LocateAsync_ProcessFailsToLaunch_ThrowsIncompatible()
    {
        CreateToolsFile("ffprobe.exe");
        CreateToolsFile("ffmpeg.exe");
        var locator = new FfmpegToolLocator(
            FakeProcessRunner.Throwing(new ProcessLaunchException("ffprobe.exe", "boom", new InvalidOperationException())),
            _appDirectory.FullName);

        await Assert.ThrowsAsync<FfmpegToolsIncompatibleException>(() => locator.LocateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LocateAsync_VersionCheckTimesOut_ThrowsIncompatible()
    {
        CreateToolsFile("ffprobe.exe");
        CreateToolsFile("ffmpeg.exe");
        var locator = new FfmpegToolLocator(
            FakeProcessRunner.Throwing(new ProcessTimeoutException(TimeSpan.FromSeconds(10), string.Empty, string.Empty)),
            _appDirectory.FullName);

        await Assert.ThrowsAsync<FfmpegToolsIncompatibleException>(() => locator.LocateAsync(CancellationToken.None));
    }

    private void CreateToolsFile(string fileName)
    {
        var toolsDir = Path.Combine(_appDirectory.FullName, "tools", "ffmpeg");
        Directory.CreateDirectory(toolsDir);
        File.WriteAllBytes(Path.Combine(toolsDir, fileName), []);
    }

    private static ProcessExecutionResult VersionResult(string banner) => new()
    {
        ExitCode = 0,
        StandardOutput = banner,
        StandardError = string.Empty,
        Elapsed = TimeSpan.FromMilliseconds(5),
    };
}
