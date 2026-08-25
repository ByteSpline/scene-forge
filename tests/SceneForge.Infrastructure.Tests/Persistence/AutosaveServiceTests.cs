using SceneForge.Core.Resources;
using SceneForge.Infrastructure.Persistence;
using SceneForge.Infrastructure.Tests.TestSupport;

namespace SceneForge.Infrastructure.Tests.Persistence;

public sealed class AutosaveServiceTests : IDisposable
{
    private readonly TempDirectoryFixture _fixture = new();
    private readonly ProjectLayout _layout;
    private readonly AutosaveService _autosave;

    public AutosaveServiceTests()
    {
        _layout = new ProjectLayout(_fixture.Path);
        _autosave = new AutosaveService(new ProjectStore(), _layout, new AdaptiveResourceGovernor());
    }

    [Fact]
    public async Task BeginStageAsync_WritesInProgressMarkerFile()
    {
        var projectId = Guid.NewGuid();

        await _autosave.BeginStageAsync(projectId, ProjectStage.Analyzed);

        Assert.True(File.Exists(_layout.InProgressMarkerPath(projectId)));
    }

    [Fact]
    public async Task CompleteStageAsync_WritesCheckpointAndClearsMarker()
    {
        var projectId = Guid.NewGuid();
        await _autosave.BeginStageAsync(projectId, ProjectStage.Analyzed);
        var document = SampleDocumentBuilder.BuildMinimal(projectId, "a.mp4") with { Stage = ProjectStage.Analyzed };

        await _autosave.CompleteStageAsync(document);

        Assert.True(File.Exists(_layout.ProjectFilePath(projectId)));
        Assert.False(File.Exists(_layout.InProgressMarkerPath(projectId)));
    }

    [Fact]
    public async Task CompleteStageAsync_StampsLastModifiedUtcToNow()
    {
        var projectId = Guid.NewGuid();
        var stale = DateTimeOffset.UtcNow.AddDays(-1);
        var document = SampleDocumentBuilder.BuildMinimal(projectId, "a.mp4") with { LastModifiedUtc = stale };

        var saved = await _autosave.CompleteStageAsync(document);

        Assert.True(saved.LastModifiedUtc > stale);
    }

    [Fact]
    public async Task CancellationBetweenBeginAndComplete_PreviousCheckpointIsRetained()
    {
        var projectId = Guid.NewGuid();
        var firstDocument = SampleDocumentBuilder.BuildMinimal(projectId, "a.mp4") with { Stage = ProjectStage.Imported };
        var savedFirst = await _autosave.CompleteStageAsync(firstDocument);

        // A stage begins (marker written) but is interrupted (cancelled)
        // before CompleteStageAsync is ever called for it.
        await _autosave.BeginStageAsync(projectId, ProjectStage.Analyzed);

        var store = new ProjectStore();
        var checkpointOnDisk = await store.LoadAsync(_layout.ProjectFilePath(projectId));

        Assert.Equal(ProjectStage.Imported, checkpointOnDisk.Stage);
        Assert.Equal(savedFirst.LastModifiedUtc, checkpointOnDisk.LastModifiedUtc);
        Assert.True(File.Exists(_layout.InProgressMarkerPath(projectId)));
    }

    public void Dispose() => _fixture.Dispose();
}
