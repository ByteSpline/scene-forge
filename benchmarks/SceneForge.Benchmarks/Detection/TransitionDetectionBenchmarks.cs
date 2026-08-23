using BenchmarkDotNet.Attributes;
using SceneForge.Media.Detection.Classification;
using SceneForge.Media.Detection.Fusion;
using SceneForge.Media.Detection.Signals;
using SceneForge.Media.Sampling;

namespace SceneForge.Benchmarks.Detection;

// Measures the signal-extraction (SignalPipeline/AnalyzedFrame/OpenCvSharp)
// and classification (all 7 ITransitionClassifiers over a bounded
// ClassifierWindow) pipeline's own cost per profile, using a synthetic
// in-memory frame source (CLAUDE.md rule 9: benchmark with evidence). No
// prior version of this code exists to diff against - this is the baseline
// measurement, recorded in docs/PHASE_06_REPORT.md, same handling as
// FrameSamplingBenchmarks' own baseline in the Phase 5 report.
[MemoryDiagnoser]
public class TransitionDetectionBenchmarks
{
    private const int TotalFrames = 200;

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

    private static readonly TransitionDetectionProfile Profile =
        TransitionDetectionProfiles.GetDefaults(TransitionDetectionProfileVersion.V1);

    [ParamsAllValues]
    public AnalysisProfile AnalysisProfile { get; set; }

    [Benchmark]
    public async Task<int> AnalyzeAndClassify()
    {
        var samplingOptions = FrameSamplingProfiles.GetDefaults(AnalysisProfile);
        var frames = BenchmarkFrameGenerator.Generate(samplingOptions.AnalysisWidthPixels, samplingOptions.AnalysisWidthPixels * 9 / 16, TotalFrames);

        var pipeline = new SignalPipeline();
        var window = new ClassifierWindow(Profile.MaxTransitionDuration);

        var candidateCount = 0;
        await foreach (var signal in pipeline.ComputeAsync(ToAsyncEnumerable(frames), CancellationToken.None))
        {
            window.Append(signal);
            foreach (var classifier in Classifiers)
            {
                candidateCount += classifier.Classify(window.Samples, Profile).Count;
            }
        }

        return candidateCount;
    }

    private static async IAsyncEnumerable<FrameSample> ToAsyncEnumerable(IEnumerable<FrameSample> frames)
    {
        foreach (var frame in frames)
        {
            yield return frame;
        }

        await Task.CompletedTask;
    }
}
