using SceneForge.Core.Resources;
using SceneForge.Media.Domain;
using SceneForge.Media.Planning;
using SceneForge.Media.Probing;
using SceneForge.Media.Processes;
using SceneForge.Media.Rendering;
using SceneForge.Media.Tests.TestSupport;
using SceneForge.Media.Tooling;
using Xunit.Abstractions;

namespace SceneForge.Media.Tests.Rendering;

// Exercises the real command line end to end - ProcessRunner/FfmpegToolLocator,
// a real spawned ffmpeg encode, and real ffprobe/decode verification - against
// the same small synthetic fixtures the rest of this project's integration
// tests use (Fixtures/Media/sample_video_audio.mp4: h264, 320x240, 25 fps,
// ~2s; Fixtures/Media/sample_audio_only.m4a as the supplied audio track).
// Skipped whenever tools/ffmpeg is absent, same as every other real-binary
// test in this project.
public sealed class FFmpegRenderServiceIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _outputDirectory;

    public FFmpegRenderServiceIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _outputDirectory = Path.Combine(Path.GetTempPath(), "SceneForgeRenderTests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDirectory))
        {
            Directory.Delete(_outputDirectory, recursive: true);
        }
    }

    [SkippableFact]
    public async Task RenderAsync_RealFfmpegAgainstRealFiles_ProducesVerifiedOutput()
    {
        Skip.IfNot(RealFfmpegAvailability.IsAvailable, RealFfmpegAvailability.SkipReason);
        Directory.CreateDirectory(_outputDirectory);

        var processRunner = new ProcessRunner();
        var toolLocator = new FfmpegToolLocator(processRunner);
        var ffprobeService = new FfprobeService(processRunner, toolLocator);

        var videoPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Media", "sample_video_audio.mp4");
        var audioPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Media", "sample_audio_only.m4a");

        var sourceMediaInfo = await ffprobeService.ProbeAsync(videoPath, CancellationToken.None);
        var audioMediaInfo = await ffprobeService.ProbeAsync(audioPath, CancellationToken.None);

        // Keep the planned duration comfortably inside both the ~2s video
        // fixture and whatever the audio fixture's own duration is, so this
        // test stays robust to either fixture changing length.
        var plannedHalfSegment = TimeSpan.FromTicks(Math.Min(
            TimeSpan.FromSeconds(0.6).Ticks,
            Math.Min(sourceMediaInfo.Duration.Ticks, audioMediaInfo.Duration.Ticks) / 3));

        var placements = new[]
        {
            TimelinePlanBuilder.CreatePlacement(0, 0, sourceStartSeconds: 0, sourceDurationSeconds: plannedHalfSegment.TotalSeconds),
            TimelinePlanBuilder.CreatePlacement(1, 1, sourceStartSeconds: plannedHalfSegment.TotalSeconds, sourceDurationSeconds: plannedHalfSegment.TotalSeconds),
        };
        var outputTimeBase = new RationalFrameRate(25, 1);
        var timelinePlan = TimelinePlanBuilder.CreatePlan(placements, outputTimeBase);

        var renderPlanRequest = new RenderPlanRequest
        {
            TimelinePlan = timelinePlan,
            SourceFilePath = videoPath,
            SourceMediaInfo = sourceMediaInfo,
            OutputSpec = new RenderOutputSpec { Width = 160, Height = 120, FrameRate = outputTimeBase, FitMode = AspectFitMode.Letterbox },
            Audio = new RenderAudioTrackSpec { FilePath = audioPath, TrimStart = TimeSpan.Zero, TrimDuration = timelinePlan.PlannedDuration },
        };

        var renderPlan = new RenderPlanBuilder().Build(renderPlanRequest);
        var renderService = new FFmpegRenderService(processRunner, toolLocator, ffprobeService, new AdaptiveResourceGovernor());

        var progressUpdates = new List<RenderProgress>();
        var progress = new Progress<RenderProgress>(progressUpdates.Add);
        var outputPath = Path.Combine(_outputDirectory, "rendered.mp4");

        var result = await renderService.RenderAsync(renderPlan, outputPath, progress, CancellationToken.None);

        _output.WriteLine($"Encoder: {result.Encoder.FfmpegEncoderName} (hardware: {result.Encoder.IsHardwareAccelerated}, fell back: {result.FellBackToSoftwareEncoder})");
        _output.WriteLine($"Verification: valid={result.Verification.IsValid}, duration delta={result.Verification.DurationDelta}");

        Assert.True(File.Exists(outputPath));
        Assert.True(result.Verification.IsValid);
        Assert.Single((await ffprobeService.ProbeAsync(outputPath, CancellationToken.None)).AudioStreams);
    }

    // Regression coverage for a real, measured bug that surfaced repeatedly
    // across Phase 9, Phase 12, and manual Phase 13 testing: ffmpeg's trim
    // filter keeps every source frame whose presentation time falls within
    // [start, start+duration) - for a segment duration that is not an
    // exact multiple of the output frame period, this deterministically
    // produces however many frames happen to overlap the trim window, not
    // whatever duration SceneForge originally requested. Because this
    // happens independently per segment, the discrepancy between the
    // rendered output's real duration and RenderPlan.PlannedVideoDuration
    // used to grow with clip count - proven here with more clips than the
    // 2-segment case above (still real, not the largest realistic edit,
    // but enough to make a per-segment-accumulating bug visible if the fix
    // regressed) using deliberately non-frame-aligned segment durations
    // (1/13th of the shorter fixture's own duration - not a round number
    // at 25fps). See docs/OPTIMIZATION_REPORT.md for the full investigation
    // and RenderPlanBuilderTests for the equivalent fake-media unit
    // coverage of the underlying frame-quantization fix.
    [SkippableFact]
    public async Task RenderAsync_RealFfmpegWithFourNonFrameAlignedClips_VerifiesWithinTolerance()
    {
        Skip.IfNot(RealFfmpegAvailability.IsAvailable, RealFfmpegAvailability.SkipReason);
        Directory.CreateDirectory(_outputDirectory);

        var processRunner = new ProcessRunner();
        var toolLocator = new FfmpegToolLocator(processRunner);
        var ffprobeService = new FfprobeService(processRunner, toolLocator);

        var videoPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Media", "sample_video_audio.mp4");
        var audioPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Media", "sample_audio_only.m4a");

        var sourceMediaInfo = await ffprobeService.ProbeAsync(videoPath, CancellationToken.None);
        var audioMediaInfo = await ffprobeService.ProbeAsync(audioPath, CancellationToken.None);

        const int clipCount = 4;
        var segmentDuration = TimeSpan.FromTicks(Math.Min(sourceMediaInfo.Duration.Ticks, audioMediaInfo.Duration.Ticks) / 13);
        var placements = Enumerable.Range(0, clipCount)
            .Select(i => TimelinePlanBuilder.CreatePlacement(i, i, sourceStartSeconds: i * segmentDuration.TotalSeconds, sourceDurationSeconds: segmentDuration.TotalSeconds))
            .ToArray();
        var outputTimeBase = new RationalFrameRate(25, 1);
        var timelinePlan = TimelinePlanBuilder.CreatePlan(placements, outputTimeBase);

        var renderPlanRequest = new RenderPlanRequest
        {
            TimelinePlan = timelinePlan,
            SourceFilePath = videoPath,
            SourceMediaInfo = sourceMediaInfo,
            OutputSpec = new RenderOutputSpec { Width = 160, Height = 120, FrameRate = outputTimeBase, FitMode = AspectFitMode.Letterbox },
            Audio = new RenderAudioTrackSpec { FilePath = audioPath, TrimStart = TimeSpan.Zero, TrimDuration = timelinePlan.PlannedDuration },
        };

        var renderPlan = new RenderPlanBuilder().Build(renderPlanRequest);
        var renderService = new FFmpegRenderService(processRunner, toolLocator, ffprobeService, new AdaptiveResourceGovernor());
        var outputPath = Path.Combine(_outputDirectory, "rendered.mp4");

        var result = await renderService.RenderAsync(renderPlan, outputPath, progress: null, CancellationToken.None);

        _output.WriteLine($"PlannedVideoDuration={renderPlan.PlannedVideoDuration}, ActualDuration={result.Verification.ActualDuration}, Delta={result.Verification.DurationDelta}, Tolerance={result.Verification.DurationTolerance}");

        Assert.True(result.Verification.IsValid, $"Verification failed: {result.Verification}");
        Assert.True(result.Verification.DurationWithinTolerance);
    }

    [SkippableFact]
    public async Task RenderAsync_RealFfmpegAgainstRealFiles_NeverContainsSourceAudioStreamMetadata()
    {
        Skip.IfNot(RealFfmpegAvailability.IsAvailable, RealFfmpegAvailability.SkipReason);
        Directory.CreateDirectory(_outputDirectory);

        var processRunner = new ProcessRunner();
        var toolLocator = new FfmpegToolLocator(processRunner);
        var ffprobeService = new FfprobeService(processRunner, toolLocator);

        var videoPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Media", "sample_video_audio.mp4");
        var audioPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Media", "sample_audio_only.m4a");

        var sourceMediaInfo = await ffprobeService.ProbeAsync(videoPath, CancellationToken.None);
        var audioMediaInfo = await ffprobeService.ProbeAsync(audioPath, CancellationToken.None);
        var segmentDuration = TimeSpan.FromTicks(Math.Min(
            TimeSpan.FromSeconds(1).Ticks,
            Math.Min(sourceMediaInfo.Duration.Ticks, audioMediaInfo.Duration.Ticks) - TimeSpan.FromMilliseconds(100).Ticks));

        var placements = new[] { TimelinePlanBuilder.CreatePlacement(0, 0, sourceStartSeconds: 0, sourceDurationSeconds: segmentDuration.TotalSeconds) };
        var outputTimeBase = new RationalFrameRate(25, 1);
        var timelinePlan = TimelinePlanBuilder.CreatePlan(placements, outputTimeBase);

        var renderPlanRequest = new RenderPlanRequest
        {
            TimelinePlan = timelinePlan,
            SourceFilePath = videoPath,
            SourceMediaInfo = sourceMediaInfo,
            OutputSpec = new RenderOutputSpec { Width = 160, Height = 120, FrameRate = outputTimeBase },
            Audio = new RenderAudioTrackSpec { FilePath = audioPath, TrimStart = TimeSpan.Zero, TrimDuration = timelinePlan.PlannedDuration },
        };

        var renderPlan = new RenderPlanBuilder().Build(renderPlanRequest);
        var renderService = new FFmpegRenderService(processRunner, toolLocator, ffprobeService, new AdaptiveResourceGovernor());
        var outputPath = Path.Combine(_outputDirectory, "rendered.mp4");

        await renderService.RenderAsync(renderPlan, outputPath, progress: null, CancellationToken.None);

        var outputMediaInfo = await ffprobeService.ProbeAsync(outputPath, CancellationToken.None);
        Assert.Single(outputMediaInfo.AudioStreams);
        Assert.Equal("aac", outputMediaInfo.PrimaryAudioStream?.CodecName);
    }
}
