using SceneForge.Accuracy.Cli;

namespace SceneForge.Accuracy.Tests;

public class ExitCodeMapperTests
{
    [Fact]
    public void Map_OperationCanceledException_ReturnsConventionalCancelledExitCodeAndCleanMessage()
    {
        var writer = new StringWriter();

        var exitCode = ExitCodeMapper.Map(new OperationCanceledException(), "evaluate", writer);

        Assert.Equal(130, exitCode);
        Assert.Equal("Cancelled.", writer.ToString().Trim());
    }

    [Fact]
    public void Map_ProcessTimeoutStyleCancellation_AlsoTreatedAsCancelled()
    {
        // ProcessTimeoutException extends OperationCanceledException
        // specifically so callers that already branch on cancellation
        // generically also catch it - a plain ArgumentException stand-in
        // is used here to keep this test free of a ProcessRunner
        // dependency, but any OperationCanceledException subtype must hit
        // the same branch.
        var writer = new StringWriter();

        var exitCode = ExitCodeMapper.Map(new TaskCanceledException(), "gate", writer);

        Assert.Equal(130, exitCode);
    }

    [Fact]
    public void Map_ArgumentException_ReturnsFailureExitCodeWithCommandAndMessage()
    {
        var writer = new StringWriter();

        var exitCode = ExitCodeMapper.Map(new ArgumentException("Missing required option '--output'."), "generate", writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("generate", writer.ToString());
        Assert.Contains("Missing required option '--output'.", writer.ToString());
    }

    [Fact]
    public void Map_UnexpectedException_ReturnsFailureExitCode()
    {
        var writer = new StringWriter();

        var exitCode = ExitCodeMapper.Map(new InvalidOperationException("boom"), "update-baseline", writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("boom", writer.ToString());
    }
}
