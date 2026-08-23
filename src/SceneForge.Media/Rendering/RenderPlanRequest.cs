using SceneForge.Media.Domain;
using SceneForge.Media.Planning;

namespace SceneForge.Media.Rendering;

// Everything one RenderPlanBuilder.Build call needs. SourceMediaInfo is a
// caller-supplied fact (the same MediaInfo CleanClipExtractor/FrameSampler
// already probed earlier in the pipeline) rather than something
// RenderPlanBuilder probes itself, keeping it pure and synchronous - the
// same "facts in, no I/O" shape TimelinePlanRequest already established for
// TimelinePlanner (see docs/PHASE_08_REPORT.md).
public sealed record RenderPlanRequest
{
    public required TimelinePlan TimelinePlan { get; init; }

    public required string SourceFilePath { get; init; }

    public required MediaInfo SourceMediaInfo { get; init; }

    public required RenderOutputSpec OutputSpec { get; init; }

    public required RenderAudioTrackSpec Audio { get; init; }
}
