using System.Diagnostics;
using System.Globalization;
using SceneForge.Media.Probing;
using SceneForge.Media.Processes;
using SceneForge.Media.Rendering.Internal;
using SceneForge.Media.Tooling;
using SceneForge.Media.Validation;

namespace SceneForge.Media.Rendering;

// Renders a RenderPlan to a concrete file: selects an encoder by capability
// testing (never a GPU name lookup - see HardwareEncoderProbe), builds one
// filter_complex graph that trims/normalizes/concatenates every segment and
// mutes/replaces the audio track in a single ffmpeg invocation (falling
// back to a temporary filter script file when the graph would be too long
// for a safe Windows command line - see BuildFilterArguments), streams
// machine-readable progress from ffmpeg's own '-progress' output, and
// verifies the result via RenderOutputVerifier before returning.
public sealed class FFmpegRenderService : IFFmpegRenderService
{
    // ffmpeg's own command line is invoked without a shell (ProcessRunner
    // always uses ArgumentList, never string concatenation), so the binding
    // constraint is Win32 CreateProcess's ~32,767 wide-character total
    // command line limit, not cmd.exe's much smaller 8,191 limit. This
    // threshold on the filter_complex string ALONE (the dominant
    // contributor once segment counts grow) leaves generous headroom for
    // both file paths, the encoder/audio arguments, and process overhead -
    // the bounded intermediate strategy the phase brief asks for is to
    // write the graph to a temporary script file (deleted after the
    // process exits, always, via the finally block below) and pass
    // '-filter_complex_script' instead, once the inline graph would risk
    // that limit.
    private const int InlineFilterGraphCharacterThreshold = 6_000;

    private readonly IProcessRunner _processRunner;
    private readonly IFfmpegToolLocator _toolLocator;
    private readonly IHardwareEncoderProbe _encoderProbe;
    private readonly RenderOutputVerifier _verifier;

    public FFmpegRenderService(IProcessRunner processRunner, IFfmpegToolLocator toolLocator, IFfprobeService ffprobeService)
        : this(processRunner, toolLocator, new HardwareEncoderProbe(processRunner, toolLocator), new RenderOutputVerifier(ffprobeService, processRunner, toolLocator))
    {
    }

    internal FFmpegRenderService(
        IProcessRunner processRunner,
        IFfmpegToolLocator toolLocator,
        IHardwareEncoderProbe encoderProbe,
        RenderOutputVerifier verifier)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(toolLocator);
        ArgumentNullException.ThrowIfNull(encoderProbe);
        ArgumentNullException.ThrowIfNull(verifier);

        _processRunner = processRunner;
        _toolLocator = toolLocator;
        _encoderProbe = encoderProbe;
        _verifier = verifier;
    }

    public async Task<RenderResult> RenderAsync(
        RenderPlan plan,
        string outputFilePath,
        IProgress<RenderProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (string.IsNullOrWhiteSpace(outputFilePath))
        {
            throw new ArgumentException("An output file path is required.", nameof(outputFilePath));
        }

        var outputDirectory = OutputDirectoryValidator.EnsureWritable(Path.GetDirectoryName(Path.GetFullPath(outputFilePath)) ?? ".");
        var resolvedOutputPath = Path.Combine(outputDirectory, Path.GetFileName(outputFilePath));
        OutputDirectoryValidator.EnsureDoesNotOverwriteInput(resolvedOutputPath, plan.SourceFilePath);
        OutputDirectoryValidator.EnsureDoesNotOverwriteInput(resolvedOutputPath, plan.Audio.FilePath);

        var tools = await _toolLocator.LocateAsync(cancellationToken).ConfigureAwait(false);
        var encoder = await _encoderProbe.SelectEncoderAsync(cancellationToken).ConfigureAwait(false);

        var stopwatch = Stopwatch.StartNew();
        var (fellBack, usedEncoder) = await RunWithFallbackAsync(tools.FfmpegPath, plan, resolvedOutputPath, encoder, progress, stopwatch, cancellationToken)
            .ConfigureAwait(false);
        stopwatch.Stop();

        var verification = await _verifier.VerifyAsync(resolvedOutputPath, plan, cancellationToken).ConfigureAwait(false);
        if (!verification.IsValid)
        {
            throw new RenderVerificationException(verification);
        }

        return new RenderResult
        {
            OutputFilePath = resolvedOutputPath,
            Encoder = usedEncoder,
            FellBackToSoftwareEncoder = fellBack,
            Elapsed = stopwatch.Elapsed,
            Verification = verification,
        };
    }

    private async Task<(bool FellBack, VideoEncoderSelection Encoder)> RunWithFallbackAsync(
        string ffmpegPath,
        RenderPlan plan,
        string outputFilePath,
        VideoEncoderSelection encoder,
        IProgress<RenderProgress>? progress,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var attemptResult = await TryRenderOnceAsync(ffmpegPath, plan, outputFilePath, encoder, progress, stopwatch, cancellationToken).ConfigureAwait(false);
        if (attemptResult.Success)
        {
            return (false, encoder);
        }

        if (!encoder.IsHardwareAccelerated)
        {
            throw new RenderExecutionException(
                $"ffmpeg render with encoder '{encoder.FfmpegEncoderName}' failed (exit code {attemptResult.ExitCode}): {attemptResult.StandardErrorExcerpt}");
        }

        // Hardware output must be validated and fall back safely: the
        // encoder passed its own short capability smoke test, but a real,
        // full-length render can still fail (unsupported resolution/
        // profile, driver contention, VRAM exhaustion). Retry once, in
        // full, with the always-available software encoder rather than
        // surfacing a hardware-specific failure to the caller.
        var softwareEncoder = new VideoEncoderSelection
        {
            Kind = VideoEncoderKind.SoftwareX264,
            FfmpegEncoderName = "libx264",
            IsHardwareAccelerated = false,
        };

        var fallbackResult = await TryRenderOnceAsync(ffmpegPath, plan, outputFilePath, softwareEncoder, progress, stopwatch, cancellationToken).ConfigureAwait(false);
        if (!fallbackResult.Success)
        {
            throw new RenderExecutionException(
                $"ffmpeg render failed with hardware encoder '{encoder.FfmpegEncoderName}' (exit code {attemptResult.ExitCode}: {attemptResult.StandardErrorExcerpt}) " +
                $"and the libx264 fallback also failed (exit code {fallbackResult.ExitCode}: {fallbackResult.StandardErrorExcerpt}).");
        }

        return (true, softwareEncoder);
    }

    private async Task<(bool Success, int ExitCode, string StandardErrorExcerpt)> TryRenderOnceAsync(
        string ffmpegPath,
        RenderPlan plan,
        string outputFilePath,
        VideoEncoderSelection encoder,
        IProgress<RenderProgress>? progress,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        string? scriptFilePath = null;
        try
        {
            var filterGraph = RenderFilterGraphBuilder.Build(plan);
            var filterArguments = BuildFilterArguments(filterGraph, out scriptFilePath);
            var arguments = BuildArguments(plan, outputFilePath, encoder, filterArguments);

            var parser = new RenderProgressParser();
            var outputProgress = progress is null
                ? null
                : new SynchronousProgress<ProcessOutputLine>(line =>
                {
                    if (line.Channel != ProcessOutputChannel.StandardOutput)
                    {
                        return;
                    }

                    var update = parser.Accept(line.Text, stopwatch.Elapsed);
                    if (update is null)
                    {
                        return;
                    }

                    progress.Report(WithEstimatedTimeRemaining(update, plan.PlannedVideoDuration));
                });

            var result = await _processRunner.RunAsync(
                new ProcessExecutionRequest
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    OutputProgress = outputProgress,
                },
                cancellationToken).ConfigureAwait(false);

            return (result.ExitCode == 0, result.ExitCode, Excerpt(result.StandardError));
        }
        finally
        {
            if (scriptFilePath is not null)
            {
                TryDeleteFile(scriptFilePath);
            }
        }
    }

    private static RenderProgress WithEstimatedTimeRemaining(RenderProgress update, TimeSpan plannedDuration)
    {
        if (update.Speed is not > 0)
        {
            return update;
        }

        var remainingOutput = plannedDuration - update.OutTime;
        if (remainingOutput <= TimeSpan.Zero)
        {
            return update with { EstimatedTimeRemaining = TimeSpan.Zero };
        }

        var eta = TimeSpan.FromSeconds(remainingOutput.TotalSeconds / update.Speed.Value);
        return update with { EstimatedTimeRemaining = eta };
    }

    private static string[] BuildFilterArguments(string filterGraph, out string? scriptFilePath)
    {
        if (filterGraph.Length <= InlineFilterGraphCharacterThreshold)
        {
            scriptFilePath = null;
            return ["-filter_complex", filterGraph];
        }

        var directory = Path.Combine(Path.GetTempPath(), "SceneForge", "render-filters");
        Directory.CreateDirectory(directory);
        scriptFilePath = Path.Combine(directory, $"{Guid.NewGuid():N}.filter");
        File.WriteAllText(scriptFilePath, filterGraph);
        return ["-filter_complex_script", scriptFilePath];
    }

    private static List<string> BuildArguments(
        RenderPlan plan,
        string outputFilePath,
        VideoEncoderSelection encoder,
        IReadOnlyList<string> filterArguments)
    {
        var spec = plan.OutputSpec;
        var frameRate = $"{spec.FrameRate.Numerator}/{spec.FrameRate.Denominator}";

        var arguments = new List<string>
        {
            "-hide_banner", "-y", "-loglevel", "error",
            "-i", plan.SourceFilePath,
            "-i", plan.Audio.FilePath,
        };

        arguments.AddRange(filterArguments);
        arguments.AddRange(["-map", RenderFilterGraphBuilder.VideoOutputLabel, "-map", RenderFilterGraphBuilder.AudioOutputLabel]);
        arguments.AddRange(["-c:v", encoder.FfmpegEncoderName]);
        arguments.AddRange(EncoderQualityArguments(encoder.Kind));
        arguments.AddRange(["-pix_fmt", spec.PixelFormat, "-r", frameRate]);
        arguments.AddRange(["-c:a", plan.Audio.Codec, "-ar", plan.Audio.SampleRateHz.ToString(CultureInfo.InvariantCulture), "-ac", plan.Audio.Channels.ToString(CultureInfo.InvariantCulture)]);
        if (plan.Audio.BitRateBitsPerSecond is { } bitRate)
        {
            arguments.AddRange(["-b:a", $"{bitRate}"]);
        }

        arguments.AddRange(["-movflags", "+faststart", "-progress", "pipe:1", "-nostats", outputFilePath]);
        return arguments;
    }

    // Conservative, documented-heuristic defaults per encoder - not tuned or
    // benchmarked against measured quality/size targets (CLAUDE.md rule 9
    // applies to optimizations; no baseline exists yet for encoder quality
    // tuning, see docs/PHASE_09_REPORT.md Outstanding).
    private static IReadOnlyList<string> EncoderQualityArguments(VideoEncoderKind kind) => kind switch
    {
        VideoEncoderKind.NvidiaNvenc => ["-preset", "p4", "-rc", "vbr", "-cq", "20"],
        VideoEncoderKind.IntelQuickSync => ["-preset", "medium", "-global_quality", "20"],
        VideoEncoderKind.AmdAmf => ["-quality", "balanced", "-rc", "cqp", "-qp_i", "20", "-qp_p", "20"],
        _ => ["-preset", "medium", "-crf", "20"],
    };

    private static string Excerpt(string standardError)
    {
        const int maxLength = 2000;
        return standardError.Length <= maxLength ? standardError : standardError[^maxLength..];
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup; a leftover temp filter script under the OS temp
            // directory is not user data and will be cleared on the next reboot.
        }
        catch (UnauthorizedAccessException)
        {
            // As above.
        }
    }
}
