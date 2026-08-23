using System.Runtime.CompilerServices;
using SceneForge.Media.Domain;
using SceneForge.Media.Extraction.Intervals;
using SceneForge.Media.Extraction.Scoring;
using SceneForge.Media.Extraction.Signals;

namespace SceneForge.Media.Extraction.Streaming;

// Assigns a single streamed pass of ClipFrameMetrics to every candidate
// whose [Start, End] it falls within, finalizing (scoring) each candidate
// as soon as the stream moves past its End, rather than ever retaining the
// whole video's metrics at once. Because candidates are generated in Start
// order and only overlap through a small, fixed sliding-window stride (see
// ClipCandidateGenerator), the number of concurrently "open" accumulators
// is bounded by a small constant independent of video length - this is
// what keeps this whole pass compliant with CLAUDE.md rule 6/7 even though
// scoring needs per-candidate frame data spanning the entire timeline, not
// just a fixed lookback window. Pure aside from the streaming shape itself
// - no OpenCvSharp anywhere here - so it is independently testable against
// a synthetic IAsyncEnumerable<ClipFrameMetrics> and hand-built candidates.
internal static class CleanClipScoringSweep
{
    public static async IAsyncEnumerable<CleanClip> RunAsync(
        IAsyncEnumerable<ClipFrameMetrics> metricsStream,
        IReadOnlyList<IndexedTimeRange> candidatesSortedByStart,
        IReadOnlyList<TimeRange> exclusions,
        CleanClipScoringOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidatesSortedByStart);
        ArgumentNullException.ThrowIfNull(exclusions);
        ArgumentNullException.ThrowIfNull(options);

        var openAccumulators = new List<CandidateAccumulator>();
        var nextCandidateIndex = 0;

        await foreach (var metrics in metricsStream.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            while (nextCandidateIndex < candidatesSortedByStart.Count
                   && candidatesSortedByStart[nextCandidateIndex].Range.Start <= metrics.Timestamp)
            {
                openAccumulators.Add(new CandidateAccumulator(candidatesSortedByStart[nextCandidateIndex]));
                nextCandidateIndex++;
            }

            foreach (var accumulator in openAccumulators)
            {
                if (accumulator.Candidate.Range.Contains(metrics.Timestamp))
                {
                    accumulator.Add(metrics);
                }
            }

            for (var i = openAccumulators.Count - 1; i >= 0; i--)
            {
                if (openAccumulators[i].Candidate.Range.End < metrics.Timestamp)
                {
                    yield return Finalize(openAccumulators[i], exclusions, options);
                    openAccumulators.RemoveAt(i);
                }
            }
        }

        // Any candidate whose Start was never reached by a streamed
        // timestamp (source shorter than expected) still needs a verdict -
        // it opens here with zero frames, which ClipScorer scores as
        // worst-case rather than throwing.
        while (nextCandidateIndex < candidatesSortedByStart.Count)
        {
            openAccumulators.Add(new CandidateAccumulator(candidatesSortedByStart[nextCandidateIndex]));
            nextCandidateIndex++;
        }

        foreach (var accumulator in openAccumulators)
        {
            yield return Finalize(accumulator, exclusions, options);
        }
    }

    private static CleanClip Finalize(CandidateAccumulator accumulator, IReadOnlyList<TimeRange> exclusions, CleanClipScoringOptions options)
    {
        var distance = ExclusionDistanceCalculator.NearestDistance(accumulator.Candidate.Range, exclusions);
        var score = ClipScorer.Score(accumulator.Candidate.Range, accumulator.Frames, distance, options);
        var motionClass = MotionClassifier.Classify(MotionClassifier.MeanStructuralDifference(accumulator.Frames), options);

        var descriptor = accumulator.Representative is { } representative
            ? new PerceptualDescriptor
            {
                PerceptualHash = representative.PerceptualHash,
                ColorHistogram = representative.ColorHistogram,
                EdgeHistogram = representative.EdgeHistogram,
                Motion = motionClass,
            }
            : new PerceptualDescriptor
            {
                PerceptualHash = 0,
                ColorHistogram = [],
                EdgeHistogram = [],
                Motion = motionClass,
            };

        return new CleanClip
        {
            Range = accumulator.Candidate.Range,
            SourceSceneIndex = accumulator.Candidate.SourceSceneIndex,
            Score = score,
            Descriptor = descriptor,
        };
    }

    // Accumulates one candidate's overlapping frames plus tracks the
    // sharpest one seen so far as the clip's representative frame for
    // perceptual descriptor purposes - deliberately the sharpest, not the
    // temporally-centered one, since a mid-transition or motion-blurred
    // frame would otherwise make a poor visual fingerprint for the whole
    // clip.
    private sealed class CandidateAccumulator
    {
        private readonly List<ClipFrameMetrics> _frames = [];

        public CandidateAccumulator(IndexedTimeRange candidate)
        {
            Candidate = candidate;
        }

        public IndexedTimeRange Candidate { get; }

        public IReadOnlyList<ClipFrameMetrics> Frames => _frames;

        public ClipFrameMetrics? Representative { get; private set; }

        public void Add(ClipFrameMetrics metrics)
        {
            _frames.Add(metrics);
            if (Representative is null || metrics.Sharpness > Representative.Sharpness)
            {
                Representative = metrics;
            }
        }
    }
}
