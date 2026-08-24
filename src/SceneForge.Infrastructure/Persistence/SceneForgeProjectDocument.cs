using SceneForge.Media.Detection;
using SceneForge.Media.Detection.Fusion;
using SceneForge.Media.Domain;
using SceneForge.Media.Sampling;

namespace SceneForge.Infrastructure.Persistence;

// The single, versioned JSON root every SceneForge project persists as -
// ProjectStore reads/writes exactly this type. Every non-scalar field
// deliberately reuses the same domain type the pipeline itself produces
// (TransitionDetection, ClipScore, RationalFrameRate, ...) rather than a
// shadow DTO, so the persisted shape never silently drifts from what a given
// SchemaVersion actually recorded. SchemaVersion is checked by ProjectStore
// on load; bumping CurrentSchemaVersion is the only sanctioned way to change
// this type's meaning once real projects exist on disk (a future migration
// step would key off the mismatch ProjectStore.LoadAsync already throws).
public sealed record SceneForgeProjectDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required Guid ProjectId { get; init; }

    public required DateTimeOffset CreatedUtc { get; init; }

    public required DateTimeOffset LastModifiedUtc { get; init; }

    public required ProjectStage Stage { get; init; }

    public required SourceFingerprint VideoSource { get; init; }

    // Null only before an audio track has been imported - every stage from
    // Analyzed onward requires one (see Session.WorkflowSession's remarks on
    // why audio is collected up front), so in practice this is set from
    // ProjectStage.Imported onward.
    public SourceFingerprint? AudioSource { get; init; }

    public AnalysisProfile? AnalysisProfile { get; init; }

    // Which TransitionFuser/classifier threshold set produced Detections -
    // see TransitionDetectionProfileVersion's own remarks on why detection
    // results are always tagged with the version that produced them.
    public TransitionDetectionProfileVersion? DetectorConfigVersion { get; init; }

    public RationalFrameRate? OutputFrameRate { get; init; }

    public IReadOnlyList<TransitionDetection>? Detections { get; init; }

    public IReadOnlyList<ClipMetadataRecord>? Clips { get; init; }

    public IReadOnlyList<ManualOverrideRecord>? ManualOverrides { get; init; }

    public int? TimelineSeed { get; init; }

    public RenderSettingsRecord? RenderSettings { get; init; }
}
