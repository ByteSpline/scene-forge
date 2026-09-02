using BenchmarkDotNet.Attributes;
using SceneForge.Core.Resources;
using SceneForge.Media.Domain;
using SceneForge.Media.Sampling;

namespace SceneForge.Benchmarks.Sampling;

// Measures FrameSampler's own pooling/bounded-channel/timestamp-correlation
// pipeline (CLAUDE.md rule 9: benchmark with evidence) using a synthetic,
// in-memory frame source instead of a real ffmpeg process, so the numbers
// reflect the pipeline's overhead rather than decode speed or process I/O -
// those are ffmpeg's cost, not this code's.
[MemoryDiagnoser]
public class FrameSamplingBenchmarks
{
    // A fixed source resolution and frame count let the three profiles be
    // compared on their actual per-profile analysis dimensions (Fast
    // 320px.. Accurate 480px) while keeping each benchmark iteration's
    // work bounded and comparable.
    private const int SourceWidth = 1920;
    private const int SourceHeight = 1080;
    private const int TotalFrames = 300;

    private string _inputPath = string.Empty;

    [ParamsAllValues]
    public AnalysisProfile Profile { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        // FrameSampler validates that the input path exists on disk before
        // ever touching ffprobe/ffmpeg (both faked below); an empty file is
        // enough to satisfy that check.
        _inputPath = Path.GetTempFileName();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        if (File.Exists(_inputPath))
        {
            File.Delete(_inputPath);
        }
    }

    [Benchmark]
    public async Task<int> SampleFrames()
    {
        var options = FrameSamplingProfiles.GetDefaults(Profile);
        var dimensions = FrameDimensions.ForTargetWidth(SourceWidth, SourceHeight, options.AnalysisWidthPixels, options.PixelFormat);

        var mediaInfo = new MediaInfo
        {
            FilePath = _inputPath,
            FormatName = "mov,mp4,m4a,3gp,3g2,mj2",
            Duration = TimeSpan.FromSeconds(TotalFrames / options.SampleFramesPerSecond),
            VideoStreams =
            [
                new VideoStreamInfo
                {
                    Index = 0,
                    CodecName = "h264",
                    Width = SourceWidth,
                    Height = SourceHeight,
                    AverageFrameRate = new RationalFrameRate(30, 1),
                    RealBaseFrameRate = new RationalFrameRate(30, 1),
                    IsVariableFrameRate = false,
                    RotationDegrees = 0,
                },
            ],
            AudioStreams = [],
        };

        var launcher = new BenchmarkFrameSamplingProcessLauncher(() => new BenchmarkFrameSamplingProcess(
            new BenchmarkFrameSource(dimensions.ByteLength, TotalFrames),
            new BenchmarkShowInfoReader(TotalFrames, options.SampleFramesPerSecond)));

        var sampler = new FrameSampler(new BenchmarkFfmpegToolLocator(), new BenchmarkFfprobeService(mediaInfo), new AdaptiveResourceGovernor(), launcher);

        var framesSeen = 0;
        await foreach (var frame in sampler.SampleAsync(_inputPath, options, progress: null, CancellationToken.None))
        {
            framesSeen++;
            frame.Dispose();
        }

        return framesSeen;
    }
}
