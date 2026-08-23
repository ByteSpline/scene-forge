using System.Globalization;
using SceneForge.Media.Probing;
using SceneForge.Media.Processes;
using SceneForge.Media.Tooling;

namespace SceneForge.Media.Rendering.Internal;

// Post-render verification against the rendered FILE itself - re-probes it
// with ffprobe (never trusts the render process's own exit code alone) and
// proves the first/middle/last samples actually decode, per the phase
// brief's literal requirement. Every check is reported, not just an
// aggregate boolean - see RenderVerificationResult.
internal sealed class RenderOutputVerifier
{
    private static readonly TimeSpan DecodeProbeTimeout = TimeSpan.FromSeconds(30);

    private readonly IFfprobeService _ffprobeService;
    private readonly IProcessRunner _processRunner;
    private readonly IFfmpegToolLocator _toolLocator;

    public RenderOutputVerifier(IFfprobeService ffprobeService, IProcessRunner processRunner, IFfmpegToolLocator toolLocator)
    {
        _ffprobeService = ffprobeService;
        _processRunner = processRunner;
        _toolLocator = toolLocator;
    }

    public async Task<RenderVerificationResult> VerifyAsync(string outputFilePath, RenderPlan plan, CancellationToken cancellationToken)
    {
        var mediaInfo = await _ffprobeService.ProbeAsync(outputFilePath, cancellationToken).ConfigureAwait(false);

        var expectedDuration = plan.PlannedVideoDuration;
        var actualDuration = mediaInfo.Duration;
        var delta = actualDuration - expectedDuration;
        var tolerance = plan.OutputSpec.FrameRate.FromFrameCount(1);

        var tools = await _toolLocator.LocateAsync(cancellationToken).ConfigureAwait(false);

        var firstFrameOk = await CanDecodeFrameAsync(tools.FfmpegPath, outputFilePath, TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
        var middleFrameOk = await CanDecodeFrameAsync(tools.FfmpegPath, outputFilePath, Halve(actualDuration), cancellationToken).ConfigureAwait(false);
        var lastFrameSeek = actualDuration - tolerance > TimeSpan.Zero ? actualDuration - tolerance : TimeSpan.Zero;
        var lastFrameOk = await CanDecodeFrameAsync(tools.FfmpegPath, outputFilePath, lastFrameSeek, cancellationToken).ConfigureAwait(false);

        return new RenderVerificationResult
        {
            HasExpectedVideoStream = mediaInfo.PrimaryVideoStream is not null,
            HasExactlyOneAudioStream = mediaInfo.AudioStreams.Count == 1,
            ExpectedDuration = expectedDuration,
            ActualDuration = actualDuration,
            DurationDelta = delta,
            DurationTolerance = tolerance,
            DurationWithinTolerance = delta.Duration() <= tolerance,
            FirstFrameDecodable = firstFrameOk,
            MiddleFrameDecodable = middleFrameOk,
            LastFrameDecodable = lastFrameOk,
        };
    }

    private static TimeSpan Halve(TimeSpan value) => TimeSpan.FromTicks(value.Ticks / 2);

    private async Task<bool> CanDecodeFrameAsync(string ffmpegPath, string filePath, TimeSpan seekTo, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _processRunner.RunAsync(
                new ProcessExecutionRequest
                {
                    FileName = ffmpegPath,
                    Arguments =
                    [
                        "-hide_banner", "-loglevel", "error",
                        "-ss", seekTo.TotalSeconds.ToString("0.######", CultureInfo.InvariantCulture),
                        "-i", filePath,
                        "-frames:v", "1",
                        "-f", "null", "-",
                    ],
                    Timeout = DecodeProbeTimeout,
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
