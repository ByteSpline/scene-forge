namespace SceneForge.Infrastructure.Persistence;

public sealed class AutosaveService : IAutosaveService
{
    private readonly IProjectStore _projectStore;
    private readonly ProjectLayout _layout;

    public AutosaveService(IProjectStore projectStore, ProjectLayout layout)
    {
        _projectStore = projectStore;
        _layout = layout;
    }

    public async Task BeginStageAsync(Guid projectId, ProjectStage stage, CancellationToken cancellationToken = default)
    {
        var directory = _layout.ProjectDirectory(projectId);
        Directory.CreateDirectory(directory);

        var markerPath = _layout.InProgressMarkerPath(projectId);
        var markerContents = $"{stage}|{DateTimeOffset.UtcNow:O}";
        await File.WriteAllTextAsync(markerPath, markerContents, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SceneForgeProjectDocument> CompleteStageAsync(SceneForgeProjectDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var updated = document with { LastModifiedUtc = DateTimeOffset.UtcNow };
        var projectFilePath = _layout.ProjectFilePath(updated.ProjectId);
        await _projectStore.SaveAsync(updated, projectFilePath, cancellationToken).ConfigureAwait(false);

        var markerPath = _layout.InProgressMarkerPath(updated.ProjectId);
        if (File.Exists(markerPath))
        {
            File.Delete(markerPath);
        }

        return updated;
    }
}
