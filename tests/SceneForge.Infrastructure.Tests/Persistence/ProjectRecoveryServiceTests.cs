using SceneForge.Core.Resources;
using SceneForge.Infrastructure.Persistence;
using SceneForge.Infrastructure.Tests.TestSupport;

namespace SceneForge.Infrastructure.Tests.Persistence;

public sealed class ProjectRecoveryServiceTests : IDisposable
{
    private readonly TempDirectoryFixture _fixture = new();
    private readonly ProjectLayout _layout;
    private readonly ProjectStore _store = new();
    private readonly AutosaveService _autosave;
    private readonly ProjectRecoveryService _recovery;

    public ProjectRecoveryServiceTests()
    {
        _layout = new ProjectLayout(_fixture.Path);
        _autosave = new AutosaveService(_store, _layout, new AdaptiveResourceGovernor());
        _recovery = new ProjectRecoveryService(_store, _layout);
    }

    [Fact]
    public async Task ScanForInterruptedProjectsAsync_NoProjectsRoot_ReturnsEmpty()
    {
        var results = await _recovery.ScanForInterruptedProjectsAsync();

        Assert.Empty(results);
    }

    [Fact]
    public async Task ScanForInterruptedProjectsAsync_CleanlyCompletedProject_IsNotReported()
    {
        var projectId = Guid.NewGuid();
        await _autosave.BeginStageAsync(projectId, ProjectStage.Analyzed);
        await _autosave.CompleteStageAsync(SampleDocumentBuilder.BuildMinimal(projectId, "a.mp4") with { Stage = ProjectStage.Analyzed });

        var results = await _recovery.ScanForInterruptedProjectsAsync();

        Assert.Empty(results);
    }

    [Fact]
    public async Task ScanForInterruptedProjectsAsync_InterruptedProjectWithCheckpoint_ReturnsItWithLastCheckpoint()
    {
        var projectId = Guid.NewGuid();
        await _autosave.CompleteStageAsync(SampleDocumentBuilder.BuildMinimal(projectId, "a.mp4") with { Stage = ProjectStage.Imported });
        await _autosave.BeginStageAsync(projectId, ProjectStage.Analyzed); // never completed - simulates a crash mid-stage.

        var results = await _recovery.ScanForInterruptedProjectsAsync();

        var recoverable = Assert.Single(results);
        Assert.Equal(projectId, recoverable.ProjectId);
        Assert.Equal(ProjectStage.Analyzed, recoverable.InterruptedStage);
        Assert.NotNull(recoverable.LastCheckpoint);
        Assert.Equal(ProjectStage.Imported, recoverable.LastCheckpoint!.Stage);
    }

    [Fact]
    public async Task ScanForInterruptedProjectsAsync_MarkerWithNoCheckpointYet_ReturnsProjectWithNullCheckpoint()
    {
        var projectId = Guid.NewGuid();
        await _autosave.BeginStageAsync(projectId, ProjectStage.Imported); // crashed before the very first checkpoint.

        var results = await _recovery.ScanForInterruptedProjectsAsync();

        var recoverable = Assert.Single(results);
        Assert.Null(recoverable.LastCheckpoint);
        Assert.Equal(ProjectStage.Imported, recoverable.InterruptedStage);
    }

    [Fact]
    public async Task ScanForInterruptedProjectsAsync_CorruptedCheckpointFile_StillReportsInterruptedStage()
    {
        var projectId = Guid.NewGuid();
        await _autosave.BeginStageAsync(projectId, ProjectStage.Analyzed);
        Directory.CreateDirectory(_layout.ProjectDirectory(projectId));
        await File.WriteAllTextAsync(_layout.ProjectFilePath(projectId), "not valid json");

        var results = await _recovery.ScanForInterruptedProjectsAsync();

        var recoverable = Assert.Single(results);
        Assert.Null(recoverable.LastCheckpoint);
        Assert.Equal(ProjectStage.Analyzed, recoverable.InterruptedStage);
    }

    [Fact]
    public async Task DiscardAsync_RemovesMarkerButKeepsCheckpointFile()
    {
        var projectId = Guid.NewGuid();
        await _autosave.CompleteStageAsync(SampleDocumentBuilder.BuildMinimal(projectId, "a.mp4") with { Stage = ProjectStage.Imported });
        await _autosave.BeginStageAsync(projectId, ProjectStage.Analyzed);

        await _recovery.DiscardAsync(projectId);

        Assert.False(File.Exists(_layout.InProgressMarkerPath(projectId)));
        Assert.True(File.Exists(_layout.ProjectFilePath(projectId)));

        var results = await _recovery.ScanForInterruptedProjectsAsync();
        Assert.Empty(results);
    }

    public void Dispose() => _fixture.Dispose();
}
