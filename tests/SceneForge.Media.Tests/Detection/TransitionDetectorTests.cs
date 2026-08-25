using SceneForge.Media.Detection;
using SceneForge.Media.Domain;
using SceneForge.Media.Sampling;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Detection;

public class TransitionDetectorTests
{
    [Fact]
    public async Task DetectAsync_IsolatedColorJump_DetectsHardCut()
    {
        // Both luma AND hue/saturation differ substantially (pure blue vs.
        // pure yellow) so both StructuralDifference (grayscale-based) and
        // HsvHistogramDistance (Hue/Saturation-based, ignores brightness)
        // cross their thresholds - a same-hue brightness-only jump would
        // pass the former but not the latter, and vice versa for a
        // same-luma hue-only jump.
        var frames = BuildFrames(
            (255, 0, 0, 3),
            (0, 255, 255, 3));
        var sampler = FakeFrameSampler.ReturningFrames(() => frames);
        var ffprobe = FakeFfprobeService.ReturningMediaInfo(CreateMediaInfo(TimeSpan.FromSeconds(2)));
        var detector = new TransitionDetector(sampler, ffprobe);

        var results = await detector.DetectAsync(
            "input.mp4",
            TransitionDetectionOptions.ForProfile(AnalysisProfile.Fast),
            null,
            CancellationToken.None);

        var detection = Assert.Single(results);
        Assert.Equal(TransitionType.HardCut, detection.Type);
        Assert.True(detection.Start < detection.End);
        Assert.InRange(detection.BoundaryTimestamp, detection.Start, detection.End);
        Assert.NotEmpty(detection.ContributingSignals);
        Assert.NotEmpty(detection.DiagnosticReason);
    }

    [Fact]
    public async Task DetectAsync_NoAbruptChange_DetectsNothing()
    {
        var frames = BuildFrames((100, 100, 100, 6));
        var sampler = FakeFrameSampler.ReturningFrames(() => frames);
        var ffprobe = FakeFfprobeService.ReturningMediaInfo(CreateMediaInfo(TimeSpan.FromSeconds(2)));
        var detector = new TransitionDetector(sampler, ffprobe);

        var results = await detector.DetectAsync(
            "input.mp4",
            TransitionDetectionOptions.ForProfile(AnalysisProfile.Fast),
            null,
            CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task DetectAsync_NoVideoStream_ThrowsWithoutInvokingFrameSampler()
    {
        var mediaInfo = new MediaInfo
        {
            FilePath = "audio.mp4",
            FormatName = "wav",
            Duration = TimeSpan.FromSeconds(1),
            VideoStreams = [],
            AudioStreams = [],
        };
        var sampler = FakeFrameSampler.ReturningFrames(() => throw new InvalidOperationException("Frame sampler must not be invoked when there is no video stream."));
        var detector = new TransitionDetector(sampler, FakeFfprobeService.ReturningMediaInfo(mediaInfo));

        await Assert.ThrowsAsync<TransitionDetectionException>(() => detector.DetectAsync(
            "audio.mp4",
            TransitionDetectionOptions.ForProfile(AnalysisProfile.Fast),
            null,
            CancellationToken.None));
    }

    [Fact]
    public async Task DetectAsync_Cancelled_ThrowsOperationCanceled()
    {
        var frames = BuildFrames((10, 10, 10, 6));
        var sampler = FakeFrameSampler.ReturningFrames(() => frames);
        var detector = new TransitionDetector(sampler, FakeFfprobeService.ReturningMediaInfo(CreateMediaInfo(TimeSpan.FromSeconds(2))));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => detector.DetectAsync(
            "input.mp4",
            TransitionDetectionOptions.ForProfile(AnalysisProfile.Fast),
            null,
            cts.Token));
    }

    [Fact]
    public async Task DetectAsync_ReportsProgressForEachAnalyzedFramePair()
    {
        var frames = BuildFrames((10, 10, 10, 4));
        var sampler = FakeFrameSampler.ReturningFrames(() => frames);
        var detector = new TransitionDetector(sampler, FakeFfprobeService.ReturningMediaInfo(CreateMediaInfo(TimeSpan.FromSeconds(2))));

        var reports = new List<TransitionDetectionProgress>();
        var progress = new Progress<TransitionDetectionProgress>(reports.Add);

        await detector.DetectAsync("input.mp4", TransitionDetectionOptions.ForProfile(AnalysisProfile.Fast), progress, CancellationToken.None);

        // Progress reporting happens synchronously on this fake pipeline (no
        // real async handoff), so all reports are visible immediately.
        Assert.Equal(3, reports.Count);
        Assert.Equal(1, reports[0].FramesAnalyzed);
        Assert.Equal(3, reports[^1].FramesAnalyzed);
    }

    [Fact]
    public async Task DetectAsync_GivenMediaInfo_NeverProbesInternally()
    {
        var frames = BuildFrames((10, 10, 10, 4));
        var sampler = FakeFrameSampler.ReturningFrames(() => frames);
        var ffprobe = FakeFfprobeService.ReturningMediaInfo(CreateMediaInfo(TimeSpan.FromSeconds(2)));
        var detector = new TransitionDetector(sampler, ffprobe);

        var results = await detector.DetectAsync(
            "input.mp4",
            CreateMediaInfo(TimeSpan.FromSeconds(2)),
            TransitionDetectionOptions.ForProfile(AnalysisProfile.Fast),
            null,
            CancellationToken.None);

        Assert.Empty(results);
        Assert.Equal(0, ffprobe.ProbeCallCount);
    }

    [Fact]
    public async Task DetectAsync_GivenMediaInfoWithNoVideoStream_ThrowsWithoutInvokingFrameSampler()
    {
        var mediaInfo = new MediaInfo
        {
            FilePath = "audio.mp4",
            FormatName = "wav",
            Duration = TimeSpan.FromSeconds(1),
            VideoStreams = [],
            AudioStreams = [],
        };
        var sampler = FakeFrameSampler.ReturningFrames(() => throw new InvalidOperationException("Frame sampler must not be invoked when there is no video stream."));
        var ffprobe = FakeFfprobeService.ReturningMediaInfo(mediaInfo);
        var detector = new TransitionDetector(sampler, ffprobe);

        await Assert.ThrowsAsync<TransitionDetectionException>(() => detector.DetectAsync(
            "audio.mp4",
            mediaInfo,
            TransitionDetectionOptions.ForProfile(AnalysisProfile.Fast),
            null,
            CancellationToken.None));

        Assert.Equal(0, ffprobe.ProbeCallCount);
    }

    private static List<FrameSample> BuildFrames(params (byte B, byte G, byte R, int Count)[] segments)
    {
        var frames = new List<FrameSample>();
        var index = 0;
        foreach (var (b, g, r, count) in segments)
        {
            for (var i = 0; i < count; i++)
            {
                frames.Add(FrameSampleBuilder.SolidColor(b, g, r, frameIndex: index, timestamp: TimeSpan.FromSeconds(index * 0.25)));
                index++;
            }
        }

        return frames;
    }

    private static MediaInfo CreateMediaInfo(TimeSpan duration) => new()
    {
        FilePath = "input.mp4",
        FormatName = "mov,mp4,m4a,3gp,3g2,mj2",
        Duration = duration,
        VideoStreams =
        [
            new VideoStreamInfo
            {
                Index = 0,
                CodecName = "h264",
                Width = 640,
                Height = 360,
                AverageFrameRate = new RationalFrameRate(30, 1),
                RealBaseFrameRate = new RationalFrameRate(30, 1),
                IsVariableFrameRate = false,
                RotationDegrees = 0,
            },
        ],
        AudioStreams = [],
    };
}
