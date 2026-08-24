namespace SceneForge.Infrastructure.Persistence;

// Thrown by ProjectStore.LoadAsync when a project file's own SchemaVersion
// does not match SceneForgeProjectDocument.CurrentSchemaVersion. This
// repository has never shipped a second schema version, so there is no
// migration path yet - the exception exists so a future version bump fails
// loudly and namely (CLAUDE.md rule 10) rather than a mismatched file being
// silently misread as the current shape.
public sealed class ProjectSchemaVersionException : ProjectPersistenceException
{
    public int FoundVersion { get; }

    public int ExpectedVersion { get; }

    public ProjectSchemaVersionException(int foundVersion, int expectedVersion)
        : base($"Project file schema version {foundVersion} is not supported by this version of SceneForge (expected {expectedVersion}).")
    {
        FoundVersion = foundVersion;
        ExpectedVersion = expectedVersion;
    }
}
