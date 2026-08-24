using SceneForge.Media.Detection;
using SceneForge.Media.Domain;
using SceneForge.Media.Extraction;

namespace SceneForge.Media.Planning;

// Bridges Detection.ITransitionDetector's output to Extraction.ICleanClipExtractor's
// input: every TransitionDetection becomes an ExcludedInterval (clamped to
// [0, totalDuration], since a detection's buffered Start/End can slightly
// overshoot either edge - see TransitionDetectionProfile.PreBufferDuration/
// PostBufferDuration), and SceneRanges is the complement - the gaps between
// consecutive (clamped, sorted) exclusions, plus before the first and after
// the last. A gap collapsed to zero or negative length by clamping/overlap
// produces no scene range at all, never a degenerate TimeRange. Pure and
// synchronous, the same "facts in, no I/O" shape TimelinePlanner/
// RenderPlanBuilder already established (see docs/PHASE_08_REPORT.md,
// docs/PHASE_09_REPORT.md).
public static class SceneRangeCalculator
{
    public static SceneRangeCalculationResult Calculate(TimeSpan totalDuration, IReadOnlyList<TransitionDetection> detections)
    {
        if (totalDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(totalDuration), totalDuration, "Total duration must not be negative.");
        }

        ArgumentNullException.ThrowIfNull(detections);

        var clamped = new List<(ExcludedInterval Interval, TransitionDetection Detection)>(detections.Count);
        foreach (var detection in detections.OrderBy(d => d.Start))
        {
            var start = Clamp(detection.Start, TimeSpan.Zero, totalDuration);
            var end = Clamp(detection.End, TimeSpan.Zero, totalDuration);
            if (end <= start)
            {
                continue;
            }

            clamped.Add((
                new ExcludedInterval
                {
                    Range = new TimeRange(start, end),
                    Kind = ExclusionKind.Transition,
                    Reason = detection.Type.ToString(),
                },
                detection));
        }

        var sceneRanges = new List<TimeRange>();
        var boundaries = new List<SceneBoundaryTransitions>();

        var cursor = TimeSpan.Zero;
        TransitionDetection? leading = null;
        for (var i = 0; i <= clamped.Count; i++)
        {
            var isLast = i == clamped.Count;
            var segmentEnd = isLast ? totalDuration : clamped[i].Interval.Range.Start;
            var trailing = isLast ? null : clamped[i].Detection;

            if (segmentEnd > cursor)
            {
                sceneRanges.Add(new TimeRange(cursor, segmentEnd));
                boundaries.Add(new SceneBoundaryTransitions { Leading = leading, Trailing = trailing });
            }

            if (!isLast)
            {
                var exclusionEnd = clamped[i].Interval.Range.End;
                cursor = exclusionEnd > cursor ? exclusionEnd : cursor;
                leading = clamped[i].Detection;
            }
        }

        return new SceneRangeCalculationResult
        {
            SceneRanges = sceneRanges,
            ExcludedIntervals = clamped.ConvertAll(pair => pair.Interval),
            BoundaryTransitions = boundaries,
        };
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max) =>
        value < min ? min : value > max ? max : value;
}

// SceneRanges/BoundaryTransitions are always the same length, same order -
// zip the two to see both a scene's own extent and which transition(s), if
// any, bound it. ExcludedIntervals is the direct input CleanClipExtractionOptions.ExcludedIntervals
// expects.
public sealed record SceneRangeCalculationResult
{
    public required IReadOnlyList<TimeRange> SceneRanges { get; init; }

    public required IReadOnlyList<ExcludedInterval> ExcludedIntervals { get; init; }

    public required IReadOnlyList<SceneBoundaryTransitions> BoundaryTransitions { get; init; }
}

// The transition(s) immediately bounding one scene range, for UI/diagnostic
// display (e.g. Scene Review's "transition type/confidence" column) - never
// used by CleanClipExtractor itself, which only consumes ExcludedIntervals.
public sealed record SceneBoundaryTransitions
{
    // Null for the first scene range (nothing precedes it).
    public TransitionDetection? Leading { get; init; }

    // Null for the last scene range (nothing follows it).
    public TransitionDetection? Trailing { get; init; }
}
