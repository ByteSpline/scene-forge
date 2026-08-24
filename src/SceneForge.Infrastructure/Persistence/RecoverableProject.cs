namespace SceneForge.Infrastructure.Persistence;

// One project IProjectRecoveryService found with a leftover in-progress
// marker (see AutosaveService.BeginStageAsync) and no matching completion -
// i.e. the application did not shut down cleanly while this project was
// mid-stage. LastCheckpoint is the most recent stage that DID complete
// (null only if the process crashed before the very first checkpoint was
// ever written); InterruptedStage/InterruptedAtUtc describe the stage that
// was in flight when the interruption happened.
public sealed record RecoverableProject
{
    public required Guid ProjectId { get; init; }

    public required string ProjectDirectory { get; init; }

    public SceneForgeProjectDocument? LastCheckpoint { get; init; }

    public required ProjectStage InterruptedStage { get; init; }

    public required DateTimeOffset InterruptedAtUtc { get; init; }
}
