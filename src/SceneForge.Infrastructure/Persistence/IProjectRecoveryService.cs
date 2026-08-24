namespace SceneForge.Infrastructure.Persistence;

// Startup-time scan for projects an earlier run of the application did not
// shut down cleanly while working on (see AutosaveService/RecoverableProject).
public interface IProjectRecoveryService
{
    Task<IReadOnlyList<RecoverableProject>> ScanForInterruptedProjectsAsync(CancellationToken cancellationToken = default);

    // Clears the in-progress marker for projectId without touching its
    // checkpoint file or any other project data (CLAUDE.md rule 11 - never
    // delete app-owned project data the user has not explicitly asked to
    // remove; this only silences the "interrupted" flag).
    Task DiscardAsync(Guid projectId, CancellationToken cancellationToken = default);
}
