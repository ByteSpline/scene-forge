using SceneForge.Media.Domain;
using SceneForge.Media.Processes;
using SceneForge.Media.Rendering;
using SceneForge.Media.Rendering.Internal;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Rendering;

public class RenderOutputVerifierTests
{
    private static readonly RationalFrameRate TwentyFiveFps = new(25, 1);

    private static RenderPlan CreatePlan(TimeSpan plannedDuration) => new()
    {
        SourceFilePath = "source.mp4",
        Segments = [new RenderSegment { Position = 0, SourceStart = TimeSpan.Zero, SourceDuration = plannedDuration, IsTrimmed = false }],
        OutputSpec = new RenderOutputSpec { Width = 640, Height = 360, FrameRate = TwentyFiveFps },
        Audio = new RenderAudioTrackSpec { FilePath = "audio.m4a", TrimDuration = plannedDuration },
        SourceRotationDegrees = 0,
        PlannedVideoDuration = plannedDuration,
    };

    private static ProcessExecutionResult Result(int exitCode) => new() { ExitCode = exitCode, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero };

    [Fact]
    public async Task VerifyAsync_MatchingOutputAndAllFramesDecodable_ReturnsValidResult()
    {
        var plan = CreatePlan(TimeSpan.FromSeconds(6));
        var mediaInfo = MediaInfoBuilder.CreateVideoWithAudio("out.mp4", durationSeconds: 6);
        var ffprobeService = FakeFfprobeService.ReturningMediaInfo(mediaInfo);
        var processRunner = new FakeProcessRunner((_, _) => Task.FromResult(Result(0)));
        var verifier = new RenderOutputVerifier(ffprobeService, processRunner, new FakeFfmpegToolLocator());

        var result = await verifier.VerifyAsync("out.mp4", plan, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Empty(result.Failures);
        Assert.Equal(3, processRunner.Requests.Count);
    }

    [Fact]
    public async Task VerifyAsync_DurationBeyondOneFrameTolerance_ReturnsInvalid()
    {
        var plan = CreatePlan(TimeSpan.FromSeconds(6));
        // 6s + 1s is far outside a 1-frame (0.04s @ 25fps) tolerance.
        var mediaInfo = MediaInfoBuilder.CreateVideoWithAudio("out.mp4", durationSeconds: 7);
        var ffprobeService = FakeFfprobeService.ReturningMediaInfo(mediaInfo);
        var processRunner = new FakeProcessRunner((_, _) => Task.FromResult(Result(0)));
        var verifier = new RenderOutputVerifier(ffprobeService, processRunner, new FakeFfmpegToolLocator());

        var result = await verifier.VerifyAsync("out.mp4", plan, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.False(result.DurationWithinTolerance);
        Assert.Contains(result.Failures, f => f.Contains("duration", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task VerifyAsync_DurationWithinOneFrameTolerance_PassesDurationCheck()
    {
        var plan = CreatePlan(TimeSpan.FromSeconds(6));
        // One 25fps frame is 0.04s; 6.02s is within tolerance.
        var mediaInfo = MediaInfoBuilder.CreateVideoWithAudio("out.mp4", durationSeconds: 6.02);
        var ffprobeService = FakeFfprobeService.ReturningMediaInfo(mediaInfo);
        var processRunner = new FakeProcessRunner((_, _) => Task.FromResult(Result(0)));
        var verifier = new RenderOutputVerifier(ffprobeService, processRunner, new FakeFfmpegToolLocator());

        var result = await verifier.VerifyAsync("out.mp4", plan, CancellationToken.None);

        Assert.True(result.DurationWithinTolerance);
    }

    [Fact]
    public async Task VerifyAsync_NoAudioStream_ReturnsInvalid()
    {
        var plan = CreatePlan(TimeSpan.FromSeconds(6));
        var mediaInfo = MediaInfoBuilder.CreateVideoOnly("out.mp4", durationSeconds: 6);
        var ffprobeService = FakeFfprobeService.ReturningMediaInfo(mediaInfo);
        var processRunner = new FakeProcessRunner((_, _) => Task.FromResult(Result(0)));
        var verifier = new RenderOutputVerifier(ffprobeService, processRunner, new FakeFfmpegToolLocator());

        var result = await verifier.VerifyAsync("out.mp4", plan, CancellationToken.None);

        Assert.False(result.HasExactlyOneAudioStream);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task VerifyAsync_TwoAudioStreams_ReturnsInvalid()
    {
        var plan = CreatePlan(TimeSpan.FromSeconds(6));
        var single = MediaInfoBuilder.CreateVideoWithAudio("out.mp4", durationSeconds: 6);
        var mediaInfo = single with { AudioStreams = [.. single.AudioStreams, .. single.AudioStreams] };
        var ffprobeService = FakeFfprobeService.ReturningMediaInfo(mediaInfo);
        var processRunner = new FakeProcessRunner((_, _) => Task.FromResult(Result(0)));
        var verifier = new RenderOutputVerifier(ffprobeService, processRunner, new FakeFfmpegToolLocator());

        var result = await verifier.VerifyAsync("out.mp4", plan, CancellationToken.None);

        Assert.False(result.HasExactlyOneAudioStream);
    }

    [Fact]
    public async Task VerifyAsync_NoVideoStream_ReturnsInvalid()
    {
        var plan = CreatePlan(TimeSpan.FromSeconds(6));
        var mediaInfo = MediaInfoBuilder.CreateVideoWithAudio("out.mp4", durationSeconds: 6) with { VideoStreams = [] };
        var ffprobeService = FakeFfprobeService.ReturningMediaInfo(mediaInfo);
        var processRunner = new FakeProcessRunner((_, _) => Task.FromResult(Result(0)));
        var verifier = new RenderOutputVerifier(ffprobeService, processRunner, new FakeFfmpegToolLocator());

        var result = await verifier.VerifyAsync("out.mp4", plan, CancellationToken.None);

        Assert.False(result.HasExpectedVideoStream);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task VerifyAsync_MiddleFrameFailsToDecode_ReportsMiddleFrameFailureOnly()
    {
        var plan = CreatePlan(TimeSpan.FromSeconds(6));
        var mediaInfo = MediaInfoBuilder.CreateVideoWithAudio("out.mp4", durationSeconds: 6);
        var ffprobeService = FakeFfprobeService.ReturningMediaInfo(mediaInfo);

        var callIndex = 0;
        var processRunner = new FakeProcessRunner((_, _) =>
        {
            callIndex++;
            // First decode call is the first frame (seek 0s), second is the
            // middle frame (seek 3s) - fail only the second.
            return Task.FromResult(Result(callIndex == 2 ? 1 : 0));
        });
        var verifier = new RenderOutputVerifier(ffprobeService, processRunner, new FakeFfmpegToolLocator());

        var result = await verifier.VerifyAsync("out.mp4", plan, CancellationToken.None);

        Assert.True(result.FirstFrameDecodable);
        Assert.False(result.MiddleFrameDecodable);
        Assert.True(result.LastFrameDecodable);
        Assert.False(result.IsValid);
    }
}
