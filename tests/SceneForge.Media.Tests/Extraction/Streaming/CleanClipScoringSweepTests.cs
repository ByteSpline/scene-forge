using System.Runtime.CompilerServices;
using SceneForge.Media.Domain;
using SceneForge.Media.Extraction;
using SceneForge.Media.Extraction.Intervals;
using SceneForge.Media.Extraction.Signals;
using SceneForge.Media.Extraction.Streaming;
using SceneForge.Media.Tests.TestSupport;

namespace SceneForge.Media.Tests.Extraction.Streaming;

public class CleanClipScoringSweepTests
{
    private static readonly CleanClipScoringOptions Options = CleanClipScoringOptions.Default;

    private static IndexedTimeRange Candidate(int sourceIndex, double startSeconds, double endSeconds) =>
        new(sourceIndex, new TimeRange(TimeSpan.FromSeconds(startSeconds), TimeSpan.FromSeconds(endSeconds)));

    private static async IAsyncEnumerable<ClipFrameMetrics> ToAsync(
        IEnumerable<ClipFrameMetrics> frames,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var frame in frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return frame;
        }

        await Task.CompletedTask;
    }

    private static async Task<List<CleanClip>> RunAsync(
        IEnumerable<ClipFrameMetrics> frames,
        IReadOnlyList<IndexedTimeRange> candidates,
        IReadOnlyList<TimeRange>? exclusions = null,
        CancellationToken cancellationToken = default)
    {
        var result = new List<CleanClip>();
        await foreach (var clip in CleanClipScoringSweep.RunAsync(ToAsync(frames, cancellationToken), candidates, exclusions ?? [], Options, cancellationToken))
        {
            result.Add(clip);
        }

        return result;
    }

    [Fact]
    public async Task RunAsync_SingleCandidate_AssignsAllFramesWithinItsRange()
    {
        var frames = Enumerable.Range(0, 10).Select(i => ClipFrameMetricsBuilder.Sample(i * 0.5)).ToList();
        var candidates = new[] { Candidate(0, 1, 3) };

        var clips = await RunAsync(frames, candidates);

        var clip = Assert.Single(clips);
        Assert.Equal(candidates[0].Range, clip.Range);
        Assert.Equal(0, clip.SourceSceneIndex);
    }

    [Fact]
    public async Task RunAsync_NonOverlappingCandidates_ProducesOneCleanClipPerCandidate()
    {
        var frames = Enumerable.Range(0, 20).Select(i => ClipFrameMetricsBuilder.Sample(i * 0.5)).ToList();
        var candidates = new[] { Candidate(0, 0, 3), Candidate(0, 5, 8) };

        var clips = await RunAsync(frames, candidates);

        Assert.Equal(2, clips.Count);
        Assert.Contains(clips, c => c.Range == candidates[0].Range);
        Assert.Contains(clips, c => c.Range == candidates[1].Range);
    }

    [Fact]
    public async Task RunAsync_OverlappingCandidates_ShareFramesInTheOverlapRegion()
    {
        var frames = new[]
        {
            ClipFrameMetricsBuilder.Sample(0, sharpness: 10),
            ClipFrameMetricsBuilder.Sample(1, sharpness: 20),
            ClipFrameMetricsBuilder.Sample(2, sharpness: 30),
            ClipFrameMetricsBuilder.Sample(3, sharpness: 40),
        };
        var candidates = new[] { Candidate(0, 0, 2), Candidate(0, 1, 3) };

        var clips = await RunAsync(frames, candidates);

        Assert.Equal(2, clips.Count);
        var first = clips.Single(c => c.Range == candidates[0].Range);
        var second = clips.Single(c => c.Range == candidates[1].Range);

        // The frames at t=1 and t=2 fall inside both [0,2] and [1,3] and
        // must influence both candidates' scoring, not be exclusively
        // claimed by one: [0,2] averages sharpness (10+20+30)/3=20 -> score
        // 0.4 against the default 50 reference; [1,3] averages
        // (20+30+40)/3=30 -> score 0.6.
        Assert.Equal(0.4, first.Score.Sharpness, precision: 10);
        Assert.Equal(0.6, second.Score.Sharpness, precision: 10);
    }

    [Fact]
    public async Task RunAsync_RepresentativeDescriptor_UsesSharpestFrameInWindow()
    {
        var frames = new[]
        {
            ClipFrameMetricsBuilder.Sample(0, sharpness: 5, perceptualHash: 0x1),
            ClipFrameMetricsBuilder.Sample(1, sharpness: 500, perceptualHash: 0x2),
            ClipFrameMetricsBuilder.Sample(2, sharpness: 10, perceptualHash: 0x3),
        };
        var candidates = new[] { Candidate(0, 0, 2) };

        var clips = await RunAsync(frames, candidates);

        var clip = Assert.Single(clips);
        Assert.Equal((ulong)0x2, clip.Descriptor.PerceptualHash);
    }

    [Fact]
    public async Task RunAsync_CandidateStartNeverReachedByStream_StillScoredWithZeroFrames()
    {
        var frames = new[] { ClipFrameMetricsBuilder.Sample(0) };
        var candidates = new[] { Candidate(0, 100, 104) };

        var clips = await RunAsync(frames, candidates);

        var clip = Assert.Single(clips);
        Assert.False(clip.Score.Accepted);
    }

    [Fact]
    public async Task RunAsync_NoCandidates_YieldsNothing()
    {
        var frames = new[] { ClipFrameMetricsBuilder.Sample(0) };

        var clips = await RunAsync(frames, []);

        Assert.Empty(clips);
    }

    [Fact]
    public async Task RunAsync_PassesExclusionsThroughToTransitionDistanceScoring()
    {
        var frames = Enumerable.Range(0, 6).Select(i => ClipFrameMetricsBuilder.Sample(i)).ToList();
        var candidates = new[] { Candidate(0, 0, 4) };
        var exclusions = new[] { new TimeRange(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4.1)) };

        var clips = await RunAsync(frames, candidates, exclusions);

        var clip = Assert.Single(clips);
        Assert.True(clip.Score.TransitionDistance < 1.0);
    }

    [Fact]
    public async Task RunAsync_ExternalCancellation_ThrowsOperationCanceled()
    {
        var frames = Enumerable.Range(0, 20).Select(i => ClipFrameMetricsBuilder.Sample(i * 0.5)).ToList();
        var candidates = new[] { Candidate(0, 0, 3) };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in CleanClipScoringSweep.RunAsync(ToAsync(frames), candidates, [], Options, cts.Token))
            {
            }
        });
    }

    // Regression coverage for a real, shipped UI-freeze bug (see
    // docs/UI_RESPONSIVENESS_AUDIT.md) - the streaming-scoring analogue of
    // SignalPipelineTests' own equivalent test (see its remarks for the
    // full mechanism): RunAsync's internal `await foreach` used to omit
    // ConfigureAwait(false), so a caller invoking it directly from a
    // context-capturing thread (a WPF UI thread in production, via
    // CleanClipExtractor) had every per-metrics-sample continuation
    // marshaled back onto that thread instead of running on a thread-pool
    // thread.
    [Fact]
    public async Task RunAsync_ConsumedFromAContextCapturingThread_NeverPostsPerSampleWorkBackToThatContext()
    {
        var spy = new SynchronizationContextSpy();
        var original = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(spy);
        try
        {
            var frames = Enumerable.Range(0, 6).Select(i => ClipFrameMetricsBuilder.Sample(i * 0.5)).ToList();
            var candidates = new[] { Candidate(0, 0, 3) };

            var clips = new List<CleanClip>();
            await foreach (var clip in CleanClipScoringSweep.RunAsync(GenuinelyYieldingAsync(frames), candidates, [], Options).ConfigureAwait(false))
            {
                clips.Add(clip);
            }

            Assert.Single(clips);
            Assert.Equal(0, spy.PostCount);
            Assert.Equal(0, spy.SendCount);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(original);
        }
    }

    // Unlike ToAsync above (synchronous yields, never exercises real
    // context-capture behavior), forces a genuine asynchronous suspension
    // before every sample via its own ConfigureAwait(false), so the
    // suspension mechanism itself never touches whatever
    // SynchronizationContext the test installed.
    private static async IAsyncEnumerable<ClipFrameMetrics> GenuinelyYieldingAsync(
        IEnumerable<ClipFrameMetrics> frames,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var frame in frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            yield return frame;
        }
    }
}
