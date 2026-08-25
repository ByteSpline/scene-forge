using System.IO;
using SceneForge.App.Persistence;
using SceneForge.App.Session;
using SceneForge.App.Tests.TestSupport;
using SceneForge.Core.Resources;
using SceneForge.Infrastructure.Logging;
using SceneForge.Infrastructure.Persistence;

namespace SceneForge.App.Tests.Persistence;

// Regression coverage for the release-review finding that
// ProjectPersistenceCoordinator.BuildDocumentAsync silently swallowed a
// corrupted prior checkpoint (while trying to preserve its CreatedUtc) with
// no diagnostic trail at all - a "silent fallback" CLAUDE.md rule 10 exists
// specifically to rule out.
public sealed class ProjectPersistenceCoordinatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SceneForgeTests", Guid.NewGuid().ToString("N"));

    public ProjectPersistenceCoordinatorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task CheckpointAsync_PriorCheckpointFileIsCorrupted_FallsBackButLogsAWarning()
    {
        var layout = new ProjectLayout(_root);
        var projectStore = new ProjectStore();
        var autosave = new AutosaveService(projectStore, layout, new AdaptiveResourceGovernor());
        var logger = new FakeAppLogger();
        var coordinator = new ProjectPersistenceCoordinator(
            projectStore,
            autosave,
            new StaleSourceDetector(),
            new TempFileRegistry(layout.TempRoot),
            layout,
            logger);

        var videoPath = Path.Combine(_root, "video.mp4");
        File.WriteAllBytes(videoPath, [1, 2, 3]);
        var session = new WorkflowSession { VideoFilePath = videoPath };

        await coordinator.CheckpointAsync(session, ProjectStage.Imported);

        // Corrupt the checkpoint BuildDocumentAsync will try to read back
        // (only to recover CreatedUtc) on the next call.
        Directory.CreateDirectory(layout.ProjectDirectory(session.ProjectId));
        await File.WriteAllTextAsync(layout.ProjectFilePath(session.ProjectId), "{ not valid json");

        await coordinator.CheckpointAsync(session, ProjectStage.Analyzed);

        var reloaded = await projectStore.LoadAsync(layout.ProjectFilePath(session.ProjectId));
        Assert.Equal(ProjectStage.Analyzed, reloaded.Stage);
        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("CreatedUtc", StringComparison.OrdinalIgnoreCase) &&
            e.Exception is ProjectCorruptedException);
    }

    [Fact]
    public async Task CheckpointAsync_NoPriorCheckpoint_SucceedsWithNoWarningLogged()
    {
        var layout = new ProjectLayout(_root);
        var projectStore = new ProjectStore();
        var autosave = new AutosaveService(projectStore, layout, new AdaptiveResourceGovernor());
        var logger = new FakeAppLogger();
        var coordinator = new ProjectPersistenceCoordinator(
            projectStore,
            autosave,
            new StaleSourceDetector(),
            new TempFileRegistry(layout.TempRoot),
            layout,
            logger);

        var videoPath = Path.Combine(_root, "video.mp4");
        File.WriteAllBytes(videoPath, [1, 2, 3]);
        var session = new WorkflowSession { VideoFilePath = videoPath };

        await coordinator.CheckpointAsync(session, ProjectStage.Imported);

        Assert.Empty(logger.Entries);
    }
}
