using System.Globalization;
using SceneForge.Core.Resources;
using SceneForge.Media.Domain;
using SceneForge.Media.Processes;
using SceneForge.Media.Rendering;
using SceneForge.Media.Rendering.Internal;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Rendering;

// Focused, argument-level coverage of RenderDurationCorrector's ffmpeg
// invocation (the last-resort tier of FFmpegRenderService's duration-only
// self-correction loop - see FFmpegRenderServiceTests' duration-correction
// section for the end-to-end wiring). Confirms the "pad-if-short, then
// trim-to-exact on both streams" construction is guaranteed effective by
// what it asks ffmpeg to do, not merely by what a fake reports back.
public sealed class RenderDurationCorrectorTests : IDisposable
{
    private static readonly RationalFrameRate TwentyFiveFps = new(25, 1);
    private readonly string _outputDirectory;

    public RenderDurationCorrectorTests()
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

    private static RenderPlan CreatePlan(TimeSpan plannedDuration, int? audioBitRate = null) => new()
    {
        SourceFilePath = "source.mp4",
        Segments = [new RenderSegment { Position = 0, SourceStart = TimeSpan.Zero, SourceDuration = plannedDuration, IsTrimmed = false }],
        OutputSpec = new RenderOutputSpec { Width = 640, Height = 360, FrameRate = TwentyFiveFps },
        Audio = new RenderAudioTrackSpec { FilePath = "audio.m4a", TrimDuration = plannedDuration, BitRateBitsPerSecond = audioBitRate },
        SourceRotationDegrees = 0,
        PlannedVideoDuration = plannedDuration,
    };

    private static VideoEncoderSelection SoftwareSelection => new()
    {
        Kind = VideoEncoderKind.SoftwareX264,
        FfmpegEncoderName = "libx264",
        IsHardwareAccelerated = false,
    };

    [Fact]
    public async Task CorrectAsync_BuildsAPadThenTrimGraphPinnedToTheExactPlannedFrameCountAndDuration()
    {
        var plan = CreatePlan(TimeSpan.FromSeconds(3));
        var outputPath = Path.Combine(_outputDirectory, "out.mp4");
        File.WriteAllBytes(outputPath, [1, 2, 3]);

        ProcessExecutionRequest? captured = null;
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            captured = request;
            File.WriteAllBytes(request.Arguments[^1], [4, 5, 6]);
            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero });
        });
        var corrector = new RenderDurationCorrector(processRunner, new FakeFfmpegToolLocator(), new AdaptiveResourceGovernor());

        await corrector.CorrectAsync(outputPath, plan, SoftwareSelection, CancellationToken.None);

        Assert.NotNull(captured);
        var args = captured!.Arguments.ToList();

        Assert.Equal("-i", args[args.IndexOf("-i")]);
        Assert.Equal(outputPath, args[args.IndexOf("-i") + 1]);

        var expectedFrameCount = plan.OutputSpec.FrameRate.ToFrameCount(plan.PlannedVideoDuration);
        var videoFilter = args[args.IndexOf("-vf") + 1];
        Assert.Contains("fps=25/1", videoFilter);
        Assert.Contains("tpad=stop_mode=clone:stop_duration=3", videoFilter);
        Assert.Contains($"trim=start_frame=0:end_frame={expectedFrameCount.ToString(CultureInfo.InvariantCulture)}", videoFilter);
        Assert.Contains("setpts=PTS-STARTPTS", videoFilter);

        var audioFilter = args[args.IndexOf("-af") + 1];
        Assert.Contains("apad=whole_dur=3", audioFilter);
        Assert.Contains("atrim=duration=3", audioFilter);
        Assert.Contains("asetpts=PTS-STARTPTS", audioFilter);

        Assert.Equal("libx264", args[args.IndexOf("-c:v") + 1]);
        Assert.Equal("aac", args[args.IndexOf("-c:a") + 1]);
        Assert.Equal("48000", args[args.IndexOf("-ar") + 1]);
        Assert.Equal("2", args[args.IndexOf("-ac") + 1]);
        Assert.Contains("+faststart", args);
    }

    [Fact]
    public async Task CorrectAsync_IncludesAudioBitRateWhenThePlanSpecifiesOne()
    {
        var plan = CreatePlan(TimeSpan.FromSeconds(3), audioBitRate: 192_000);
        var outputPath = Path.Combine(_outputDirectory, "out.mp4");
        File.WriteAllBytes(outputPath, [1]);

        ProcessExecutionRequest? captured = null;
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            captured = request;
            File.WriteAllBytes(request.Arguments[^1], [1]);
            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero });
        });
        var corrector = new RenderDurationCorrector(processRunner, new FakeFfmpegToolLocator(), new AdaptiveResourceGovernor());

        await corrector.CorrectAsync(outputPath, plan, SoftwareSelection, CancellationToken.None);

        var args = captured!.Arguments.ToList();
        Assert.Equal("192000", args[args.IndexOf("-b:a") + 1]);
    }

    [Fact]
    public async Task CorrectAsync_ReplacesTheOutputFileWithTheCorrectedCopyAndCleansUpTheTemporaryFile()
    {
        var plan = CreatePlan(TimeSpan.FromSeconds(3));
        var outputPath = Path.Combine(_outputDirectory, "out.mp4");
        File.WriteAllBytes(outputPath, [1, 1, 1]);

        string? correctedPath = null;
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            correctedPath = request.Arguments[^1];
            File.WriteAllBytes(correctedPath, [2, 2, 2, 2]);
            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero });
        });
        var corrector = new RenderDurationCorrector(processRunner, new FakeFfmpegToolLocator(), new AdaptiveResourceGovernor());

        await corrector.CorrectAsync(outputPath, plan, SoftwareSelection, CancellationToken.None);

        Assert.NotNull(correctedPath);
        Assert.NotEqual(outputPath, correctedPath);
        Assert.False(File.Exists(correctedPath), "the temporary corrected-copy file must not be left behind");
        Assert.True(File.Exists(outputPath));
        Assert.Equal(new byte[] { 2, 2, 2, 2 }, await File.ReadAllBytesAsync(outputPath));
    }

    [Fact]
    public async Task CorrectAsync_TheFileSwapItselfFails_LeavesTheAlreadyValidRenderInPlaceRatherThanLosingBothCopies()
    {
        // Regression: the swap was previously File.Delete(output) then
        // File.Move(corrected, output) - if the delete succeeded and the
        // move then failed (a transient handle/scanner race on the output
        // path), the user was left with NEITHER file. A single-call replace
        // keeps the original render whenever the swap cannot complete.
        var plan = CreatePlan(TimeSpan.FromSeconds(3));
        var outputPath = Path.Combine(_outputDirectory, "out.mp4");
        File.WriteAllBytes(outputPath, [7, 7, 7]);

        var swapBlockers = new List<FileStream>();
        var processRunner = new FakeProcessRunner((request, _) =>
        {
            var correctedPath = request.Arguments[^1];
            File.WriteAllBytes(correctedPath, [8, 8, 8, 8]);
            // Hold the corrected copy open with no sharing so the move out
            // of it cannot succeed - a deterministic stand-in for the real
            // transient-lock race on the swap.
            swapBlockers.Add(new FileStream(correctedPath, FileMode.Open, FileAccess.Read, FileShare.None));
            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "", StandardError = "", Elapsed = TimeSpan.Zero });
        });
        var corrector = new RenderDurationCorrector(processRunner, new FakeFfmpegToolLocator(), new AdaptiveResourceGovernor());

        try
        {
            await Assert.ThrowsAnyAsync<IOException>(
                () => corrector.CorrectAsync(outputPath, plan, SoftwareSelection, CancellationToken.None));

            Assert.True(File.Exists(outputPath), "the already-valid render must survive a failed swap");
            Assert.Equal(new byte[] { 7, 7, 7 }, await File.ReadAllBytesAsync(outputPath));
        }
        finally
        {
            foreach (var blocker in swapBlockers)
            {
                blocker.Dispose();
            }
        }
    }

    [Fact]
    public async Task CorrectAsync_FfmpegFails_ThrowsRenderExecutionExceptionAndLeavesTheOriginalOutputUntouched()
    {
        var plan = CreatePlan(TimeSpan.FromSeconds(3));
        var outputPath = Path.Combine(_outputDirectory, "out.mp4");
        File.WriteAllBytes(outputPath, [9, 9, 9]);

        var processRunner = new FakeProcessRunner((_, _) =>
            Task.FromResult(new ProcessExecutionResult { ExitCode = 1, StandardOutput = "", StandardError = "corrective pass boom", Elapsed = TimeSpan.Zero }));
        var corrector = new RenderDurationCorrector(processRunner, new FakeFfmpegToolLocator(), new AdaptiveResourceGovernor());

        var exception = await Assert.ThrowsAsync<RenderExecutionException>(
            () => corrector.CorrectAsync(outputPath, plan, SoftwareSelection, CancellationToken.None));

        Assert.Contains("corrective pass boom", exception.Message);
        Assert.True(File.Exists(outputPath), "the original output must survive a failed correction attempt");
        Assert.Equal(new byte[] { 9, 9, 9 }, await File.ReadAllBytesAsync(outputPath));
    }
}
