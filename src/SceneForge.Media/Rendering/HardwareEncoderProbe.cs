using System.Globalization;
using SceneForge.Core.Resources;
using SceneForge.Media.Processes;
using SceneForge.Media.Rendering.Internal;
using SceneForge.Media.Tooling;

namespace SceneForge.Media.Rendering;

// Detects a usable video encoder strictly by capability testing - actually
// launching ffmpeg with each candidate -c:v against a small synthetic
// lavfi source and checking it exits cleanly - never by inspecting the GPU
// vendor/model name (the phase brief's explicit requirement: "detect ...
// by capability testing, not GPU name assumptions"). A machine with no
// working hardware encoder simply fails every hardware candidate and falls
// through to a software encoder, which is itself still smoke-tested rather
// than assumed to work.
public sealed class HardwareEncoderProbe : IHardwareEncoderProbe
{
    private static readonly TimeSpan SmokeTestTimeout = TimeSpan.FromSeconds(15);

    // The smoke test encodes a few frames of this synthetic clip. 320x240
    // (not a tiny 64x64) clears every current hardware encoder's minimum
    // supported dimensions - NVENC H.264 in particular rejects very small
    // resolutions outright, which would make a machine WITH a working NVENC
    // fail the probe and silently fall back to software. Small enough that
    // the whole test is still well under a second on any encoder.
    private const string SmokeTestSource = "color=c=black:s=320x240:r=25:d=0.4";
    private const string SmokeTestFrameCount = "5";

    // Priority order per the phase brief: NVENC, then Intel Quick Sync,
    // then AMD AMF, then the software fallbacks. libx264 is preferred among
    // the software encoders (better quality/speed) but SceneForge's vendored
    // ffmpeg is built --disable-libx264, so libopenh264 is listed after it
    // as the always-present software H.264 encoder.
    private static readonly IReadOnlyList<EncoderCandidate> Candidates =
    [
        new(VideoEncoderKind.NvidiaNvenc, "h264_nvenc", IsHardware: true),
        new(VideoEncoderKind.IntelQuickSync, "h264_qsv", IsHardware: true),
        new(VideoEncoderKind.AmdAmf, "h264_amf", IsHardware: true),
        new(VideoEncoderKind.SoftwareX264, "libx264", IsHardware: false),
        new(VideoEncoderKind.SoftwareOpenH264, "libopenh264", IsHardware: false),
    ];

    private readonly IProcessRunner _processRunner;
    private readonly IFfmpegToolLocator _toolLocator;
    private readonly IAdaptiveResourceGovernor _resourceGovernor;

    // Real candidate probing launches real ffmpeg smoke-test processes;
    // FFmpegRenderService constructs one HardwareEncoderProbe per app
    // session (it is registered as a DI singleton), so caching the winning
    // selection here - rather than re-probing on every RenderAsync call -
    // turns "N renders in one session" into "1 probe, N renders" without
    // changing which encoder gets selected. A failed probe is deliberately
    // NOT cached (transient conditions - e.g. a GPU driver hang - might
    // resolve by the next render), only a successful one. The
    // hardware-or-better selection and the software-only selection are
    // cached separately. The cache itself is a plain lock (not a
    // SemaphoreSlim - this class must stay synchronous-construction/no-
    // Dispose, matching every other stateless service in this project);
    // this app never runs two RenderAsync calls concurrently, so the rare
    // theoretical race (two truly concurrent first callers both probing once
    // before either publishes) only costs a handful of duplicate smoke-test
    // processes, never an incorrect result - each caller always probes with,
    // and is only ever cancelled by, its own token.
    private readonly object _cacheGate = new();
    private VideoEncoderSelection? _cachedSelection;
    private VideoEncoderSelection? _cachedSoftwareSelection;

    public HardwareEncoderProbe(IProcessRunner processRunner, IFfmpegToolLocator toolLocator, IAdaptiveResourceGovernor resourceGovernor)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(toolLocator);
        ArgumentNullException.ThrowIfNull(resourceGovernor);

        _processRunner = processRunner;
        _toolLocator = toolLocator;
        _resourceGovernor = resourceGovernor;
    }

    public Task<VideoEncoderSelection> SelectEncoderAsync(CancellationToken cancellationToken) =>
        SelectAsync(includeHardware: true, cancellationToken);

    public Task<VideoEncoderSelection> SelectSoftwareEncoderAsync(CancellationToken cancellationToken) =>
        SelectAsync(includeHardware: false, cancellationToken);

    private async Task<VideoEncoderSelection> SelectAsync(bool includeHardware, CancellationToken cancellationToken)
    {
        lock (_cacheGate)
        {
            var cached = includeHardware ? _cachedSelection : _cachedSoftwareSelection;
            if (cached is { } hit)
            {
                return hit;
            }
        }

        var selection = await ProbeAsync(includeHardware, cancellationToken).ConfigureAwait(false);

        lock (_cacheGate)
        {
            if (includeHardware)
            {
                _cachedSelection ??= selection;
                return _cachedSelection;
            }

            _cachedSoftwareSelection ??= selection;
            return _cachedSoftwareSelection;
        }
    }

    private async Task<VideoEncoderSelection> ProbeAsync(bool includeHardware, CancellationToken cancellationToken)
    {
        var tools = await _toolLocator.LocateAsync(cancellationToken).ConfigureAwait(false);
        var failures = new List<string>();

        foreach (var candidate in Candidates)
        {
            if (!includeHardware && candidate.IsHardware)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (await CanEncodeAsync(tools.FfmpegPath, candidate, cancellationToken).ConfigureAwait(false))
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

        var scope = includeHardware ? "video encoder" : "software video encoder";
        throw new RenderExecutionException(
            $"No usable {scope} was found; every candidate failed its capability test: {string.Join(", ", failures)}.");
    }

    private async Task<bool> CanEncodeAsync(string ffmpegPath, EncoderCandidate candidate, CancellationToken cancellationToken)
    {
        try
        {
            var arguments = new List<string>
            {
                "-hide_banner", "-loglevel", "error", "-y",
                "-threads", _resourceGovernor.MaxWorkers.ToString(CultureInfo.InvariantCulture),
                "-f", "lavfi", "-i", SmokeTestSource,
                "-frames:v", SmokeTestFrameCount,
                "-c:v", candidate.FfmpegName,
            };

            // Probe with the same quality arguments a real render uses, so a
            // candidate whose bare -c:v launches but rejects our actual
            // preset/rate-control settings is caught here, not mid-render.
            arguments.AddRange(EncoderQualityDefaults.For(candidate.Kind));
            arguments.AddRange(["-pix_fmt", "yuv420p", "-f", "null", "-"]);

            var result = await _processRunner.RunAsync(
                new ProcessExecutionRequest
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
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

    private readonly record struct EncoderCandidate(VideoEncoderKind Kind, string FfmpegName, bool IsHardware);
}
