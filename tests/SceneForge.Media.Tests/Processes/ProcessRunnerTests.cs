using System.Diagnostics;
using SceneForge.Media.Processes;

namespace SceneForge.Media.Tests.Processes;

// Deliberately targets only executables that ship with Windows itself
// (dotnet.exe, ping.exe, cmd.exe) so these tests are hermetic: no ffmpeg
// binary, no network egress (ping only ever targets the loopback address),
// and no shell metacharacter interpretation of test data.
public class ProcessRunnerTests
{
    private static readonly string DotNetExecutablePath = ResolveDotNetExecutablePath();

    [Fact]
    public async Task RunAsync_SuccessfulProcess_CapturesExitCodeAndStdOut()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(
            new ProcessExecutionRequest { FileName = DotNetExecutablePath, Arguments = ["--version"] },
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StandardOutput));
        Assert.True(result.Elapsed > TimeSpan.Zero);
    }

    [Fact]
    public async Task RunAsync_NonZeroExit_ReturnsResultInsteadOfThrowing()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(
            new ProcessExecutionRequest
            {
                FileName = DotNetExecutablePath,
                Arguments = ["exec", @"C:\definitely\does\not\exist.dll"],
            },
            CancellationToken.None);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("does not exist", result.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_MissingExecutable_ThrowsProcessLaunchException()
    {
        var runner = new ProcessRunner();

        var exception = await Assert.ThrowsAsync<ProcessLaunchException>(() => runner.RunAsync(
            new ProcessExecutionRequest { FileName = @"C:\definitely\does\not\exist.exe", Arguments = [] },
            CancellationToken.None));

        Assert.Equal(@"C:\definitely\does\not\exist.exe", exception.FileName);
    }

    [Fact]
    public async Task RunAsync_OutputProgress_ReceivesStandardOutputLines()
    {
        var runner = new ProcessRunner();
        var receivedLines = new List<ProcessOutputLine>();

        var result = await runner.RunAsync(
            new ProcessExecutionRequest
            {
                FileName = DotNetExecutablePath,
                Arguments = ["--version"],
                OutputProgress = new Progress<ProcessOutputLine>(receivedLines.Add),
            },
            CancellationToken.None);

        Assert.Contains(receivedLines, l => l.Channel == ProcessOutputChannel.StandardOutput);
        Assert.Equal(result.StandardOutput.TrimEnd('\n'), string.Join('\n', receivedLines.Select(l => l.Text)));
    }

    [Fact]
    public async Task RunAsync_ExternalCancellation_ThrowsOperationCanceledExceptionAndKillsProcess()
    {
        var runner = new ProcessRunner();
        using var cts = new CancellationTokenSource();

        var baseline = CountProcesses("PING");
        var runTask = runner.RunAsync(
            new ProcessExecutionRequest { FileName = "ping.exe", Arguments = ["-n", "30", "127.0.0.1"] },
            cts.Token);

        await WaitForProcessCountAsync("PING", baseline + 1, TimeSpan.FromSeconds(5));
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => runTask);
        await WaitForProcessCountAsync("PING", baseline, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RunAsync_Timeout_ThrowsProcessTimeoutExceptionAndKillsProcess()
    {
        var runner = new ProcessRunner();
        var timeout = TimeSpan.FromMilliseconds(500);

        var baseline = CountProcesses("PING");

        var exception = await Assert.ThrowsAsync<ProcessTimeoutException>(() => runner.RunAsync(
            new ProcessExecutionRequest { FileName = "ping.exe", Arguments = ["-n", "30", "127.0.0.1"], Timeout = timeout },
            CancellationToken.None));

        Assert.Equal(timeout, exception.Timeout);
        await WaitForProcessCountAsync("PING", baseline, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RunAsync_OnCancellation_KillsEntireProcessTreeNotJustTheDirectChild()
    {
        var runner = new ProcessRunner();
        using var cts = new CancellationTokenSource();

        var baseline = CountProcesses("PING");
        var runTask = runner.RunAsync(
            new ProcessExecutionRequest { FileName = "cmd.exe", Arguments = ["/c", "ping", "-n", "30", "127.0.0.1"] },
            cts.Token);

        // cmd.exe spawns ping.exe as a grandchild of this test process; wait for it to appear.
        await WaitForProcessCountAsync("PING", baseline + 1, TimeSpan.FromSeconds(5));
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => runTask);

        // If only cmd.exe were killed (not the tree), the orphaned ping.exe would remain.
        await WaitForProcessCountAsync("PING", baseline, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RunAsync_CapturedOutputExceedsBudget_IsTruncated()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(
            new ProcessExecutionRequest
            {
                FileName = DotNetExecutablePath,
                Arguments = ["--info"],
                MaxCapturedBytesPerStream = 1024,
            },
            CancellationToken.None);

        Assert.EndsWith("...[truncated]", result.StandardOutput);
        Assert.True(result.StandardOutput.Length <= 1024 + "...[truncated]".Length);
    }

    // dotnet.exe is used as a real, non-shell, always-present executable to
    // exercise RunAsync against. The current test process may be hosted by
    // dotnet.exe directly, by a testhost apphost, or launched with
    // DOTNET_HOST_PATH set by the SDK, so this tries each in turn rather than
    // assuming one specific hosting shape.
    private static string ResolveDotNetExecutablePath()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrEmpty(fromEnvironment) && File.Exists(fromEnvironment))
        {
            return fromEnvironment;
        }

        var currentModule = Process.GetCurrentProcess().MainModule?.FileName;
        if (currentModule is not null && Path.GetFileName(currentModule).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return currentModule;
        }

        var pathEntries = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
        var fromPath = pathEntries
            .Select(directory => Path.Combine(directory, "dotnet.exe"))
            .FirstOrDefault(File.Exists);
        if (fromPath is not null)
        {
            return fromPath;
        }

        const string wellKnownPath = @"C:\Program Files\dotnet\dotnet.exe";
        if (File.Exists(wellKnownPath))
        {
            return wellKnownPath;
        }

        throw new InvalidOperationException("Could not resolve dotnet.exe for use as a test target executable.");
    }

    private static int CountProcesses(string name) => Process.GetProcessesByName(name).Length;

    private static async Task WaitForProcessCountAsync(string name, int expectedCount, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (CountProcesses(name) != expectedCount)
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(50, CancellationToken.None);
        }
    }
}
