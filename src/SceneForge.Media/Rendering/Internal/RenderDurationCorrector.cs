using System.Globalization;
using SceneForge.Media.Processes;
using SceneForge.Media.Tooling;

namespace SceneForge.Media.Rendering.Internal;

// Last-resort, guaranteed-effective duration fix for an already-assembled
// render output that still misses RenderOutputVerifier's one-frame duration
// tolerance after both full-render retry tiers (see FFmpegRenderService's
// duration-only correction loop). Re-processes the existing output file
// in place: pads (if short - clones the trailing frame / adds silence)
// generously past the target and then trims (always) both streams down to
// the EXACT planned frame count / duration, so the result is correct by
// construction - the same "guaranteed effective, not hoped-for" principle
// RenderFilterGraphBuilder's own frame-domain trim already uses per segment,
// applied once more to the finished file instead of to every segment.
internal sealed class RenderDurationCorrector
{
    private readonly IProcessRunner _processRunner;
    private readonly IFfmpegToolLocator _toolLocator;

    public RenderDurationCorrector(IProcessRunner processRunner, IFfmpegToolLocator toolLocator)
    {
        _processRunner = processRunner;
        _toolLocator = toolLocator;
    }

    public async Task CorrectAsync(string outputFilePath, RenderPlan plan, VideoEncoderSelection encoder, CancellationToken cancellationToken)
    {
        var tools = await _toolLocator.LocateAsync(cancellationToken).ConfigureAwait(false);
        var spec = plan.OutputSpec;
        var frameRate = $"{spec.FrameRate.Numerator}/{spec.FrameRate.Denominator}";
        var frameCount = spec.FrameRate.ToFrameCount(plan.PlannedVideoDuration);
        var expectedSeconds = FormatSeconds(plan.PlannedVideoDuration);

        var correctedPath = BuildCorrectedPath(outputFilePath);

        // tpad pads by cloning the trailing frame up to (at least) the whole
        // target duration - a generous, always-sufficient headroom given the
        // input is already within a few frames of correct - then the
        // frame-domain trim cuts to EXACTLY frameCount regardless of whether
        // padding was actually needed (a no-op when it was not). Same
        // guaranteed-by-construction shape for audio: apad's whole_dur pads
        // with silence up to the target if short (a no-op if not), then
        // atrim cuts to exactly the target duration.
        var videoFilter = FormattableString.Invariant(
            $"fps={frameRate},tpad=stop_mode=clone:stop_duration={expectedSeconds},trim=start_frame=0:end_frame={frameCount},setpts=PTS-STARTPTS");
        var audioFilter = FormattableString.Invariant(
            $"apad=whole_dur={expectedSeconds},atrim=duration={expectedSeconds},asetpts=PTS-STARTPTS");

        var arguments = new List<string>
        {
            "-hide_banner", "-y", "-loglevel", "error",
            "-i", outputFilePath,
            "-vf", videoFilter,
            "-af", audioFilter,
            "-c:v", encoder.FfmpegEncoderName,
        };
        arguments.AddRange(EncoderQualityDefaults.For(encoder.Kind));
        arguments.AddRange(["-pix_fmt", spec.PixelFormat, "-r", frameRate]);
        arguments.AddRange(["-c:a", plan.Audio.Codec, "-ar", plan.Audio.SampleRateHz.ToString(CultureInfo.InvariantCulture), "-ac", plan.Audio.Channels.ToString(CultureInfo.InvariantCulture)]);
        if (plan.Audio.BitRateBitsPerSecond is { } bitRate)
        {
            arguments.AddRange(["-b:a", $"{bitRate}"]);
        }

        arguments.AddRange(["-movflags", "+faststart", correctedPath]);

        try
        {
            var result = await _processRunner.RunAsync(
                new ProcessExecutionRequest { FileName = tools.FfmpegPath, Arguments = arguments },
                cancellationToken).ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                throw new RenderExecutionException(
                    $"The frame-exact duration correction pass failed (exit code {result.ExitCode}): {Excerpt(result.StandardError)}");
            }

            // Single-call replace (MOVEFILE_REPLACE_EXISTING) rather than
            // File.Delete followed by File.Move: a delete that succeeds and
            // a move that then fails (a transient scanner/handle race on the
            // output path is a well-known Windows failure mode) would leave
            // the user with NEITHER the corrected copy NOR the already-valid
            // render - the exact "a valid render must always end in a
            // successful output" guarantee this whole loop exists to keep.
            // If the replace itself fails, outputFilePath still holds the
            // original render (content-valid, only its duration off) and the
            // finally below cleans up the corrected copy.
            File.Move(correctedPath, outputFilePath, overwrite: true);
        }
        finally
        {
            TryDeleteFile(correctedPath);
        }
    }

    private static string BuildCorrectedPath(string outputFilePath)
    {
        var directory = Path.GetDirectoryName(outputFilePath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(outputFilePath);
        var extension = Path.GetExtension(outputFilePath);
        return Path.Combine(directory, $"{name}.duration-corrected-{Guid.NewGuid():N}{extension}");
    }

    private static string FormatSeconds(TimeSpan value) => value.TotalSeconds.ToString("0.######", CultureInfo.InvariantCulture);

    private static string Excerpt(string standardError)
    {
        const int maxLength = 2000;
        return standardError.Length <= maxLength ? standardError : standardError[^maxLength..];
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of the transient corrected-copy file; not
            // user data, and the outputFilePath swap above has already
            // either succeeded (this is a leftover temp file) or thrown
            // (this is a failed attempt) by the time this runs.
        }
        catch (UnauthorizedAccessException)
        {
            // As above.
        }
    }
}
