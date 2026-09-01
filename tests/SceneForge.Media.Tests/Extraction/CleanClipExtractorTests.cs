using SceneForge.Media.Domain;
using SceneForge.Media.Extraction;
using SceneForge.Media.Sampling;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Extraction;

public class CleanClipExtractorTests
{
    // Thresholds relaxed to their most permissive legal values so acceptance
    // never depends on the fine numeric behavior of real decoded pixel
    // content (that behavior is covered exhaustively by ClipScorerTests) -
    // these tests instead verify orchestration: subtraction, candidate
    // generation, streaming, clustering, progress, and cancellation wiring.
    private static readonly CleanClipScoringOptions LenientScoring = new()
    {
        MinAcceptableFactorScore = 0.0,
        FreezeRiskRejectionThreshold = 1.0,
        OverlaySuspicionRejectionThreshold = 1.0,
        AcceptanceThreshold = 0.0,
    };

    [Fact]
    public async Task ExtractAsync_StableContentWithLenientOptions_ProducesAcceptedClips()
    {
        var frames = BuildFrames(0.0, 10.0, 0.25, (128, 128, 128));
        var sampler = FakeFrameSampler.ReturningFrames(() => frames);
        var ffprobe = FakeFfprobeService.ReturningMediaInfo(CreateMediaInfo(TimeSpan.FromSeconds(10)));
        var extractor = new CleanClipExtractor(sampler, ffprobe);

        var options = new CleanClipExtractionOptions
        {
            SamplingOptions = FrameSamplingOptions.ForProfile(AnalysisProfile.Fast),
            SceneRanges = [new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10))],
            Scoring = LenientScoring,
        };

        var result = await extractor.ExtractAsync("input.mp4", options, null, CancellationToken.None);

        Assert.NotEmpty(result.AcceptedClips);
        Assert.Empty(result.RejectedClips);
        Assert.All(result.AcceptedClips, c => Assert.True(c.Score.Accepted));
        Assert.All(result.AcceptedClips, c => Assert.NotNull(c.ClusterId));
    }

    [Fact]
    public async Task ExtractAsync_AcceptedClipsWithIdenticalContent_AllJoinOneCluster()
    {
        // 20s scene with non-overlapping 5s candidates (default MaxClipDuration)
        // fits several back-to-back candidates, so this actually exercises
        // clustering multiple accepted clips rather than just one.
        var frames = BuildFrames(0.0, 20.0, 0.25, (128, 128, 128));
        var sampler = FakeFrameSampler.ReturningFrames(() => frames);
        var ffprobe = FakeFfprobeService.ReturningMediaInfo(CreateMediaInfo(TimeSpan.FromSeconds(20)));
        var extractor = new CleanClipExtractor(sampler, ffprobe);

        var options = new CleanClipExtractionOptions
        {
            SamplingOptions = FrameSamplingOptions.ForProfile(AnalysisProfile.Fast),
            SceneRanges = [new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(20))],
            Scoring = LenientScoring with { OverlapFraction = 0.0 },
        };

        var result = await extractor.ExtractAsync("input.mp4", options, null, CancellationToken.None);

        Assert.True(result.AcceptedClips.Count > 1);
        Assert.Single(result.Clusters);
        Assert.All(result.AcceptedClips, c => Assert.Equal(0, c.ClusterId));
    }

    [Fact]
    public async Task ExtractAsync_FlatLowDetailContentWithDefaultOptions_RejectsForInsufficientSharpness()
    {
        // Solid-color frames have zero Laplacian variance (see
        // AnalyzedFrameTests), so under the default (non-relaxed)
        // MinAcceptableFactorScore this fails deterministically, regardless
        // of every other factor.
        var frames = BuildFrames(0.0, 10.0, 0.25, (128, 128, 128));
        var sampler = FakeFrameSampler.ReturningFrames(() => frames);
        var ffprobe = FakeFfprobeService.ReturningMediaInfo(CreateMediaInfo(TimeSpan.FromSeconds(10)));
        var extractor = new CleanClipExtractor(sampler, ffprobe);

        var options = new CleanClipExtractionOptions
        {
            SamplingOptions = FrameSamplingOptions.ForProfile(AnalysisProfile.Fast),
            SceneRanges = [new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10))],
        };

        var result = await extractor.ExtractAsync("input.mp4", options, null, CancellationToken.None);

        Assert.NotEmpty(result.RejectedClips);
        Assert.Empty(result.AcceptedClips);
        Assert.Empty(result.Clusters);
        Assert.All(result.RejectedClips, c =>
        {
            var reason = c.Score.Reasons.Single(r => r.Factor == "Sharpness");
            Assert.False(reason.Passed);
            Assert.Equal(RejectionReason.InsufficientSharpness, reason.Code);
        });
    }

    [Fact]
    public async Task ExtractAsync_ExcludedIntervalInMiddleOfScene_NoClipEverOverlapsIt()
    {
        var frames = BuildFrames(0.0, 10.0, 0.25, (128, 128, 128));
        var sampler = FakeFrameSampler.ReturningFrames(() => frames);
        var ffprobe = FakeFfprobeService.ReturningMediaInfo(CreateMediaInfo(TimeSpan.FromSeconds(10)));
        var extractor = new CleanClipExtractor(sampler, ffprobe);

        var exclusion = new TimeRange(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6));
        var options = new CleanClipExtractionOptions
        {
            SamplingOptions = FrameSamplingOptions.ForProfile(AnalysisProfile.Fast),
            SceneRanges = [new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10))],
            ExcludedIntervals = [new ExcludedInterval { Range = exclusion, Kind = ExclusionKind.Transition }],
            Scoring = LenientScoring,
        };

        var result = await extractor.ExtractAsync("input.mp4", options, null, CancellationToken.None);

        Assert.All(result.RemainingCleanRanges, r => Assert.False(r.Overlaps(exclusion)));
        Assert.All(result.AcceptedClips.Concat(result.RejectedClips), c => Assert.False(c.Range.Overlaps(exclusion)));
        Assert.NotEmpty(result.AcceptedClips);
    }

    [Fact]
    public async Task ExtractAsync_SceneRangeTooShortForAnyCandidate_SkipsFrameSamplingEntirely()
    {
        var sampler = FakeFrameSampler.ReturningFrames(() => throw new InvalidOperationException("Frame sampler must not be invoked when there are no candidates to score."));
        var ffprobe = FakeFfprobeService.ReturningMediaInfo(CreateMediaInfo(TimeSpan.FromSeconds(1)));
        var extractor = new CleanClipExtractor(sampler, ffprobe);

        var options = new CleanClipExtractionOptions
        {
            SamplingOptions = FrameSamplingOptions.ForProfile(AnalysisProfile.Fast),
            SceneRanges = [new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1))],
        };

        var result = await extractor.ExtractAsync("input.mp4", options, null, CancellationToken.None);

        Assert.Empty(result.AcceptedClips);
        Assert.Empty(result.RejectedClips);
    }

    [Fact]
    public async Task ExtractAsync_NoVideoStream_ThrowsWithoutInvokingFrameSampler()
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
        var extractor = new CleanClipExtractor(sampler, FakeFfprobeService.ReturningMediaInfo(mediaInfo));

        var options = new CleanClipExtractionOptions
        {
            SamplingOptions = FrameSamplingOptions.ForProfile(AnalysisProfile.Fast),
            SceneRanges = [new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1))],
        };

        await Assert.ThrowsAsync<CleanClipExtractionException>(() => extractor.ExtractAsync("audio.mp4", options, null, CancellationToken.None));
    }

    [Fact]
    public async Task ExtractAsync_Cancelled_ThrowsOperationCanceled()
    {
        var frames = BuildFrames(0.0, 10.0, 0.25, (128, 128, 128));
        var sampler = FakeFrameSampler.ReturningFrames(() => frames);
        var extractor = new CleanClipExtractor(sampler, FakeFfprobeService.ReturningMediaInfo(CreateMediaInfo(TimeSpan.FromSeconds(10))));

        var options = new CleanClipExtractionOptions
        {
            SamplingOptions = FrameSamplingOptions.ForProfile(AnalysisProfile.Fast),
            SceneRanges = [new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10))],
            Scoring = LenientScoring,
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => extractor.ExtractAsync("input.mp4", options, null, cts.Token));
    }

    [Fact]
    public async Task ExtractAsync_ReportsProgressCoveringEveryAnalyzedFrame()
    {
        var frames = BuildFrames(0.0, 10.0, 0.25, (128, 128, 128));
        var sampler = FakeFrameSampler.ReturningFrames(() => frames);
        var extractor = new CleanClipExtractor(sampler, FakeFfprobeService.ReturningMediaInfo(CreateMediaInfo(TimeSpan.FromSeconds(10))));

        var options = new CleanClipExtractionOptions
        {
            SamplingOptions = FrameSamplingOptions.ForProfile(AnalysisProfile.Fast),
            SceneRanges = [new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10))],
            Scoring = LenientScoring,
        };
        var reports = new List<CleanClipExtractionProgress>();
        var progress = new Progress<CleanClipExtractionProgress>(reports.Add);

        await extractor.ExtractAsync("input.mp4", options, progress, CancellationToken.None);

        Assert.NotEmpty(reports);
        Assert.Equal(frames.Count, reports[^1].FramesAnalyzed);
        Assert.Equal(1, reports[0].FramesAnalyzed);
    }

    [Fact]
    public async Task ExtractAsync_GivenMediaInfo_NeverProbesInternally()
    {
        var frames = BuildFrames(0.0, 10.0, 0.25, (128, 128, 128));
        var sampler = FakeFrameSampler.ReturningFrames(() => frames);
        var ffprobe = FakeFfprobeService.ReturningMediaInfo(CreateMediaInfo(TimeSpan.FromSeconds(10)));
        var extractor = new CleanClipExtractor(sampler, ffprobe);

        var options = new CleanClipExtractionOptions
        {
            SamplingOptions = FrameSamplingOptions.ForProfile(AnalysisProfile.Fast),
            SceneRanges = [new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10))],
            Scoring = LenientScoring,
        };

        var result = await extractor.ExtractAsync("input.mp4", CreateMediaInfo(TimeSpan.FromSeconds(10)), options, null, CancellationToken.None);

        Assert.NotEmpty(result.AcceptedClips);
        Assert.Equal(0, ffprobe.ProbeCallCount);
    }

    [Fact]
    public async Task ExtractAsync_GivenMediaInfoWithNoVideoStream_ThrowsWithoutInvokingFrameSampler()
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
        var extractor = new CleanClipExtractor(sampler, ffprobe);

        var options = new CleanClipExtractionOptions
        {
            SamplingOptions = FrameSamplingOptions.ForProfile(AnalysisProfile.Fast),
            SceneRanges = [new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1))],
        };

        await Assert.ThrowsAsync<CleanClipExtractionException>(() => extractor.ExtractAsync("audio.mp4", mediaInfo, options, null, CancellationToken.None));

        Assert.Equal(0, ffprobe.ProbeCallCount);
    }

    // Regression coverage for a real, shipped UI-freeze bug (see
    // docs/UI_RESPONSIVENESS_AUDIT.md) - exercises the whole ExtractAsync
    // pipeline (ClipFrameMetricsPipeline, CleanClipScoringSweep, and
    // WithProgress, all previously missing ConfigureAwait(false) on their
    // internal `await foreach`) end to end from a context-capturing thread,
    // the same way AnalysisProgressViewModel invokes it in production.
    // Feeds frames through a sampler that genuinely suspends between each
    // one (via its own ConfigureAwait(false), so the suspension mechanism
    // itself never touches the spy) and asserts not one continuation -
    // across the whole call - was ever posted back to it.
    [Fact]
    public async Task ExtractAsync_ConsumedFromAContextCapturingThread_NeverPostsPerFrameWorkBackToThatContext()
    {
        var spy = new SynchronizationContextSpy();
        var original = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(spy);
        try
        {
            var frames = BuildFrames(0.0, 10.0, 0.25, (128, 128, 128));
            var sampler = new FakeFrameSampler((_, _, cancellationToken) => GenuinelyYieldingAsyncEnumerable(frames, cancellationToken));
            var ffprobe = FakeFfprobeService.ReturningMediaInfo(CreateMediaInfo(TimeSpan.FromSeconds(10)));
            var extractor = new CleanClipExtractor(sampler, ffprobe);

            var options = new CleanClipExtractionOptions
            {
                SamplingOptions = FrameSamplingOptions.ForProfile(AnalysisProfile.Fast),
                SceneRanges = [new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10))],
                Scoring = LenientScoring,
            };

            // ConfigureAwait(false) here is deliberate and required for the
            // test itself to be valid, not a style violation (xUnit1030) -
            // without it, THIS await's own resumption (running the
            // assertions below) would be posted through the spy, which
            // would make the spy.PostCount assertion below fail regardless
            // of whether the pipeline under test is fixed.
#pragma warning disable xUnit1030
            var result = await extractor.ExtractAsync("input.mp4", options, progress: null, CancellationToken.None).ConfigureAwait(false);
#pragma warning restore xUnit1030

            Assert.NotEmpty(result.AcceptedClips);
            // No IProgress<T> supplied, so every Post the spy might see here
            // can only come from the extraction pipeline's own internal
            // continuations - not from Progress<T>'s own (separate,
            // legitimate) UI-thread marshaling, which a real caller like
            // AnalysisProgressViewModel does rely on and this test
            // deliberately does not exercise.
            Assert.Equal(0, spy.PostCount);
            Assert.Equal(0, spy.SendCount);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(original);
        }
    }

    private static async IAsyncEnumerable<FrameSample> GenuinelyYieldingAsyncEnumerable(
        IEnumerable<FrameSample> frames,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var frame in frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            yield return frame;
        }
    }

    private static List<FrameSample> BuildFrames(double startSeconds, double endSeconds, double stepSeconds, (byte B, byte G, byte R) color)
    {
        var frames = new List<FrameSample>();
        var index = 0;
        for (var t = startSeconds; t <= endSeconds + 1e-9; t += stepSeconds)
        {
            frames.Add(FrameSampleBuilder.SolidColor(color.B, color.G, color.R, frameIndex: index, timestamp: TimeSpan.FromSeconds(t)));
            index++;
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
