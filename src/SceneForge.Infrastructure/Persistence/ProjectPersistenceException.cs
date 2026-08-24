namespace SceneForge.Infrastructure.Persistence;

public class ProjectPersistenceException : Exception
{
    public ProjectPersistenceException(string message)
        : base(message)
    {
    }

    public ProjectPersistenceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
