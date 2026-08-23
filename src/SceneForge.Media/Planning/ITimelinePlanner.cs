namespace SceneForge.Media.Planning;

public interface ITimelinePlanner
{
    // Pure and deterministic - no I/O, no OpenCvSharp, no ffmpeg. See
    // TimelinePlanner for the constraint-based selection algorithm. Accepts
    // a CancellationToken (checked once per placement) rather than being
    // async, matching this codebase's convention for CPU-only pipeline
    // stages that do not themselves await anything (e.g.
    // Extraction.Intervals.IntervalSubtractor, Extraction.Clustering.VisualClusterer)
    // - see docs/PHASE_08_REPORT.md, Design summary, for why an async
    // signature would be misleading here despite CLAUDE.md rule 5.
    TimelinePlan Plan(TimelinePlanRequest request, CancellationToken cancellationToken = default);
}
