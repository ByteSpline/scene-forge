using SceneForge.Accuracy.Evaluation;
using SceneForge.Media.Processes;

namespace SceneForge.Accuracy.Tests;

// Regression coverage for a strict-review finding: the original
// HardwareDescriber shelled out via a raw, unbounded Process.Start with a
// dead-code "timeout" (WaitForExit ran *after* an already-blocking
// ReadToEnd), so a hung PowerShell/WMI call could stall `update-baseline`
// forever with no way to cancel. It now routes through the project's own
// hardened IProcessRunner - these tests prove both halves of the fix: a
// lookup-specific timeout degrades gracefully to "Unknown" (never thrown),
// while a genuine external cancellation still propagates rather than being
// silently swallowed into the same fallback.
public class HardwareDescriberTests
{
    [Fact]
    public async Task DescribeAsync_CpuNameLookupSucceeds_ReturnsIt()
    {
        var runner = new FakeProcessRunner(_ => Task.FromResult(Result(0, "AMD Ryzen 5 3500U\n")));

        var hardware = await HardwareDescriber.DescribeAsync(runner, CancellationToken.None);

        Assert.Equal("AMD Ryzen 5 3500U", hardware.CpuName);
        Assert.True(hardware.LogicalProcessorCount > 0);
    }

    [Fact]
    public async Task DescribeAsync_CpuNameLookupTimesOut_DegradesToUnknownRatherThanThrowing()
    {
        var runner = new FakeProcessRunner(_ => throw new ProcessTimeoutException(TimeSpan.FromSeconds(5), string.Empty, string.Empty));

        var hardware = await HardwareDescriber.DescribeAsync(runner, CancellationToken.None);

        Assert.Equal("Unknown", hardware.CpuName);
    }

    [Fact]
    public async Task DescribeAsync_CpuNameLookupFailsToLaunch_DegradesToUnknownRatherThanThrowing()
    {
        var runner = new FakeProcessRunner(_ => throw new ProcessLaunchException("powershell", "not found", new InvalidOperationException()));

        var hardware = await HardwareDescriber.DescribeAsync(runner, CancellationToken.None);

        Assert.Equal("Unknown", hardware.CpuName);
    }

    [Fact]
    public async Task DescribeAsync_ExitCodeNonZero_DegradesToUnknownRatherThanUsingGarbageOutput()
    {
        var runner = new FakeProcessRunner(_ => Task.FromResult(Result(1, "some error text")));

        var hardware = await HardwareDescriber.DescribeAsync(runner, CancellationToken.None);

        Assert.Equal("Unknown", hardware.CpuName);
    }

    [Fact]
    public async Task DescribeAsync_GenuineExternalCancellation_PropagatesRatherThanBeingSwallowed()
    {
        // Distinct from ProcessTimeoutException (this lookup's own internal
        // 5s bound): this simulates the *caller's* token already having
        // been cancelled (e.g. Ctrl+C during update-baseline), which must
        // never be silently downgraded to "Unknown" the way a lookup-local
        // timeout is.
        var runner = new FakeProcessRunner(_ => throw new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => HardwareDescriber.DescribeAsync(runner, CancellationToken.None));
    }

    private static ProcessExecutionResult Result(int exitCode, string standardOutput) => new()
    {
        ExitCode = exitCode,
        StandardOutput = standardOutput,
        StandardError = string.Empty,
        Elapsed = TimeSpan.Zero,
    };

    private sealed class FakeProcessRunner : IProcessRunner
    {
        private readonly Func<ProcessExecutionRequest, Task<ProcessExecutionResult>> _handler;

        public FakeProcessRunner(Func<ProcessExecutionRequest, Task<ProcessExecutionResult>> handler)
        {
            _handler = handler;
        }

        public Task<ProcessExecutionResult> RunAsync(ProcessExecutionRequest request, CancellationToken cancellationToken) =>
            _handler(request);
    }
}
