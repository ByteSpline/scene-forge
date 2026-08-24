using SceneForge.Infrastructure.Logging;
using SceneForge.Infrastructure.Tests.TestSupport;

namespace SceneForge.Infrastructure.Tests.Logging;

public sealed class RollingFileLoggerTests : IDisposable
{
    private readonly TempDirectoryFixture _fixture = new();

    [Fact]
    public void Log_WritesLineContainingLevelAndMessage()
    {
        var logger = new RollingFileLogger(_fixture.Path);

        logger.Log(LogLevel.Warning, "something happened");

        var contents = File.ReadAllText(Path.Combine(_fixture.Path, "sceneforge.log"));
        Assert.Contains("[Warning]", contents);
        Assert.Contains("something happened", contents);
    }

    [Fact]
    public void Log_IncludesExceptionDetailsWhenProvided()
    {
        var logger = new RollingFileLogger(_fixture.Path);

        logger.Log(LogLevel.Error, "failed", new InvalidOperationException("root cause"));

        var contents = File.ReadAllText(Path.Combine(_fixture.Path, "sceneforge.log"));
        Assert.Contains("root cause", contents);
    }

    [Fact]
    public void Log_ExceedingMaxFileSize_RotatesCurrentFileToTimestampedName()
    {
        var logger = new RollingFileLogger(_fixture.Path, maxFileSizeBytes: 50);

        for (var i = 0; i < 10; i++)
        {
            logger.Log(LogLevel.Info, $"line number {i} padded with extra text to grow the file quickly");
        }

        var rotatedFiles = Directory.GetFiles(_fixture.Path, "sceneforge-*.log");
        Assert.NotEmpty(rotatedFiles);
        Assert.True(File.Exists(Path.Combine(_fixture.Path, "sceneforge.log")));
    }

    [Fact]
    public void Log_RotationBeyondRetentionCap_DeletesOldestRotatedFiles()
    {
        var logger = new RollingFileLogger(_fixture.Path, maxFileSizeBytes: 20, maxRetainedFiles: 2);

        for (var i = 0; i < 30; i++)
        {
            logger.Log(LogLevel.Info, $"entry {i} with enough text to force frequent rotation across iterations");
        }

        var rotatedFiles = Directory.GetFiles(_fixture.Path, "sceneforge-*.log");
        Assert.True(rotatedFiles.Length <= 2, $"expected at most 2 retained rotated files, found {rotatedFiles.Length}");
    }

    [Fact]
    public void Constructor_NonPositiveMaxFileSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RollingFileLogger(_fixture.Path, maxFileSizeBytes: 0));
    }

    [Fact]
    public void Constructor_NonPositiveMaxRetainedFiles_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RollingFileLogger(_fixture.Path, maxRetainedFiles: 0));
    }

    public void Dispose() => _fixture.Dispose();
}
