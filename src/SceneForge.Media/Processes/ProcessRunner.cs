using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace SceneForge.Media.Processes;

// Hardened process invocation: never goes through a shell (UseShellExecute is
// always false and arguments are always passed as discrete ArgumentList
// entries, never concatenated into a single command string), captures
// stdout/stderr asynchronously with a bounded buffer, honors both an external
// CancellationToken and an internal timeout, and kills the entire child
// process tree - not just the immediate child - when either fires.
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessExecutionResult> RunAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startInfo = BuildStartInfo(request);
        var stopwatch = Stopwatch.StartNew();

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            throw new ProcessLaunchException(request.FileName, $"Failed to start process '{request.FileName}'.", ex);
        }

        var standardOutput = new BoundedTextCollector(request.MaxCapturedBytesPerStream);
        var standardError = new BoundedTextCollector(request.MaxCapturedBytesPerStream);

        using var timeoutCts = request.Timeout is { } timeout
            ? new CancellationTokenSource(timeout)
            : null;
        using var linkedCts = timeoutCts is null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var linkedToken = linkedCts.Token;

        var standardOutputPump = PumpAsync(process.StandardOutput, ProcessOutputChannel.StandardOutput, standardOutput, request.OutputProgress, linkedToken);
        var standardErrorPump = PumpAsync(process.StandardError, ProcessOutputChannel.StandardError, standardError, request.OutputProgress, linkedToken);

        try
        {
            await process.WaitForExitAsync(linkedToken).ConfigureAwait(false);
            await Task.WhenAll(standardOutputPump, standardErrorPump).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linkedToken.IsCancellationRequested)
        {
            KillProcessTree(process);
            await WaitQuietlyAsync(standardOutputPump, standardErrorPump).ConfigureAwait(false);

            if (timeoutCts is { IsCancellationRequested: true } && !cancellationToken.IsCancellationRequested)
            {
                throw new ProcessTimeoutException(request.Timeout!.Value, standardOutput.ToString(), standardError.ToString());
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }

        stopwatch.Stop();

        return new ProcessExecutionResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = standardOutput.ToString(),
            StandardError = standardError.ToString(),
            Elapsed = stopwatch.Elapsed,
        };
    }

    internal static ProcessStartInfo BuildStartInfo(ProcessExecutionRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (!string.IsNullOrEmpty(request.WorkingDirectory))
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static async Task PumpAsync(
        StreamReader reader,
        ProcessOutputChannel channel,
        BoundedTextCollector collector,
        IProgress<ProcessOutputLine>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                collector.AppendLine(line);
                progress?.Report(new ProcessOutputLine(channel, line));
            }
        }
        catch (OperationCanceledException)
        {
            // The process is being torn down; RunAsync surfaces the cancellation/timeout.
        }
        catch (IOException)
        {
            // The pipe was closed because the process was killed.
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited between the check and the kill attempt.
        }
    }

    private static async Task WaitQuietlyAsync(params Task[] tasks)
    {
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch
        {
            // A pump fault here is secondary to the cancellation/timeout already being thrown.
        }
    }
}
