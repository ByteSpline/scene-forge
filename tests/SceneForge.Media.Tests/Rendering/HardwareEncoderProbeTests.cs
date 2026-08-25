using SceneForge.Media.Processes;
using SceneForge.Media.Rendering;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Rendering;

public class HardwareEncoderProbeTests
{
    private static ProcessExecutionResult Result(int exitCode) => new()
    {
        ExitCode = exitCode,
        StandardOutput = "",
        StandardError = exitCode == 0 ? "" : "encoder unavailable",
        Elapsed = TimeSpan.Zero,
    };

    private static string? EncoderNameFromRequest(ProcessExecutionRequest request)
    {
        var index = request.Arguments.ToList().IndexOf("-c:v");
        return index >= 0 && index + 1 < request.Arguments.Count ? request.Arguments[index + 1] : null;
    }

    [Fact]
    public async Task SelectEncoderAsync_NvencSucceeds_ReturnsNvencFirst()
    {
        var processRunner = new FakeProcessRunner((request, _) => Task.FromResult(Result(0)));
        var probe = new HardwareEncoderProbe(processRunner, new FakeFfmpegToolLocator());

        var selection = await probe.SelectEncoderAsync(CancellationToken.None);

        Assert.Equal(VideoEncoderKind.NvidiaNvenc, selection.Kind);
        Assert.Equal("h264_nvenc", selection.FfmpegEncoderName);
        Assert.True(selection.IsHardwareAccelerated);
        Assert.Single(processRunner.Requests);
    }

    [Fact]
    public async Task SelectEncoderAsync_NvencFails_QsvSucceeds_ReturnsQsv()
    {
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            var encoder = EncoderNameFromRequest(request);
            return Task.FromResult(Result(encoder == "h264_qsv" ? 0 : 1));
        });
        var probe = new HardwareEncoderProbe(processRunner, new FakeFfmpegToolLocator());

        var selection = await probe.SelectEncoderAsync(CancellationToken.None);

        Assert.Equal(VideoEncoderKind.IntelQuickSync, selection.Kind);
        Assert.Equal(2, processRunner.Requests.Count);
    }

    [Fact]
    public async Task SelectEncoderAsync_AllHardwareFail_FallsBackToLibx264()
    {
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            var encoder = EncoderNameFromRequest(request);
            return Task.FromResult(Result(encoder == "libx264" ? 0 : 1));
        });
        var probe = new HardwareEncoderProbe(processRunner, new FakeFfmpegToolLocator());

        var selection = await probe.SelectEncoderAsync(CancellationToken.None);

        Assert.Equal(VideoEncoderKind.SoftwareX264, selection.Kind);
        Assert.False(selection.IsHardwareAccelerated);
        Assert.Equal(4, processRunner.Requests.Count);
    }

    [Fact]
    public async Task SelectEncoderAsync_EveryCandidateFails_ThrowsRenderExecutionException()
    {
        var processRunner = new FakeProcessRunner((_, _) => Task.FromResult(Result(1)));
        var probe = new HardwareEncoderProbe(processRunner, new FakeFfmpegToolLocator());

        await Assert.ThrowsAsync<RenderExecutionException>(() => probe.SelectEncoderAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SelectEncoderAsync_CandidateThrowsProcessLaunchException_TreatedAsFailureAndNextCandidateTried()
    {
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            var encoder = EncoderNameFromRequest(request);
            if (encoder == "h264_nvenc")
            {
                throw new ProcessLaunchException("ffmpeg.exe", "not found", new InvalidOperationException());
            }

            return Task.FromResult(Result(encoder == "h264_qsv" ? 0 : 1));
        });
        var probe = new HardwareEncoderProbe(processRunner, new FakeFfmpegToolLocator());

        var selection = await probe.SelectEncoderAsync(CancellationToken.None);

        Assert.Equal(VideoEncoderKind.IntelQuickSync, selection.Kind);
    }

    [Fact]
    public async Task SelectEncoderAsync_CalledTwiceOnSameInstance_OnlyProbesOnce()
    {
        var processRunner = new FakeProcessRunner((request, _) => Task.FromResult(Result(0)));
        var probe = new HardwareEncoderProbe(processRunner, new FakeFfmpegToolLocator());

        var first = await probe.SelectEncoderAsync(CancellationToken.None);
        var second = await probe.SelectEncoderAsync(CancellationToken.None);

        Assert.Equal(first.Kind, second.Kind);
        Assert.Single(processRunner.Requests);
    }

    [Fact]
    public async Task SelectEncoderAsync_FirstAttemptThrows_SecondAttemptRetriesRatherThanCachingFailure()
    {
        var attempt = 0;
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            var encoder = EncoderNameFromRequest(request);
            if (encoder == "h264_nvenc")
            {
                attempt++;
                return attempt == 1
                    ? throw new ProcessTimeoutException(TimeSpan.FromSeconds(15), string.Empty, string.Empty)
                    : Task.FromResult(Result(0));
            }

            return Task.FromResult(Result(1));
        });
        var probe = new HardwareEncoderProbe(processRunner, new FakeFfmpegToolLocator());

        // First call: NVENC smoke test throws (treated as failure, same as
        // ProcessLaunchException), every other candidate fails too in this
        // fake, so the whole probe throws and nothing is cached.
        await Assert.ThrowsAsync<RenderExecutionException>(() => probe.SelectEncoderAsync(CancellationToken.None));

        var second = await probe.SelectEncoderAsync(CancellationToken.None);

        Assert.Equal(VideoEncoderKind.NvidiaNvenc, second.Kind);
    }

    [Fact]
    public async Task SelectEncoderAsync_PreCancelledToken_ThrowsOperationCanceledExceptionBeforeAnyCandidate()
    {
        var processRunner = new FakeProcessRunner((_, _) => Task.FromResult(Result(0)));
        var probe = new HardwareEncoderProbe(processRunner, new FakeFfmpegToolLocator());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => probe.SelectEncoderAsync(cts.Token));
        Assert.Empty(processRunner.Requests);
    }
}
