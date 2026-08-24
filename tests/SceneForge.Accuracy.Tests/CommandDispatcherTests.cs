using SceneForge.Accuracy.Cli;

namespace SceneForge.Accuracy.Tests;

// Regression coverage for a strict-review finding: CommandDispatcher used
// to call CommandLineOptions.Parse *before* its try/catch, so a malformed
// invocation (a dangling "--flag" with no following value) threw an
// unhandled ArgumentException straight out of RunAsync instead of the
// tool's intended clean, non-zero-exit error message. None of these cases
// need real ffmpeg - they all fail before any fixture/detector work starts.
public class CommandDispatcherTests
{
    [Fact]
    public async Task RunAsync_NoArguments_PrintsUsageAndReturnsFailureWithoutThrowing()
    {
        var exitCode = await CommandDispatcher.RunAsync([], CancellationToken.None);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task RunAsync_UnknownCommand_ReturnsFailureWithoutThrowing()
    {
        var exitCode = await CommandDispatcher.RunAsync(["not-a-real-command"], CancellationToken.None);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task RunAsync_DanglingOptionFlagWithNoValue_ReturnsFailureRatherThanThrowingUnhandled()
    {
        // This is exactly the parse failure that used to escape RunAsync's
        // try/catch entirely.
        var exitCode = await CommandDispatcher.RunAsync(["generate", "--output"], CancellationToken.None);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task RunAsync_MissingRequiredOption_ReturnsFailureRatherThanThrowingUnhandled()
    {
        var exitCode = await CommandDispatcher.RunAsync(["gate"], CancellationToken.None);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task RunAsync_PreCancelledToken_NeverThrowsUnhandledOutOfRunAsync()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Regardless of which internal path notices the cancellation first,
        // RunAsync itself must never let an exception escape uncaught -
        // that guarantee is what makes Program.cs's Ctrl+C wiring safe.
        var exitCode = await CommandDispatcher.RunAsync(
            ["evaluate", "--ffmpeg-base-dir", Path.GetTempPath()],
            cts.Token);

        Assert.True(exitCode is 1 or 130);
    }
}
