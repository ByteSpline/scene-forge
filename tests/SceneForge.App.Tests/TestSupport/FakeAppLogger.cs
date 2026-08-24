using SceneForge.Infrastructure.Logging;

namespace SceneForge.App.Tests.TestSupport;

internal sealed class FakeAppLogger : IAppLogger
{
    public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

    public void Log(LogLevel level, string message, Exception? exception = null) => Entries.Add((level, message, exception));
}
