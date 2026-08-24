using SceneForge.App.Persistence;
using SceneForge.App.Session;
using SceneForge.Infrastructure.Persistence;

namespace SceneForge.App.Tests.TestSupport;

// A no-op stand-in for IProjectPersistenceCoordinator - every method returns
// an already-completed Task and never touches disk, so ViewModel tests that
// call Continue/RunAsync synchronously (via Execute(null) or by awaiting a
// gated fake service) see no behavior difference from before this
// dependency existed. BeginStages/Checkpoints are recorded so a test can
// assert persistence was actually invoked at the expected point without
// needing a real IProjectStore/IAutosaveService.
public sealed class FakeProjectPersistenceCoordinator : IProjectPersistenceCoordinator
{
    public List<ProjectStage> BegunStages { get; } = [];

    public List<ProjectStage> CheckpointedStages { get; } = [];

    public int FinalizeCallCount { get; private set; }

    public Task BeginStageAsync(WorkflowSession session, ProjectStage stage, CancellationToken cancellationToken = default)
    {
        BegunStages.Add(stage);
        return Task.CompletedTask;
    }

    public Task CheckpointAsync(WorkflowSession session, ProjectStage stage, CancellationToken cancellationToken = default)
    {
        CheckpointedStages.Add(stage);
        return Task.CompletedTask;
    }

    public Task FinalizeAsync(CancellationToken cancellationToken = default)
    {
        FinalizeCallCount++;
        return Task.CompletedTask;
    }
}
