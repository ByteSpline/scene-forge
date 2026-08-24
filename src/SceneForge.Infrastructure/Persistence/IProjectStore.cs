namespace SceneForge.Infrastructure.Persistence;

public interface IProjectStore
{
    Task SaveAsync(SceneForgeProjectDocument document, string projectFilePath, CancellationToken cancellationToken = default);

    // Throws ProjectCorruptedException if the file is not valid JSON or is
    // missing a required field, or ProjectSchemaVersionException if its
    // SchemaVersion does not match SceneForgeProjectDocument.CurrentSchemaVersion.
    Task<SceneForgeProjectDocument> LoadAsync(string projectFilePath, CancellationToken cancellationToken = default);
}
