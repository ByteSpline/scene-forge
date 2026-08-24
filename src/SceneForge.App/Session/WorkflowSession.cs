using SceneForge.Media.Detection;
using SceneForge.Media.Domain;
using SceneForge.Media.Extraction;
using SceneForge.Media.Planning;
using SceneForge.Media.Rendering;
using SceneForge.Media.Sampling;

namespace SceneForge.App.Session;

// Mutable state shared across every workflow step's ViewModel, registered as
// a single App-lifetime instance in DI (see App.xaml.cs). Each step
// ViewModel is re-created (transient) on every navigation, so anything a
// later step needs from an earlier one - the imported files, analysis
// results, the user's Scene Review edits - has to live here rather than on
// a ViewModel instance that may no longer exist. Never touched by
// SceneForge.Media itself: this type exists only in the App/UI layer
// (CLAUDE.md rule 4).
public sealed class WorkflowSession
{
    public string? VideoFilePath { get; set; }

    public string? AudioFilePath { get; set; }

    public MediaInfo? VideoMediaInfo { get; set; }

    public MediaInfo? AudioMediaInfo { get; set; }

    public AnalysisProfile AnalysisProfile { get; set; } = AnalysisProfile.Balanced;

    public int Seed { get; set; } = 1;

    // Drives both TimelinePlanRequest.OutputTimeBase and RenderOutputSpec.FrameRate
    // - chosen once here so the two can never disagree (RenderPlanBuilder
    // cannot verify that agreement itself; see docs/PHASE_09_REPORT.md,
    // "Design summary", RenderOutputSpec.FrameRate remarks).
    public RationalFrameRate OutputFrameRate { get; set; } = new(30, 1);

    public IReadOnlyList<TransitionDetection>? Detections { get; set; }

    public SceneRangeCalculationResult? SceneRangeResult { get; set; }

    public CleanClipExtractionResult? ExtractionResult { get; set; }

    // Keyed by a clip's stable position in AcceptedClips followed by
    // RejectedClips (see SceneReviewViewModel) - holds every user edit
    // (include/exclude toggle, boundary adjustment) so revisiting Scene
    // Review after navigating away restores them rather than resetting to
    // the automatic accept/reject verdict.
    public Dictionary<int, ClipReviewOverride> ClipOverrides { get; } = [];

    // The clips actually available to TimelinePlanner after Scene Review -
    // built from AcceptedClips/RejectedClips plus ClipOverrides. Null until
    // Scene Review has run at least once.
    public IReadOnlyList<CleanClip>? ReviewedClips { get; set; }

    public TimelinePlan? TimelinePlan { get; set; }

    public AspectFitMode FitMode { get; set; } = AspectFitMode.Letterbox;

    public int OutputWidth { get; set; } = 1920;

    public int OutputHeight { get; set; } = 1080;

    public string? OutputVideoPath { get; set; }

    public RenderPlan? RenderPlan { get; set; }

    public RenderResult? RenderResult { get; set; }

    // Used by Completion's "Start over" command (CLAUDE.md rule 11/12 are
    // about files, not in-memory state, but starting a fresh session must
    // never leave a stale plan/result visible on a screen the user has not
    // actually reached yet).
    public void Reset()
    {
        VideoFilePath = null;
        AudioFilePath = null;
        VideoMediaInfo = null;
        AudioMediaInfo = null;
        AnalysisProfile = AnalysisProfile.Balanced;
        Seed = 1;
        OutputFrameRate = new RationalFrameRate(30, 1);
        Detections = null;
        SceneRangeResult = null;
        ExtractionResult = null;
        ClipOverrides.Clear();
        ReviewedClips = null;
        TimelinePlan = null;
        FitMode = AspectFitMode.Letterbox;
        OutputWidth = 1920;
        OutputHeight = 1080;
        OutputVideoPath = null;
        RenderPlan = null;
        RenderResult = null;
    }
}

// One user edit to a candidate clip from Scene Review. AdjustedRange starts
// out equal to the clip's own CleanClip.Range and is only ever narrowed by
// the boundary-adjustment controls (see SceneReviewViewModel.ClipBoundaryEditor).
public sealed record ClipReviewOverride
{
    public required bool IsIncluded { get; init; }

    public required TimeRange AdjustedRange { get; init; }
}
