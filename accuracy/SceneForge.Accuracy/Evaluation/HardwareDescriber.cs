using System.Runtime.InteropServices;
using SceneForge.Media.Processes;

namespace SceneForge.Accuracy.Evaluation;

// Best-effort: the CPU name lookup (a PowerShell/WMI query - the only piece
// that can meaningfully fail) runs through the project's own hardened
// ProcessRunner (no shell, bounded timeout, proper process-tree teardown -
// see ProcessRunner's own remarks) instead of a raw Process.Start, so a
// hung PowerShell/WMI query can never block `update-baseline` indefinitely
// and a real Ctrl+C still cancels it cooperatively (CLAUDE.md rule 5). Only
// a *timeout or launch failure specific to this lookup* degrades to
// "Unknown" - a genuine external cancellation still propagates, exactly
// like everywhere else in this codebase.
public static class HardwareDescriber
{
    private static readonly TimeSpan CpuNameLookupTimeout = TimeSpan.FromSeconds(5);

    public static Task<HardwareDescription> DescribeAsync(CancellationToken cancellationToken) =>
        DescribeAsync(new ProcessRunner(), cancellationToken);

    internal static async Task<HardwareDescription> DescribeAsync(IProcessRunner processRunner, CancellationToken cancellationToken)
    {
        var cpuName = await TryGetCpuNameAsync(processRunner, cancellationToken).ConfigureAwait(false) ?? "Unknown";
        var totalMemoryGigabytes = Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024.0 / 1024.0 / 1024.0, 1);

        return new HardwareDescription(
            cpuName,
            Environment.ProcessorCount,
            totalMemoryGigabytes,
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription);
    }

    private static async Task<string?> TryGetCpuNameAsync(IProcessRunner processRunner, CancellationToken cancellationToken)
    {
        try
        {
            var result = await processRunner.RunAsync(
                new ProcessExecutionRequest
                {
                    FileName = "powershell",
                    // Discrete ArgumentList entries (never a hand-escaped
                    // single command string) - the same never-through-a-
                    // shell posture ProcessRunner enforces for every other
                    // process this codebase launches.
                    Arguments = ["-NoProfile", "-NonInteractive", "-Command", "(Get-CimInstance Win32_Processor).Name"],
                    Timeout = CpuNameLookupTimeout,
                },
                cancellationToken).ConfigureAwait(false);

            var output = result.StandardOutput.Trim();
            return result.ExitCode == 0 && !string.IsNullOrEmpty(output) ? output : null;
        }
        catch (ProcessTimeoutException)
        {
            return null;
        }
        catch (ProcessLaunchException)
        {
            return null;
        }
    }
}
