namespace SceneForge.Infrastructure.Logging;

public interface IAppLogger
{
    void Log(LogLevel level, string message, Exception? exception = null);
}
