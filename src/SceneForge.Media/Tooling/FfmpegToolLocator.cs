using SceneForge.Media.Processes;

namespace SceneForge.Media.Tooling;

// Resolves ffmpeg/ffprobe strictly relative to the running application (never
// the system PATH, per CLAUDE.md rule "never depend on PATH in packaged
// builds") and proves both binaries actually launch before returning them, so
// a missing or incompatible toolchain fails loudly at startup rather than on
// the first probe a user triggers.
public sealed class FfmpegToolLocator : IFfmpegToolLocator
{
    private static readonly TimeSpan VersionCheckTimeout = TimeSpan.FromSeconds(10);

    private readonly IProcessRunner _processRunner;
    private readonly string _toolsDirectory;

    public FfmpegToolLocator(IProcessRunner processRunner, string? applicationBaseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(processRunner);

        _processRunner = processRunner;
        _toolsDirectory = Path.Combine(applicationBaseDirectory ?? AppContext.BaseDirectory, "tools", "ffmpeg");
    }

    public async Task<FfmpegToolPaths> LocateAsync(CancellationToken cancellationToken)
    {
        var ffprobePath = Path.Combine(_toolsDirectory, "ffprobe.exe");
        var ffmpegPath = Path.Combine(_toolsDirectory, "ffmpeg.exe");

        var missing = new List<string>();
        if (!File.Exists(ffprobePath))
        {
            missing.Add(ffprobePath);
        }

        if (!File.Exists(ffmpegPath))
        {
            missing.Add(ffmpegPath);
        }

        if (missing.Count > 0)
        {
            throw new FfmpegToolsNotFoundException(missing);
        }

        await ValidateExecutableAsync(ffprobePath, "ffprobe version", cancellationToken).ConfigureAwait(false);
        await ValidateExecutableAsync(ffmpegPath, "ffmpeg version", cancellationToken).ConfigureAwait(false);

        return new FfmpegToolPaths
        {
            FfprobePath = ffprobePath,
            FfmpegPath = ffmpegPath,
        };
    }

    private async Task ValidateExecutableAsync(string executablePath, string expectedBannerPrefix, CancellationToken cancellationToken)
    {
        ProcessExecutionResult result;
        try
        {
            result = await _processRunner.RunAsync(
                new ProcessExecutionRequest
                {
                    FileName = executablePath,
                    Arguments = ["-version"],
                    Timeout = VersionCheckTimeout,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (ProcessLaunchException ex)
        {
            throw new FfmpegToolsIncompatibleException(executablePath, $"the executable could not be launched ({ex.Message})");
        }
        catch (ProcessTimeoutException)
        {
            throw new FfmpegToolsIncompatibleException(executablePath, $"'-version' did not return within {VersionCheckTimeout}");
        }

        if (result.ExitCode != 0)
        {
            throw new FfmpegToolsIncompatibleException(executablePath, $"'-version' exited with code {result.ExitCode}");
        }

        if (!result.StandardOutput.StartsWith(expectedBannerPrefix, StringComparison.Ordinal))
        {
            throw new FfmpegToolsIncompatibleException(executablePath, $"unexpected '-version' output (expected it to start with '{expectedBannerPrefix}')");
        }
    }
}
