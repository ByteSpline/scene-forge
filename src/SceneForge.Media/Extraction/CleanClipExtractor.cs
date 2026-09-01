using System.Diagnostics;
using System.Runtime.CompilerServices;
using SceneForge.Media.Domain;
using SceneForge.Media.Extraction.Clustering;
using SceneForge.Media.Extraction.Intervals;
using SceneForge.Media.Extraction.Signals;
using SceneForge.Media.Extraction.Streaming;
using SceneForge.Media.Probing;
using SceneForge.Media.Sampling;

namespace SceneForge.Media.Extraction;

// Orchestrates the whole clean-clip pipeline:
// IntervalSubtractor -> ClipCandidateGenerator -> IFrameSampler ->
// ClipFrameMetricsPipeline -> CleanClipScoringSweep -> VisualClusterer.
// Never attempts to reconstruct frames hidden by an effect: every candidate
// is generated only from IntervalSubtractor's output, so an excluded
// interval (transition/black-frozen-invalid/user-excluded) can never
// contribute frames to a clip by construction, not by a runtime check.
// Probes the file once up front (same fail-fast/single-probe pattern
// TransitionDetector established for Phase 6) and skips frame sampling
// entirely when there are no candidates to score at all, so an
// exclusion-saturated scene list never launches ffmpeg for nothing.
public sealed class CleanClipExtractor : ICleanClipExtractor
{
    private readonly IFrameSampler _frameSampler;
    private readonly IFfprobeService _ffprobeService;

    public CleanClipExtractor(IFrameSampler frameSampler, IFfprobeService ffprobeService)
    {
        ArgumentNullException.ThrowIfNull(frameSampler);
        ArgumentNullException.ThrowIfNull(ffprobeService);

        _frameSampler = frameSampler;
        _ffprobeService = ffprobeService;
    }

    public async Task<CleanClipExtractionResult> ExtractAsync(
        string filePath,
        CleanClipExtractionOptions options,
        IProgress<CleanClipExtractionProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var mediaInfo = await _ffprobeService.ProbeAsync(filePath, cancellationToken).ConfigureAwait(false);
        return await ExtractCoreAsync(filePath, mediaInfo, options, progress, cancellationToken).ConfigureAwait(false);
    }

    public Task<CleanClipExtractionResult> ExtractAsync(
        string filePath,
        MediaInfo mediaInfo,
        CleanClipExtractionOptions options,
        IProgress<CleanClipExtractionProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mediaInfo);
        ArgumentNullException.ThrowIfNull(options);

        return ExtractCoreAsync(filePath, mediaInfo, options, progress, cancellationToken);
    }

    private async Task<CleanClipExtractionResult> ExtractCoreAsync(
        string filePath,
        MediaInfo mediaInfo,
        CleanClipExtractionOptions options,
        IProgress<CleanClipExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (mediaInfo.PrimaryVideoStream is null)
        {
            throw new CleanClipExtractionException($"'{filePath}' has no video stream to extract clean clips from.");
        }

        var exclusionRanges = options.ExcludedIntervals.Select(e => e.Range).ToList();
        var remaining = IntervalSubtractor.Subtract(options.SceneRanges, exclusionRanges);
        var candidates = ClipCandidateGenerator.Generate(remaining, options.Scoring);

        if (candidates.Count == 0)
        {
            return new CleanClipExtractionResult
            {
                RemainingCleanRanges = remaining.Select(r => r.Range).ToList(),
                AcceptedClips = [],
                RejectedClips = [],
                Clusters = [],
            };
        }

        var samplingOptions = options.SamplingOptions with { PixelFormat = FrameSamplePixelFormat.Bgr24 };
        var frames = _frameSampler.SampleAsync(filePath, mediaInfo, samplingOptions, progress: null, cancellationToken);
        var metrics = ClipFrameMetricsPipeline.ComputeAsync(frames, cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        var framesAnalyzed = 0;
        var candidatesScored = 0;
        var accepted = 0;
        var lastTimestamp = TimeSpan.Zero;

        void OnFrame(ClipFrameMetrics frame)
        {
            framesAnalyzed++;
            lastTimestamp = frame.Timestamp;
            progress?.Report(new CleanClipExtractionProgress
            {
                FramesAnalyzed = framesAnalyzed,
                CandidatesScoredSoFar = candidatesScored,
                ClipsAcceptedSoFar = accepted,
                LastSourceTimestamp = lastTimestamp,
                Elapsed = stopwatch.Elapsed,
            });
        }

        var clips = new List<CleanClip>();
        var trackedMetrics = WithProgress(metrics, OnFrame, cancellationToken);
        await foreach (var clip in CleanClipScoringSweep.RunAsync(trackedMetrics, candidates, exclusionRanges, options.Scoring, cancellationToken).ConfigureAwait(false))
        {
            clips.Add(clip);
            candidatesScored++;
            if (clip.Score.Accepted)
            {
                accepted++;
            }
        }

        var acceptedClips = clips.Where(c => c.Score.Accepted).OrderBy(c => c.Range.Start).ToList();
        var rejectedClips = clips.Where(c => !c.Score.Accepted).OrderBy(c => c.Range.Start).ToList();

        var clusters = VisualClusterer.Cluster(acceptedClips.Select(c => c.Descriptor).ToList(), options.Clustering);
        for (var clusterId = 0; clusterId < clusters.Count; clusterId++)
        {
            foreach (var memberIndex in clusters[clusterId].MemberClipIndices)
            {
                acceptedClips[memberIndex] = acceptedClips[memberIndex] with { ClusterId = clusterId };
            }
        }

        return new CleanClipExtractionResult
        {
            RemainingCleanRanges = remaining.Select(r => r.Range).ToList(),
            AcceptedClips = acceptedClips,
            RejectedClips = rejectedClips,
            Clusters = clusters,
        };
    }

    private static async IAsyncEnumerable<ClipFrameMetrics> WithProgress(
        IAsyncEnumerable<ClipFrameMetrics> source,
        Action<ClipFrameMetrics> onFrame,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // ConfigureAwait(false) required, not optional - see
        // Detection.Signals.SignalPipeline.ComputeAsync's own remarks and
        // docs/UI_RESPONSIVENESS_AUDIT.md: without it, a UI-thread caller
        // has this continuation (and everything upstream feeding it, since
        // this is the outermost wrapper around the whole metrics stream)
        // marshaled back onto the UI dispatcher instead of a thread-pool
        // thread.
        await foreach (var frame in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            onFrame(frame);
            yield return frame;
        }
    }
}
