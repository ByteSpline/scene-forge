using System.IO;
using SceneForge.App.Persistence;
using SceneForge.App.Tests.TestSupport;
using SceneForge.Infrastructure.Logging;
using SceneForge.Infrastructure.Persistence;
using SceneForge.Media.Domain;

namespace SceneForge.App.Tests.Persistence;

// Regression coverage for the release-review finding that
// StartupRecoveryRunner used to invoke IFfprobeService.ProbeAsync with
// CancellationToken.None from a synchronous, UI-thread-blocking call in
// App.OnStartup before the shell window even existed - a real, uncancelled
// child-process invocation with no cooperative-cancellation path (CLAUDE.md
// rule 5). These tests exercise the fixed, always-async, always-bounded
// per-source restore logic directly (via InternalsVisibleTo), without a
// real DI container, real dialogs, or a real ffprobe binary.
public sealed class StartupRecoveryRunnerTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "SceneForgeTests", Guid.NewGuid().ToString("N"));

    public StartupRecoveryRunnerTests() => Directory.CreateDirectory(_tempDirectory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task RestoreSourceIfFreshAsync_FreshSource_PassesAGenuinelyCancellableTokenToProbeAsync()
    {
        var path = CreateFile("video.mp4");
        var fingerprint = new StaleSourceDetector().Capture(path);
        var probe = new RecordingFfprobeService { ResultToReturn = MediaInfoBuilder.Video(path, TimeSpan.FromSeconds(5)) };
        MediaInfo? captured = null;

        await StartupRecoveryRunner.RestoreSourceIfFreshAsync(
            fingerprint,
            "source video",
            new StaleSourceDetector(),
            probe,
            new FakeDialogService(),
            new FakeAppLogger(),
            _ => { },
            info => captured = info,
            CancellationToken.None);

        Assert.NotNull(captured);
        var tokenUsed = Assert.Single(probe.TokensReceived);
        Assert.True(tokenUsed.CanBeCanceled, "the token passed to ProbeAsync must be a real, cancellable token - never CancellationToken.None.");
    }

    [Fact]
    public async Task RestoreSourceIfFreshAsync_StaleSource_NeverCallsProbeAndSurfacesAnError()
    {
        var path = CreateFile("video.mp4");
        var fingerprint = new StaleSourceDetector().Capture(path);
        File.WriteAllBytes(path, [1, 2, 3, 4, 5, 6, 7, 8]); // size changes -> Changed, not Fresh
        var probe = new RecordingFfprobeService();
        var dialogService = new FakeDialogService();

        await StartupRecoveryRunner.RestoreSourceIfFreshAsync(
            fingerprint,
            "source video",
            new StaleSourceDetector(),
            probe,
            dialogService,
            new FakeAppLogger(),
            _ => { },
            _ => { },
            CancellationToken.None);

        Assert.Empty(probe.TokensReceived);
        Assert.Single(dialogService.Errors);
    }

    [Fact]
    public async Task RestoreSourceIfFreshAsync_ProbeExceedsItsOwnTimeout_LogsWarningInsteadOfHangingOrThrowing()
    {
        var path = CreateFile("video.mp4");
        var fingerprint = new StaleSourceDetector().Capture(path);
        var probe = new RecordingFfprobeService { HangUntilCancelled = true };
        var logger = new FakeAppLogger();
        var callerToken = CancellationToken.None;

        await StartupRecoveryRunner.RestoreSourceIfFreshAsync(
            fingerprint,
            "source video",
            new StaleSourceDetector(),
            probe,
            new FakeDialogService(),
            logger,
            _ => { },
            _ => { },
            callerToken,
            probeTimeout: TimeSpan.FromMilliseconds(50));

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("did not finish within"));
    }

    [Fact]
    public async Task RestoreSourceIfFreshAsync_CallerCancelsExplicitly_PropagatesCancellationRatherThanLoggingAndSwallowing()
    {
        var path = CreateFile("video.mp4");
        var fingerprint = new StaleSourceDetector().Capture(path);
        var probe = new RecordingFfprobeService { HangUntilCancelled = true };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            StartupRecoveryRunner.RestoreSourceIfFreshAsync(
                fingerprint,
                "source video",
                new StaleSourceDetector(),
                probe,
                new FakeDialogService(),
                new FakeAppLogger(),
                _ => { },
                _ => { },
                cts.Token,
                probeTimeout: TimeSpan.FromSeconds(20)));
    }

    private string CreateFile(string name)
    {
        var path = Path.Combine(_tempDirectory, name);
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }
}
