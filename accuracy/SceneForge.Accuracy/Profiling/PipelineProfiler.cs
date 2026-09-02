using System.Diagnostics;
using SceneForge.Accuracy.Evaluation;
using SceneForge.Core.Resources;
using SceneForge.Media.Detection;
using SceneForge.Media.Domain;
using SceneForge.Media.Extraction;
using SceneForge.Media.Planning;
using SceneForge.Media.Probing;
using SceneForge.Media.Processes;
using SceneForge.Media.Rendering;
using SceneForge.Media.Sampling;
using SceneForge.Media.Tooling;

namespace SceneForge.Accuracy.Profiling;

// Runs the real Detect -> SceneRangeCalculator -> Extract -> Plan -> Render
// pipeline end to end against one real input file, at one AnalysisProfile,
// capturing per-stage wall clock plus whole-run throughput/CPU/memory/disk
// evidence (CLAUDE.md rule 9). Unlike FixtureEvaluationRunner (32 small
// synthetic fixtures, scored for correctness against committed ground
// truth), this measures resource cost on one large, realistic-scale input -
// it carries no ground truth and is never used for accuracy scoring; that
// stays the fixture matrix's job (see docs/ACCURACY_REPORT.md).
//
// Render is a genuine, bounded pass (not the full input's length) - a short
// TargetAudioDuration timeline built from whatever clips Extract accepted,
// rendered and verified via RenderOutputVerifier - proving the whole chain
// still produces valid output post-optimization without paying for a full
// 30-minute re-encode every run.
public static class PipelineProfiler
{
    private static readonly TimeSpan RenderTargetDuration = TimeSpan.FromSeconds(90);
    private static readonly RationalFrameRate OutputTimeBase = new(25, 1);

    public static async Task<PipelineProfileReport> RunAsync(
        string applicationBaseDirectory,
        string inputFilePath,
        AnalysisProfile profile,
        CancellationToken cancellationToken)
    {
        var processRunner = new ProcessRunner();
        var toolLocator = new FfmpegToolLocator(processRunner, applicationBaseDirectory);
        var ffprobeService = new FfprobeService(processRunner, toolLocator);
        var resourceGovernor = new AdaptiveResourceGovernor();
        var frameSampler = new FrameSampler(toolLocator, ffprobeService, resourceGovernor);
        var detector = new TransitionDetector(frameSampler, ffprobeService);
        var extractor = new CleanClipExtractor(frameSampler, ffprobeService);
        var planner = new TimelinePlanner();
        var renderPlanBuilder = new RenderPlanBuilder();
        var renderService = new FFmpegRenderService(processRunner, toolLocator, ffprobeService, resourceGovernor);

        var outputDirectory = Directory.CreateTempSubdirectory("sceneforge-profiling-run");
        try
        {
            var freeDiskBefore = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(outputDirectory.FullName)) ?? outputDirectory.FullName).AvailableFreeSpace;
            var currentProcess = Process.GetCurrentProcess();
            var cpuTimeBefore = currentProcess.TotalProcessorTime;

            var mediaInfo = await ffprobeService.ProbeAsync(inputFilePath, cancellationToken).ConfigureAwait(false);

            await using var sampler = new ResourceUsageSampler();
            var totalStopwatch = Stopwatch.StartNew();

            var detectStopwatch = Stopwatch.StartNew();
            var detectionOptions = TransitionDetectionOptions.ForProfile(profile);
            var detections = await detector.DetectAsync(inputFilePath, detectionOptions, progress: null, cancellationToken).ConfigureAwait(false);
            detectStopwatch.Stop();
            var detectStage = new StageProfile(detectStopwatch.Elapsed.TotalSeconds, detections.Count, "ItemCount = transitions found");

            var sceneRangeResult = SceneRangeCalculator.Calculate(mediaInfo.Duration, detections);

            var extractStopwatch = Stopwatch.StartNew();
            var extractionOptions = CleanClipExtractionOptions.ForProfile(profile, sceneRangeResult.SceneRanges, sceneRangeResult.ExcludedIntervals);
            var extractionResult = await extractor.ExtractAsync(inputFilePath, extractionOptions, progress: null, cancellationToken).ConfigureAwait(false);
            extractStopwatch.Stop();
            var extractStage = new StageProfile(
                extractStopwatch.Elapsed.TotalSeconds,
                extractionResult.AcceptedClips.Count + extractionResult.RejectedClips.Count,
                "ItemCount = candidates scored");

            StageProfile? renderStage = null;
            bool? renderValid = null;
            if (extractionResult.AcceptedClips.Count > 0)
            {
                var renderStopwatch = Stopwatch.StartNew();
                var audioPath = await BuildSilentAudioAsync(toolLocator, processRunner, outputDirectory.FullName, cancellationToken).ConfigureAwait(false);
                var audioMediaInfo = await ffprobeService.ProbeAsync(audioPath, cancellationToken).ConfigureAwait(false);

                var timelineRequest = new TimelinePlanRequest
                {
                    AvailableClips = extractionResult.AcceptedClips,
                    TargetAudioDuration = audioMediaInfo.Duration < RenderTargetDuration ? audioMediaInfo.Duration : RenderTargetDuration,
                    OutputTimeBase = OutputTimeBase,
                    Seed = 1,
                };
                var timelinePlan = planner.Plan(timelineRequest, cancellationToken);

                var renderPlanRequest = new RenderPlanRequest
                {
                    TimelinePlan = timelinePlan,
                    SourceFilePath = inputFilePath,
                    SourceMediaInfo = mediaInfo,
                    OutputSpec = new RenderOutputSpec { Width = SyntheticProfilingSourceBuilder.Width, Height = SyntheticProfilingSourceBuilder.Height, FrameRate = OutputTimeBase },
                    Audio = new RenderAudioTrackSpec { FilePath = audioPath, TrimStart = TimeSpan.Zero, TrimDuration = timelinePlan.PlannedDuration },
                };
                var renderPlan = renderPlanBuilder.Build(renderPlanRequest);

                var renderOutputPath = Path.Combine(outputDirectory.FullName, "rendered.mp4");
                var renderResult = await renderService.RenderAsync(renderPlan, renderOutputPath, progress: null, cancellationToken).ConfigureAwait(false);
                renderStopwatch.Stop();
                renderValid = renderResult.Verification.IsValid;
                renderStage = new StageProfile(renderStopwatch.Elapsed.TotalSeconds, 1, $"ItemCount = 1 render; encoder={renderResult.Encoder.FfmpegEncoderName}; fellBack={renderResult.FellBackToSoftwareEncoder}");
            }

            totalStopwatch.Stop();
            currentProcess.Refresh();
            var cpuTimeAfter = currentProcess.TotalProcessorTime;
            var freeDiskAfter = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(outputDirectory.FullName)) ?? outputDirectory.FullName).AvailableFreeSpace;

            var wallClockSeconds = totalStopwatch.Elapsed.TotalSeconds;
            var throughput = wallClockSeconds <= 0 ? 0.0 : mediaInfo.Duration.TotalSeconds / wallClockSeconds;

            return new PipelineProfileReport(
                DateTimeOffset.UtcNow,
                await TryGetCommitShaAsync(cancellationToken).ConfigureAwait(false),
                profile,
                inputFilePath,
                mediaInfo.Duration.TotalSeconds,
                detectStage,
                extractStage,
                renderStage,
                detections.Count,
                extractionResult.AcceptedClips.Count,
                extractionResult.RejectedClips.Count,
                renderValid,
                wallClockSeconds,
                throughput,
                sampler.PeakManagedMemoryBytes,
                sampler.PeakWorkingSetBytes,
                (cpuTimeAfter - cpuTimeBefore).TotalSeconds,
                freeDiskBefore,
                freeDiskAfter);
        }
        finally
        {
            outputDirectory.Delete(recursive: true);
        }
    }

    private static async Task<string> BuildSilentAudioAsync(FfmpegToolLocator toolLocator, ProcessRunner processRunner, string workingDirectory, CancellationToken cancellationToken)
    {
        var tools = await toolLocator.LocateAsync(cancellationToken).ConfigureAwait(false);
        var outputPath = Path.Combine(workingDirectory, "silent-audio.m4a");
        var result = await processRunner.RunAsync(
            new ProcessExecutionRequest
            {
                FileName = tools.FfmpegPath,
                Arguments =
                [
                    "-y", "-hide_banner", "-loglevel", "error",
                    "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo",
                    "-t", RenderTargetDuration.TotalSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
                    "-c:a", "aac", outputPath,
                ],
                Timeout = TimeSpan.FromSeconds(30),
            },
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg silent-audio build failed (exit {result.ExitCode}):\n{result.StandardError}");
        }

        return outputPath;
    }

    private static async Task<string?> TryGetCommitShaAsync(CancellationToken cancellationToken)
    {
        try
        {
            var runner = new ProcessRunner();
            var result = await runner.RunAsync(
                new ProcessExecutionRequest
                {
                    FileName = "git",
                    Arguments = ["rev-parse", "HEAD"],
                    Timeout = TimeSpan.FromSeconds(5),
                },
                cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }
}
