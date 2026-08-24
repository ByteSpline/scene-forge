namespace SceneForge.Infrastructure.Persistence;

// Thrown by ProjectStore.LoadAsync whenever a project file cannot be trusted
// as a complete, valid SceneForgeProjectDocument - malformed JSON, an empty
// file, or valid JSON missing a required field. Never thrown for a merely
// stale source file (see IStaleSourceDetector) or an unsupported schema
// version (see ProjectSchemaVersionException) - those are recoverable/
// explainable conditions of their own, distinct from "this file is broken."
public sealed class ProjectCorruptedException : ProjectPersistenceException
{
    public ProjectCorruptedException(string message)
        : base(message)
    {
    }

    public ProjectCorruptedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
