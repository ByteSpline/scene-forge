namespace SceneForge.Infrastructure.Persistence;

// The last pipeline stage a project's on-disk checkpoint actually reflects.
// AutosaveService only ever writes a new checkpoint once a stage's real work
// has completed (see AutosaveService.CompleteStageAsync) - a value here is
// always "this much was genuinely finished," never a partial/in-flight
// state, so a crash or cancellation mid-stage always resumes from the
// previous value, never a corrupt in-between one.
public enum ProjectStage
{
    Created,
    Imported,
    Analyzed,
    Reviewed,
    TimelinePlanned,
    RenderConfigured,
    Completed,
}
