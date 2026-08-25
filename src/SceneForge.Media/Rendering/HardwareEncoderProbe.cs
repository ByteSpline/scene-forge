using SceneForge.Media.Processes;
using SceneForge.Media.Tooling;

namespace SceneForge.Media.Rendering;

// Detects a usable video encoder strictly by capability testing - actually
// launching ffmpeg with each candidate -c:v against a tiny synthetic
// lavfi source and checking it exits cleanly - never by inspecting the GPU
// vendor/model name (the phase brief's explicit requirement: "detect ...
// by capability testing, not GPU name assumptions"). A machine with no
// working hardware encoder simply fails every hardware candidate and falls
// through to libx264, which is itself still smoke-tested rather than
// assumed to work.
public sealed class HardwareEncoderProbe : IHardwareEncoderProbe
{
    private static readonly TimeSpan SmokeTestTimeout = TimeSpan.FromSeconds(15);

    // Priority order per the phase brief: NVENC, then Intel Quick Sync,
    // then AMD AMF, then the libx264 software fallback.
    private static readonly (VideoEncoderKind Kind, string FfmpegName, bool IsHardware)[] Candidates =
    [
        (VideoEncoderKind.NvidiaNvenc, "h264_nvenc", true),
        (VideoEncoderKind.IntelQuickSync, "h264_qsv", true),
        (VideoEncoderKind.AmdAmf, "h264_amf", true),
        (VideoEncoderKind.SoftwareX264, "libx264", false),
    ];

    private readonly IProcessRunner _processRunner;
    private readonly IFfmpegToolLocator _toolLocator;

    // Real candidate probing launches 1-4 real ffmpeg smoke-test processes;
    // FFmpegRenderService constructs one HardwareEncoderProbe per app
    // session (it is registered as a DI singleton), so caching the winning
    // selection here - rather than re-probing on every RenderAsync call -
    // turns "N renders in one session" into "1 probe, N renders" without
    // changing which encoder gets selected. A failed probe is deliberately
    // NOT cached (transient conditions - e.g. a GPU driver hang - might
    // resolve by the next render), only a successful one. The cache itself
    // is a plain lock (not a SemaphoreSlim - this class must stay
    // synchronous-construction/no-Dispose, matching every other stateless
    // service in this project); this app never runs two RenderAsync calls
    // concurrently, so the rare theoretical race (two truly concurrent
    // first callers both probing once before either publishes) only costs
    // a handful of duplicate smoke-test processes, never an incorrect
    // result - each caller always probes with, and is only ever cancelled
    // by, its own token.
    private readonly object _cacheGate = new();
    private VideoEncoderSelection? _cachedSelection;

    public HardwareEncoderProbe(IProcessRunner processRunner, IFfmpegToolLocator toolLocator)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(toolLocator);

        _processRunner = processRunner;
        _toolLocator = toolLocator;
    }

    public async Task<VideoEncoderSelection> SelectEncoderAsync(CancellationToken cancellationToken)
    {
        lock (_cacheGate)
        {
            if (_cachedSelection is { } cached)
            {
                return cached;
            }
        }

        var selection = await ProbeAsync(cancellationToken).ConfigureAwait(false);

        lock (_cacheGate)
        {
            _cachedSelection ??= selection;
            return _cachedSelection;
        }
    }

    private async Task<VideoEncoderSelection> ProbeAsync(CancellationToken cancellationToken)
    {
        var tools = await _toolLocator.LocateAsync(cancellationToken).ConfigureAwait(false);
        var failures = new List<string>();

        foreach (var candidate in Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await CanEncodeAsync(tools.FfmpegPath, candidate.FfmpegName, cancellationToken).ConfigureAwait(false))
            {
                return new VideoEncoderSelection
                {
                    Kind = candidate.Kind,
                    FfmpegEncoderName = candidate.FfmpegName,
                    IsHardwareAccelerated = candidate.IsHardware,
                };
            }

            failures.Add(candidate.FfmpegName);
        }

        throw new RenderExecutionException(
            $"No usable video encoder was found; every candidate failed its capability test: {string.Join(", ", failures)}.");
    }

    private async Task<bool> CanEncodeAsync(string ffmpegPath, string encoderName, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _processRunner.RunAsync(
                new ProcessExecutionRequest
                {
                    FileName = ffmpegPath,
                    Arguments =
                    [
                        "-hide_banner", "-loglevel", "error", "-y",
                        "-f", "lavfi", "-i", "color=c=black:s=64x64:r=25:d=0.2",
                        "-frames:v", "3",
                        "-c:v", encoderName,
                        "-pix_fmt", "yuv420p",
                        "-f", "null", "-",
                    ],
                    Timeout = SmokeTestTimeout,
                },
                cancellationToken).ConfigureAwait(false);

            return result.ExitCode == 0;
        }
        catch (ProcessLaunchException)
        {
            return false;
        }
        catch (ProcessTimeoutException)
        {
            return false;
        }
    }
}
