using SceneForge.Core.Resources;

namespace SceneForge.Infrastructure.Persistence;

public sealed class AutosaveService : IAutosaveService
{
    // Project documents/markers are small JSON/text files, but a completely
    // full disk still fails the write - this is a low floor (a few MB), not
    // a size prediction, purely to fail with a clear, catchable
    // InsufficientDiskSpaceException instead of a raw IOException mid-write.
    private const long MinimumRequiredFreeBytes = 10_000_000;

    private readonly IProjectStore _projectStore;
    private readonly ProjectLayout _layout;
    private readonly IAdaptiveResourceGovernor _resourceGovernor;

    public AutosaveService(IProjectStore projectStore, ProjectLayout layout, IAdaptiveResourceGovernor resourceGovernor)
    {
        _projectStore = projectStore;
        _layout = layout;
        _resourceGovernor = resourceGovernor;
    }

    public async Task BeginStageAsync(Guid projectId, ProjectStage stage, CancellationToken cancellationToken = default)
    {
        var directory = _layout.ProjectDirectory(projectId);
        Directory.CreateDirectory(directory);
        _resourceGovernor.EnsureSufficientDiskSpace(directory, MinimumRequiredFreeBytes);

        var markerPath = _layout.InProgressMarkerPath(projectId);
        var markerContents = $"{stage}|{DateTimeOffset.UtcNow:O}";
        await File.WriteAllTextAsync(markerPath, markerContents, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SceneForgeProjectDocument> CompleteStageAsync(SceneForgeProjectDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var updated = document with { LastModifiedUtc = DateTimeOffset.UtcNow };
        var projectFilePath = _layout.ProjectFilePath(updated.ProjectId);
        _resourceGovernor.EnsureSufficientDiskSpace(projectFilePath, MinimumRequiredFreeBytes);
        await _projectStore.SaveAsync(updated, projectFilePath, cancellationToken).ConfigureAwait(false);

        var markerPath = _layout.InProgressMarkerPath(updated.ProjectId);
        if (File.Exists(markerPath))
        {
            File.Delete(markerPath);
        }

        return updated;
    }
}
