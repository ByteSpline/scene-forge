using System.Globalization;

namespace SceneForge.Infrastructure.Persistence;

public sealed class ProjectRecoveryService : IProjectRecoveryService
{
    private readonly IProjectStore _projectStore;
    private readonly ProjectLayout _layout;

    public ProjectRecoveryService(IProjectStore projectStore, ProjectLayout layout)
    {
        _projectStore = projectStore;
        _layout = layout;
    }

    public async Task<IReadOnlyList<RecoverableProject>> ScanForInterruptedProjectsAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<RecoverableProject>();
        if (!Directory.Exists(_layout.ProjectsRoot))
        {
            return results;
        }

        foreach (var directory in Directory.EnumerateDirectories(_layout.ProjectsRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var projectId))
            {
                continue;
            }

            var markerPath = _layout.InProgressMarkerPath(projectId);
            if (!File.Exists(markerPath))
            {
                continue;
            }

            var (stage, timestamp) = await ReadMarkerAsync(markerPath, cancellationToken).ConfigureAwait(false);

            SceneForgeProjectDocument? checkpoint = null;
            var projectFilePath = _layout.ProjectFilePath(projectId);
            if (File.Exists(projectFilePath))
            {
                try
                {
                    checkpoint = await _projectStore.LoadAsync(projectFilePath, cancellationToken).ConfigureAwait(false);
                }
                catch (ProjectPersistenceException)
                {
                    // The marker alone is still real evidence of an
                    // interruption even if its checkpoint file cannot be
                    // read - surface the project with LastCheckpoint null
                    // rather than dropping it from the scan entirely.
                    checkpoint = null;
                }
            }

            results.Add(new RecoverableProject
            {
                ProjectId = projectId,
                ProjectDirectory = directory,
                LastCheckpoint = checkpoint,
                InterruptedStage = stage,
                InterruptedAtUtc = timestamp,
            });
        }

        return results;
    }

    public Task DiscardAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var markerPath = _layout.InProgressMarkerPath(projectId);
        if (File.Exists(markerPath))
        {
            File.Delete(markerPath);
        }

        return Task.CompletedTask;
    }

    private static async Task<(ProjectStage Stage, DateTimeOffset Timestamp)> ReadMarkerAsync(string markerPath, CancellationToken cancellationToken)
    {
        try
        {
            var contents = await File.ReadAllTextAsync(markerPath, cancellationToken).ConfigureAwait(false);
            var parts = contents.Split('|', 2);
            if (parts.Length == 2
                && Enum.TryParse<ProjectStage>(parts[0], out var stage)
                && DateTimeOffset.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
            {
                return (stage, timestamp);
            }
        }
        catch (IOException)
        {
            // Fall through - an unreadable marker file is still evidence
            // that this project was interrupted; report it with the most
            // conservative stage/timestamp rather than skipping it.
        }

        return (ProjectStage.Created, DateTimeOffset.MinValue);
    }
}
