using SceneForge.Media.Domain;
using SceneForge.Media.Probing;
using SceneForge.Media.Processes;
using SceneForge.Media.Rendering;
using SceneForge.Media.Rendering.Internal;
using SceneForge.Media.Tests.TestSupport;
using SceneForge.Media.Tooling;
using SceneForge.Media.Validation;

namespace SceneForge.Media.Tests.Rendering;

// Exercises FFmpegRenderService's argument-building, single-vs-fallback
// encoder handling, filter-script threshold, and verification wiring
// entirely against fakes (FakeProcessRunner/FakeFfmpegToolLocator/
// FakeFfprobeService and a fake IHardwareEncoderProbe) - no real ffmpeg
// process is spawned. See FFmpegRenderServiceIntegrationTests for the real-
// binary end-to-end coverage.
public sealed class FFmpegRenderServiceTests : IDisposable
{
    private static readonly RationalFrameRate TwentyFiveFps = new(25, 1);

    private readonly string _outputDirectory;

    public FFmpegRenderServiceTests()
    {
        _outputDirectory = Path.Combine(Path.GetTempPath(), "SceneForgeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDirectory))
        {
            Directory.Delete(_outputDirectory, recursive: true);
        }
    }

    private static RenderSegment CreateSegment(int position, double startSeconds, double durationSeconds) => new()
    {
        Position = position,
        SourceStart = TimeSpan.FromSeconds(startSeconds),
        SourceDuration = TimeSpan.FromSeconds(durationSeconds),
        IsTrimmed = false,
    };

    private static RenderPlan CreatePlan(int segmentCount = 1)
    {
        var segments = Enumerable.Range(0, segmentCount)
            .Select(i => CreateSegment(i, i * 10.0, 3.0))
            .ToList();
        var plannedDuration = segments.Aggregate(TimeSpan.Zero, (sum, s) => sum + s.SourceDuration);

        return new RenderPlan
        {
            SourceFilePath = "source.mp4",
            Segments = segments,
            OutputSpec = new RenderOutputSpec { Width = 640, Height = 360, FrameRate = TwentyFiveFps },
            Audio = new RenderAudioTrackSpec { FilePath = "audio.m4a", TrimDuration = plannedDuration },
            SourceRotationDegrees = 0,
            PlannedVideoDuration = plannedDuration,
        };
    }

    private static VideoEncoderSelection HardwareSelection => new()
    {
        Kind = VideoEncoderKind.NvidiaNvenc,
        FfmpegEncoderName = "h264_nvenc",
        IsHardwareAccelerated = true,
    };

    private static VideoEncoderSelection SoftwareSelection => new()
    {
        Kind = VideoEncoderKind.SoftwareX264,
        FfmpegEncoderName = "libx264",
        IsHardwareAccelerated = false,
    };

    private sealed class FakeEncoderProbe : IHardwareEncoderProbe
    {
        private readonly VideoEncoderSelection _selection;

        public FakeEncoderProbe(VideoEncoderSelection selection)
        {
            _selection = selection;
        }

        public Task<VideoEncoderSelection> SelectEncoderAsync(CancellationToken cancellationToken) => Task.FromResult(_selection);
    }

    private static bool IsRenderInvocation(ProcessExecutionRequest request) => request.Arguments.Contains("-progress");

    private static RenderOutputVerifier CreatePassingVerifier(FakeProcessRunner processRunner, TimeSpan expectedDuration)
    {
        var mediaInfo = MediaInfoBuilder.CreateVideoWithAudio("out.mp4", durationSeconds: expectedDuration.TotalSeconds);
        var ffprobeService = FakeFfprobeService.ReturningMediaInfo(mediaInfo);
        return new RenderOutputVerifier(ffprobeService, processRunner, new FakeFfmpegToolLocator());
    }

    private static RenderOutputVerifier CreateFailingVerifier(FakeProcessRunner processRunner)
    {
        // Reports a video-only file with double the expected duration -
        // fails both the audio-stream-count and duration-tolerance checks.
        var mediaInfo = MediaInfoBuilder.CreateVideoOnly("out.mp4", durationSeconds: 999);
        var ffprobeService = FakeFfprobeService.ReturningMediaInfo(mediaInfo);
        return new RenderOutputVerifier(ffprobeService, processRunner, new FakeFfmpegToolLocator());
    }

    [Fact]
    public async Task RenderAsync_NullPlan_Throws()
    {
        var processRunner = FakeProcessRunner.ReturningResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero });
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(SoftwareSelection), CreatePassingVerifier(processRunner, TimeSpan.FromSeconds(3)));

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.RenderAsync(null!, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None));
    }

    [Fact]
    public async Task RenderAsync_OutputPathEqualsSourceFile_ThrowsMediaValidationException()
    {
        var plan = CreatePlan() with { SourceFilePath = Path.Combine(_outputDirectory, "same.mp4") };
        var processRunner = FakeProcessRunner.ReturningResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero });
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(SoftwareSelection), CreatePassingVerifier(processRunner, plan.PlannedVideoDuration));

        await Assert.ThrowsAsync<MediaValidationException>(() => service.RenderAsync(plan, Path.Combine(_outputDirectory, "same.mp4"), null, CancellationToken.None));
    }

    [Fact]
    public async Task RenderAsync_HardwareEncoderSucceeds_ReturnsResultWithoutFallback()
    {
        var plan = CreatePlan();
        var processRunner = new FakeProcessRunner((request, _) => Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero }));
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(HardwareSelection), CreatePassingVerifier(processRunner, plan.PlannedVideoDuration));

        var result = await service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None);

        Assert.False(result.FellBackToSoftwareEncoder);
        Assert.Equal(VideoEncoderKind.NvidiaNvenc, result.Encoder.Kind);
        Assert.True(result.Verification.IsValid);

        var renderCalls = processRunner.Requests.Count(IsRenderInvocation);
        Assert.Equal(1, renderCalls);
    }

    [Fact]
    public async Task RenderAsync_HardwareEncoderFails_FallsBackToLibx264AndSucceeds()
    {
        var plan = CreatePlan();
        var renderAttempt = 0;
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            if (IsRenderInvocation(request))
            {
                renderAttempt++;
                var exitCode = renderAttempt == 1 ? 1 : 0;
                return Task.FromResult(new ProcessExecutionResult { ExitCode = exitCode, StandardOutput = "", StandardError = "simulated failure", Elapsed = TimeSpan.Zero });
            }

            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero });
        });
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(HardwareSelection), CreatePassingVerifier(processRunner, plan.PlannedVideoDuration));

        var result = await service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None);

        Assert.True(result.FellBackToSoftwareEncoder);
        Assert.Equal(VideoEncoderKind.SoftwareX264, result.Encoder.Kind);
        Assert.Equal(2, renderAttempt);
    }

    [Fact]
    public async Task RenderAsync_HardwareAndSoftwareBothFail_ThrowsRenderExecutionException()
    {
        var plan = CreatePlan();
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            var exitCode = IsRenderInvocation(request) ? 1 : 0;
            return Task.FromResult(new ProcessExecutionResult { ExitCode = exitCode, StandardOutput = "", StandardError = "boom", Elapsed = TimeSpan.Zero });
        });
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(HardwareSelection), CreatePassingVerifier(processRunner, plan.PlannedVideoDuration));

        await Assert.ThrowsAsync<RenderExecutionException>(() => service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None));
    }

    [Fact]
    public async Task RenderAsync_SoftwareEncoderFails_ThrowsImmediatelyWithoutRetry()
    {
        var plan = CreatePlan();
        var renderAttempts = 0;
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            if (IsRenderInvocation(request))
            {
                renderAttempts++;
            }

            var exitCode = IsRenderInvocation(request) ? 1 : 0;
            return Task.FromResult(new ProcessExecutionResult { ExitCode = exitCode, StandardOutput = "", StandardError = "boom", Elapsed = TimeSpan.Zero });
        });
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(SoftwareSelection), CreatePassingVerifier(processRunner, plan.PlannedVideoDuration));

        await Assert.ThrowsAsync<RenderExecutionException>(() => service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None));
        Assert.Equal(1, renderAttempts);
    }

    [Fact]
    public async Task RenderAsync_VerificationFails_ThrowsRenderVerificationExceptionCarryingResult()
    {
        var plan = CreatePlan();
        var processRunner = new FakeProcessRunner((_, _) => Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero }));
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(SoftwareSelection), CreateFailingVerifier(processRunner));

        var exception = await Assert.ThrowsAsync<RenderVerificationException>(() => service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None));

        Assert.False(exception.Result.IsValid);
        Assert.NotEmpty(exception.Result.Failures);
    }

    [Fact]
    public async Task RenderAsync_ReportsProgressParsedFromFfmpegStdout()
    {
        var plan = CreatePlan();
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            if (IsRenderInvocation(request))
            {
                request.OutputProgress?.Report(new ProcessOutputLine(ProcessOutputChannel.StandardOutput, "frame=5"));
                request.OutputProgress?.Report(new ProcessOutputLine(ProcessOutputChannel.StandardOutput, "out_time_us=1000000"));
                request.OutputProgress?.Report(new ProcessOutputLine(ProcessOutputChannel.StandardOutput, "speed=1x"));
                request.OutputProgress?.Report(new ProcessOutputLine(ProcessOutputChannel.StandardOutput, "progress=end"));
            }

            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero });
        });
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(SoftwareSelection), CreatePassingVerifier(processRunner, plan.PlannedVideoDuration));

        var progress = new RecordingProgress<RenderProgress>();

        await service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), progress, CancellationToken.None);

        Assert.Single(progress.Reports);
        Assert.Equal(5, progress.Reports[0].FrameNumber);
        Assert.True(progress.Reports[0].IsFinished);
    }

    [Fact]
    public async Task RenderAsync_ManySegments_UsesFilterComplexScriptFile_AndDeletesItAfterward()
    {
        var plan = CreatePlan(segmentCount: 400);
        string? observedScriptPath = null;
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            if (IsRenderInvocation(request))
            {
                var scriptIndex = request.Arguments.ToList().IndexOf("-filter_complex_script");
                Assert.True(scriptIndex >= 0, "Expected -filter_complex_script for a large segment count.");
                observedScriptPath = request.Arguments[scriptIndex + 1];
                Assert.True(File.Exists(observedScriptPath), "Filter script must exist while ffmpeg is running.");
                Assert.DoesNotContain("-filter_complex", request.Arguments.Where(a => a != "-filter_complex_script"));
            }

            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero });
        });
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(SoftwareSelection), CreatePassingVerifier(processRunner, plan.PlannedVideoDuration));

        await service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None);

        Assert.NotNull(observedScriptPath);
        Assert.False(File.Exists(observedScriptPath), "Filter script must be deleted after the render process exits.");
    }

    [Fact]
    public async Task RenderAsync_FewSegments_UsesInlineFilterComplex()
    {
        var plan = CreatePlan(segmentCount: 2);
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            if (IsRenderInvocation(request))
            {
                Assert.Contains("-filter_complex", request.Arguments);
                Assert.DoesNotContain("-filter_complex_script", request.Arguments);
            }

            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero });
        });
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(SoftwareSelection), CreatePassingVerifier(processRunner, plan.PlannedVideoDuration));

        await service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None);
    }

    [Fact]
    public async Task RenderAsync_NeverMapsSourceAudioStream()
    {
        var plan = CreatePlan();
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            if (IsRenderInvocation(request))
            {
                Assert.DoesNotContain("0:a", request.Arguments);
            }

            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero });
        });
        var service = new FFmpegRenderService(processRunner, new FakeFfmpegToolLocator(), new FakeEncoderProbe(SoftwareSelection), CreatePassingVerifier(processRunner, plan.PlannedVideoDuration));

        await service.RenderAsync(plan, Path.Combine(_outputDirectory, "out.mp4"), null, CancellationToken.None);
    }
}
