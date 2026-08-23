using System.Diagnostics;
using SceneForge.Media.Detection.Classification;
using SceneForge.Media.Detection.Fusion;
using SceneForge.Media.Detection.Signals;
using SceneForge.Media.Probing;
using SceneForge.Media.Sampling;

namespace SceneForge.Media.Detection;

// Orchestrates the whole transition-analysis pipeline:
// IFrameSampler -> SignalPipeline -> ClassifierWindow + all 7
// ITransitionClassifiers -> TransitionFuser. Probes the file once up front
// (fail-fast on no video stream, same pattern as FrameSampler itself) both
// to pass the source duration through to TransitionFuser for End clamping
// and to hand the already-resolved MediaInfo to IFrameSampler's
// MediaInfo-accepting SampleAsync overload, so the file is probed exactly
// once per DetectAsync call rather than once here and again inside
// FrameSampler.
public sealed class TransitionDetector : ITransitionDetector
{
    private static readonly IReadOnlyList<ITransitionClassifier> Classifiers =
    [
        new HardCutClassifier(),
        new FadeBlackClassifier(),
        new DissolveClassifier(),
        new FlashClassifier(),
        new BlurTransitionClassifier(),
        new ZoomTransitionClassifier(),
        new DirectionalSwipeClassifier(),
    ];

    private readonly IFrameSampler _frameSampler;
    private readonly IFfprobeService _ffprobeService;

    public TransitionDetector(IFrameSampler frameSampler, IFfprobeService ffprobeService)
    {
        ArgumentNullException.ThrowIfNull(frameSampler);
        ArgumentNullException.ThrowIfNull(ffprobeService);

        _frameSampler = frameSampler;
        _ffprobeService = ffprobeService;
    }

    public async Task<IReadOnlyList<TransitionDetection>> DetectAsync(
        string filePath,
        TransitionDetectionOptions options,
        IProgress<TransitionDetectionProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var mediaInfo = await _ffprobeService.ProbeAsync(filePath, cancellationToken).ConfigureAwait(false);
        if (mediaInfo.PrimaryVideoStream is null)
        {
            throw new TransitionDetectionException($"'{filePath}' has no video stream to analyze for transitions.");
        }

        // AnalyzedFrame requires color; force Bgr24 regardless of what the
        // caller's sampling options otherwise specify.
        var samplingOptions = options.SamplingOptions with { PixelFormat = FrameSamplePixelFormat.Bgr24 };

        var signalPipeline = new SignalPipeline();
        var window = new ClassifierWindow(options.Profile.MaxTransitionDuration);
        var candidates = new List<TransitionCandidate>();
        var stopwatch = Stopwatch.StartNew();
        var framesAnalyzed = 0;

        var frames = _frameSampler.SampleAsync(filePath, mediaInfo, samplingOptions, progress: null, cancellationToken);
        await foreach (var signal in signalPipeline.ComputeAsync(frames, cancellationToken).ConfigureAwait(false))
        {
            framesAnalyzed++;
            window.Append(signal);

            foreach (var classifier in Classifiers)
            {
                candidates.AddRange(classifier.Classify(window.Samples, options.Profile));
            }

            progress?.Report(new TransitionDetectionProgress
            {
                FramesAnalyzed = framesAnalyzed,
                RawCandidatesSoFar = candidates.Count,
                LastSourceTimestamp = signal.Timestamp,
                Elapsed = stopwatch.Elapsed,
            });
        }

        return TransitionFuser.Fuse(candidates, options.Profile, mediaInfo.Duration);
    }
}
