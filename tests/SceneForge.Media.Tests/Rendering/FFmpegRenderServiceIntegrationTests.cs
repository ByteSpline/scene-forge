using SceneForge.Core.Resources;
using SceneForge.Media.Domain;
using SceneForge.Media.Planning;
using SceneForge.Media.Probing;
using SceneForge.Media.Processes;
using SceneForge.Media.Rendering;
using SceneForge.Media.Rendering.Internal;
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

    // Regression coverage for a real bug found in manual end-to-end testing
    // (Phase 16): a many-clip edit produced a filter graph long enough to
    // cross InlineFilterGraphCharacterThreshold, so FFmpegRenderService
    // took its "write the graph to a file" branch for the first time
    // against real data - and ffmpeg rejected the whole invocation with
    // "Unrecognized option 'filter_complex_script'. Error splitting the
    // argument list: Option not found". That option was deprecated in
    // ffmpeg 7.0 and removed in 8.0; the service now uses the generic
    // '-/filter_complex <file>' read-from-file form instead. The pre-Phase
    // 16 tests only ever exercised this path against a fake process runner
    // that never validated the argument name, which is why it survived to
    // manual testing. This test forces the file branch (dozens of clips),
    // asserts the graph really did exceed the inline threshold, and
    // requires the real ffmpeg encode to succeed and verify.
    [SkippableFact]
    public async Task RenderAsync_ManyClips_CrossesFilterScriptThreshold_RealFfmpegAcceptsFileForm()
    {
        Skip.IfNot(RealFfmpegAvailability.IsAvailable, RealFfmpegAvailability.SkipReason);
        Directory.CreateDirectory(_outputDirectory);

        var processRunner = new ProcessRunner();
        var toolLocator = new FfmpegToolLocator(processRunner);
        var ffprobeService = new FfprobeService(processRunner, toolLocator);

        var videoPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Media", "sample_video_audio.mp4");

        // The committed audio fixture is only ~1s; a many-clip plan needs a
        // longer target track, so synthesize one with the same real ffmpeg
        // the test already depends on (silent AAC, 8s).
        var audioPath = Path.Combine(_outputDirectory, "synthetic_target_audio.m4a");
        var audioGen = await processRunner.RunAsync(
            new ProcessExecutionRequest
            {
                FileName = RealFfmpegAvailability.FfmpegPath,
                Arguments = ["-hide_banner", "-y", "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo", "-t", "8", "-c:a", "aac", audioPath],
            },
            CancellationToken.None);
        Assert.True(audioGen.ExitCode == 0, $"Failed to synthesize the target audio track: {audioGen.StandardError}");

        var sourceMediaInfo = await ffprobeService.ProbeAsync(videoPath, CancellationToken.None);

        // 48 clips, each an exact 3-output-frame slice (0.12s at 25 fps) so
        // frame quantization is a no-op and this test isolates the
        // filter-script code path rather than re-testing duration drift
        // (RenderAsync_RealFfmpegWithFourNonFrameAlignedClips covers that).
        // Source start offsets are still deliberately varied and non-round.
        // 48 segment filters push the graph well past the inline threshold;
        // 48 x 0.12s = 5.76s of video, comfortably inside the 8s audio.
        const int clipCount = 48;
        var outputTimeBase = new RationalFrameRate(25, 1);
        var segmentDuration = outputTimeBase.FromFrameCount(3);
        var maxStart = sourceMediaInfo.Duration - segmentDuration - TimeSpan.FromMilliseconds(50);
        var placements = Enumerable.Range(0, clipCount)
            .Select(i => TimelinePlanBuilder.CreatePlacement(
                i,
                i,
                sourceStartSeconds: (i % 17) * (maxStart.TotalSeconds / 17.0),
                sourceDurationSeconds: segmentDuration.TotalSeconds))
            .ToArray();
        var timelinePlan = TimelinePlanBuilder.CreatePlan(placements, outputTimeBase);

        var renderPlan = new RenderPlanBuilder().Build(new RenderPlanRequest
        {
            TimelinePlan = timelinePlan,
            SourceFilePath = videoPath,
            SourceMediaInfo = sourceMediaInfo,
            OutputSpec = new RenderOutputSpec { Width = 160, Height = 120, FrameRate = outputTimeBase, FitMode = AspectFitMode.Letterbox },
            // Match the audio window to the frame-quantized video duration
            // so verification tests the render path, not an A/V length gap.
            Audio = new RenderAudioTrackSpec { FilePath = audioPath, TrimStart = TimeSpan.Zero, TrimDuration = segmentDuration * clipCount },
        });

        // White-box guard: prove this plan actually reaches the file branch,
        // so a future change to the graph builder or the threshold can't
        // silently turn this back into an inline-only test.
        var filterGraph = RenderFilterGraphBuilder.Build(renderPlan);
        _output.WriteLine($"Filter graph length: {filterGraph.Length} (inline threshold {FFmpegRenderService.InlineFilterGraphCharacterThreshold})");
        Assert.True(
            filterGraph.Length > FFmpegRenderService.InlineFilterGraphCharacterThreshold,
            $"Expected the {clipCount}-clip graph ({filterGraph.Length} chars) to exceed the inline threshold " +
            $"({FFmpegRenderService.InlineFilterGraphCharacterThreshold}); this test must exercise the filter-script file path.");

        var renderService = new FFmpegRenderService(processRunner, toolLocator, ffprobeService, new AdaptiveResourceGovernor());
        var outputPath = Path.Combine(_outputDirectory, "rendered_manyclips.mp4");

        var result = await renderService.RenderAsync(renderPlan, outputPath, progress: null, CancellationToken.None);

        _output.WriteLine($"PlannedVideoDuration={renderPlan.PlannedVideoDuration}, ActualDuration={result.Verification.ActualDuration}, Delta={result.Verification.DurationDelta}");

        Assert.True(File.Exists(outputPath));
        Assert.True(result.Verification.IsValid, $"Verification failed: {result.Verification}");
        Assert.True(result.Verification.DurationWithinTolerance);
        Assert.Single((await ffprobeService.ProbeAsync(outputPath, CancellationToken.None)).AudioStreams);
    }

    // Regression coverage for the real-world bug found in manual testing
    // after Phase 16: very little clean footage (a handful of clips) against
    // a long audio target makes TimelinePlanner repeat that footage
    // hundreds of times, and the single-filter_complex render architecture
    // then emits one ~7-node filter chain plus one implicit split output
    // PER placement - a graph ffmpeg 9.x fails to allocate ("Cannot
    // allocate memory") or schedules impossibly slowly. Past
    // InitialBatchSegmentCount, when the plan repeats a small distinct set,
    // FFmpegRenderService instead pre-renders each DISTINCT segment once and
    // assembles the output with the concat demuxer. This test drives that
    // path with real ffmpeg: 6 distinct 0.2s windows repeated to 150
    // placements (30s of output), and requires the concat assembly to
    // produce a frame-accurate, verified file.
    [SkippableFact]
    public async Task RenderAsync_HighRepetitionPlan_RealFfmpegPreRendersDistinctSegmentsAndConcats()
    {
        Skip.IfNot(RealFfmpegAvailability.IsAvailable, RealFfmpegAvailability.SkipReason);
        Directory.CreateDirectory(_outputDirectory);

        var processRunner = new ProcessRunner();
        var toolLocator = new FfmpegToolLocator(processRunner);
        var ffprobeService = new FfprobeService(processRunner, toolLocator);

        var videoPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Media", "sample_video_audio.mp4");

        var audioPath = Path.Combine(_outputDirectory, "synthetic_target_audio.m4a");
        var audioGen = await processRunner.RunAsync(
            new ProcessExecutionRequest
            {
                FileName = RealFfmpegAvailability.FfmpegPath,
                Arguments = ["-hide_banner", "-y", "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo", "-t", "35", "-c:a", "aac", audioPath],
            },
            CancellationToken.None);
        Assert.True(audioGen.ExitCode == 0, $"Failed to synthesize the target audio track: {audioGen.StandardError}");

        var sourceMediaInfo = await ffprobeService.ProbeAsync(videoPath, CancellationToken.None);

        var outputTimeBase = new RationalFrameRate(25, 1);
        var windowDuration = outputTimeBase.FromFrameCount(5); // 0.2s
        const int distinctWindows = 6;
        const int segmentCount = 150;

        var placements = Enumerable.Range(0, segmentCount)
            .Select(i =>
            {
                var w = i % distinctWindows;
                return TimelinePlanBuilder.CreatePlacement(
                    i,
                    w,
                    sourceStartSeconds: w * 0.25,
                    sourceDurationSeconds: windowDuration.TotalSeconds);
            })
            .ToArray();
        var timelinePlan = TimelinePlanBuilder.CreatePlan(placements, outputTimeBase);

        var renderPlan = new RenderPlanBuilder().Build(new RenderPlanRequest
        {
            TimelinePlan = timelinePlan,
            SourceFilePath = videoPath,
            SourceMediaInfo = sourceMediaInfo,
            OutputSpec = new RenderOutputSpec { Width = 160, Height = 120, FrameRate = outputTimeBase, FitMode = AspectFitMode.Letterbox },
            Audio = new RenderAudioTrackSpec { FilePath = audioPath, TrimStart = TimeSpan.Zero, TrimDuration = timelinePlan.PlannedDuration },
        });

        Assert.Equal(FFmpegRenderService.RenderStrategy.DistinctDedup, FFmpegRenderService.SelectRenderStrategy(renderPlan));

        var renderService = new FFmpegRenderService(processRunner, toolLocator, ffprobeService, new AdaptiveResourceGovernor());
        var outputPath = Path.Combine(_outputDirectory, "rendered_highrep.mp4");

        var progressUpdates = new List<RenderProgress>();
        var result = await renderService.RenderAsync(renderPlan, outputPath, new Progress<RenderProgress>(progressUpdates.Add), CancellationToken.None);

        _output.WriteLine($"PlannedVideoDuration={renderPlan.PlannedVideoDuration}, ActualDuration={result.Verification.ActualDuration}, Delta={result.Verification.DurationDelta}, Tolerance={result.Verification.DurationTolerance}");

        Assert.True(File.Exists(outputPath));
        Assert.True(result.Verification.IsValid, $"Verification failed: {result.Verification}");
        Assert.True(result.Verification.DurationWithinTolerance);
        Assert.Single((await ffprobeService.ProbeAsync(outputPath, CancellationToken.None)).AudioStreams);
    }

    // The SECOND real-world failure shape: a source with plenty of clean
    // footage fills the audio target with hundreds of DISTINCT segments and
    // little or no repetition, so the distinct-dedup strategy does not
    // apply - yet the single-pass filter_complex graph is just as infeasible
    // as the high-repetition case. The Batched strategy renders the timeline
    // in bounded filter_complex batches (starting at InitialBatchSegmentCount,
    // self-correcting smaller on any memory failure - see FFmpegRenderService)
    // and concat-demuxes the batch outputs. This test drives that path with
    // real ffmpeg: 70 distinct windows (no repetition) -> 2 batches -> a
    // frame-accurate, verified 28s output.
    [SkippableFact]
    public async Task RenderAsync_LargeAllDistinctPlan_RealFfmpegRendersInBoundedBatchesAndConcats()
    {
        Skip.IfNot(RealFfmpegAvailability.IsAvailable, RealFfmpegAvailability.SkipReason);
        Directory.CreateDirectory(_outputDirectory);

        var processRunner = new ProcessRunner();
        var toolLocator = new FfmpegToolLocator(processRunner);
        var ffprobeService = new FfprobeService(processRunner, toolLocator);

        var videoPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Media", "sample_video_audio.mp4");

        var audioPath = Path.Combine(_outputDirectory, "synthetic_target_audio.m4a");
        var audioGen = await processRunner.RunAsync(
            new ProcessExecutionRequest
            {
                FileName = RealFfmpegAvailability.FfmpegPath,
                Arguments = ["-hide_banner", "-y", "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo", "-t", "32", "-c:a", "aac", audioPath],
            },
            CancellationToken.None);
        Assert.True(audioGen.ExitCode == 0, $"Failed to synthesize the target audio track: {audioGen.StandardError}");

        var sourceMediaInfo = await ffprobeService.ProbeAsync(videoPath, CancellationToken.None);

        var outputTimeBase = new RationalFrameRate(25, 1);
        var segmentDuration = outputTimeBase.FromFrameCount(10); // 0.4s
        const int segmentCount = 70; // all distinct -> 2 batches of <=60

        var placements = Enumerable.Range(0, segmentCount)
            .Select(i => TimelinePlanBuilder.CreatePlacement(
                i,
                i,
                sourceStartSeconds: i * 0.02, // 0..1.38s, all inside the ~2s source
                sourceDurationSeconds: segmentDuration.TotalSeconds))
            .ToArray();
        var timelinePlan = TimelinePlanBuilder.CreatePlan(placements, outputTimeBase);

        var renderPlan = new RenderPlanBuilder().Build(new RenderPlanRequest
        {
            TimelinePlan = timelinePlan,
            SourceFilePath = videoPath,
            SourceMediaInfo = sourceMediaInfo,
            OutputSpec = new RenderOutputSpec { Width = 160, Height = 120, FrameRate = outputTimeBase, FitMode = AspectFitMode.Letterbox },
            Audio = new RenderAudioTrackSpec { FilePath = audioPath, TrimStart = TimeSpan.Zero, TrimDuration = timelinePlan.PlannedDuration },
        });

        Assert.Equal(FFmpegRenderService.RenderStrategy.Batched, FFmpegRenderService.SelectRenderStrategy(renderPlan));

        var renderService = new FFmpegRenderService(processRunner, toolLocator, ffprobeService, new AdaptiveResourceGovernor());
        var outputPath = Path.Combine(_outputDirectory, "rendered_alldistinct.mp4");

        var result = await renderService.RenderAsync(renderPlan, outputPath, progress: null, CancellationToken.None);

        _output.WriteLine($"PlannedVideoDuration={renderPlan.PlannedVideoDuration}, ActualDuration={result.Verification.ActualDuration}, Delta={result.Verification.DurationDelta}, Tolerance={result.Verification.DurationTolerance}");

        Assert.True(File.Exists(outputPath));
        Assert.True(result.Verification.IsValid, $"Verification failed: {result.Verification}");
        Assert.True(result.Verification.DurationWithinTolerance);
        Assert.Single((await ffprobeService.ProbeAsync(outputPath, CancellationToken.None)).AudioStreams);
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
