using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace SceneForge.Media.Sampling;

// Real ffmpeg process launcher. Hardened the same way as
// SceneForge.Media.Processes.ProcessRunner (never a shell, arguments only
// ever go through ArgumentList, entire process tree killed on teardown) but
// exposes stdout as a raw Stream instead of buffering it as text, since it
// carries binary rawvideo frame data rather than line-oriented output.
internal sealed class FfmpegFrameSamplingProcessLauncher : IFrameSamplingProcessLauncher
{
    public Task<IFrameSamplingProcess> StartAsync(string ffmpegPath, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ffmpegPath);
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = false };

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            process.Dispose();
            throw new FrameSamplingException($"Failed to start ffmpeg at '{ffmpegPath}'.", ex);
        }

        return Task.FromResult<IFrameSamplingProcess>(new FfmpegFrameSamplingProcess(process));
    }

    private sealed class FfmpegFrameSamplingProcess : IFrameSamplingProcess
    {
        private readonly Process _process;
        private int _disposed;

        public FfmpegFrameSamplingProcess(Process process)
        {
            _process = process;
        }

        public Stream StandardOutput => _process.StandardOutput.BaseStream;

        public TextReader StandardError => _process.StandardError;

        public int ExitCode => _process.ExitCode;

        public Task WaitForExitAsync(CancellationToken cancellationToken) => _process.WaitForExitAsync(cancellationToken);

        public void Kill()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // Process already exited between the check and the kill attempt.
            }
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _process.Dispose();
            }

            return ValueTask.CompletedTask;
        }
    }
}
