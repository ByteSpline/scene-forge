namespace SceneForge.Infrastructure.Persistence;

// Autosave's two halves, deliberately kept as separate calls rather than one
// "run this stage" method: BeginStageAsync marks a stage as started (so a
// crash or a cancelled operation mid-stage is later visible as
// "interrupted" - see IProjectRecoveryService), and CompleteStageAsync is
// the only thing that ever advances the actual on-disk checkpoint. If a
// caller cancels between the two, the checkpoint written by the previous
// CompleteStageAsync call is untouched - "retain the last valid checkpoint"
// falls out of this shape directly, rather than needing its own special
// case.
public interface IAutosaveService
{
    Task BeginStageAsync(Guid projectId, ProjectStage stage, CancellationToken cancellationToken = default);

    // Saves document (stamping LastModifiedUtc) as the project's new
    // checkpoint, then clears the in-progress marker BeginStageAsync set for
    // this project. Returns the document actually persisted.
    Task<SceneForgeProjectDocument> CompleteStageAsync(SceneForgeProjectDocument document, CancellationToken cancellationToken = default);
}
