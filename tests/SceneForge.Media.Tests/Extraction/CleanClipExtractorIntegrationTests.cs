using SceneForge.Core.Resources;
using SceneForge.Media.Domain;
using SceneForge.Media.Extraction;
using SceneForge.Media.Extraction.Export;
using SceneForge.Media.Probing;
using SceneForge.Media.Processes;
using SceneForge.Media.Sampling;
using SceneForge.Media.Tests.TestSupport;
using SceneForge.Media.Tooling;
using Xunit.Abstractions;

namespace SceneForge.Media.Tests.Extraction;

// Exercises the real command line - ProcessRunner/FfmpegToolLocator for
// probing, a real spawned ffmpeg process for decoding - end to end through
// the whole CleanClipExtractor pipeline against the same small synthetic
// fixture FrameSamplerIntegrationTests uses
// (Fixtures/Media/sample_video_audio.mp4: h264, 320x240, 25 fps, ~2s).
// Skipped whenever tools/ffmpeg is absent, same as every other real-binary
// test in this project. The fixture is only ~2s, so clip-duration bounds
// are relaxed well below the 3-5s production default for this test alone -
// the production default itself is exercised only against synthetic
// ClipFrameMetrics (ClipScorerTests) and faked frame streams
// (CleanClipExtractorTests), never against this short a real source.
public class CleanClipExtractorIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public CleanClipExtractorIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableFact]
    public async Task ExtractAsync_RealFfmpegAgainstRealVideoFile_ProducesExplainableScoredClipsAndExportsToJson()
    {
        Skip.IfNot(RealFfmpegAvailability.IsAvailable, RealFfmpegAvailability.SkipReason);

        var extractor = CreateExtractor();
        var mediaPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Media", "sample_video_audio.mp4");

        var options = new CleanClipExtractionOptions
        {
            SamplingOptions = new FrameSamplingOptions { AnalysisWidthPixels = 64, SampleFramesPerSecond = 5.0 },
            SceneRanges = [new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(2))],
            Scoring = new CleanClipScoringOptions
            {
                MinClipDuration = TimeSpan.FromMilliseconds(500),
                MaxClipDuration = TimeSpan.FromSeconds(1),
                BoundaryGuard = TimeSpan.Zero,
            },
        };

        var result = await extractor.ExtractAsync(mediaPath, options, progress: null, CancellationToken.None);

        _output.WriteLine($"Accepted: {result.AcceptedClips.Count}, Rejected: {result.RejectedClips.Count}, Remaining ranges: {result.RemainingCleanRanges.Count}");

        var allClips = result.AcceptedClips.Concat(result.RejectedClips).ToList();
        Assert.NotEmpty(allClips);
        Assert.All(allClips, c => Assert.Equal(8, c.Score.Reasons.Count));
        Assert.All(allClips, c => Assert.InRange(c.Score.Overall, 0.0, 1.0));

        using var stream = new MemoryStream();
        await CleanClipJsonWriter.WriteAsync(result, stream);
        Assert.True(stream.Length > 0);
    }

    private static CleanClipExtractor CreateExtractor()
    {
        var processRunner = new ProcessRunner();
        var toolLocator = new FfmpegToolLocator(processRunner);
        var ffprobeService = new FfprobeService(processRunner, toolLocator);
        var frameSampler = new FrameSampler(toolLocator, ffprobeService, new AdaptiveResourceGovernor());
        return new CleanClipExtractor(frameSampler, ffprobeService);
    }
}
