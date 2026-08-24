using SceneForge.App.Session;
using SceneForge.Infrastructure.Persistence;

namespace SceneForge.App.Persistence;

// The one App-layer seam between WorkflowSession and
// SceneForge.Infrastructure.Persistence - every ViewModel that completes a
// pipeline stage calls BeginStageAsync before starting real work and
// CheckpointAsync once it has genuinely finished, rather than any ViewModel
// touching IAutosaveService/IProjectStore directly. Both methods are
// best-effort: a persistence failure is logged and swallowed, never thrown
// into the workflow, because autosave must never be the reason the user's
// actual pipeline run fails (see ProjectPersistenceCoordinator's remarks).
public interface IProjectPersistenceCoordinator
{
    Task BeginStageAsync(WorkflowSession session, ProjectStage stage, CancellationToken cancellationToken = default);

    Task CheckpointAsync(WorkflowSession session, ProjectStage stage, CancellationToken cancellationToken = default);

    // Normal-completion cleanup: deletes every registered app-owned temp
    // file. Called once the workflow reaches Completion.
    Task FinalizeAsync(CancellationToken cancellationToken = default);
}
