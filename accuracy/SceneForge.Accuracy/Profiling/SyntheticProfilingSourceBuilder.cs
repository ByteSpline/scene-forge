using System.Globalization;
using SceneForge.Media.Processes;

namespace SceneForge.Accuracy.Profiling;

// Builds one ~30-minute, 1920x1080 synthetic source for full-pipeline
// throughput/CPU/memory/disk profiling - deliberately larger-scale and
// disclosed-synthetic content, not real footage, matching the same honesty
// convention as SyntheticFixtureCatalog (whose 32 fixtures this profiling
// source is not part of - it carries no committed ground truth and is never
// used for accuracy scoring, only resource/throughput measurement; accuracy
// before/after comparison still runs against the existing fixture matrix).
//
// Content is deliberately mixed rather than one static pattern, so Detection
// actually has real work to do across the file: alternating
// low-motion/high-motion segments joined by ordinary hard cuts, plus one
// fade-to-black, one fade-from-black, and one dissolve - the same
// transition-shape vocabulary as the fixture matrix, just at realistic scale
// and resolution. The "motion" segment pans a crop window across a
// larger-than-output testsrc2 canvas rather than using a per-pixel-computed
// generator like ffmpeg's mandelbrot/life lavfi sources - both were measured
// (directly, standalone) at well under 1x realtime encode speed at 1080p
// (mandelbrot ~0.4x, life ~1.2x), risking the per-segment ffmpeg timeout on
// a slower machine; a panning crop still gives genuine continuous per-pixel
// motion (exercising the optical-flow signal's real cost) at the same cheap
// encode speed as a static pattern. Built as several independently-encoded
// segments concatenated losslessly via ffmpeg's concat demuxer (`-c copy`,
// no re-encode of the whole file), which is far cheaper and more robust
// than one giant filter_complex graph at this duration.
//
// Cached on disk (never committed - 30 minutes of 1080p is large) and
// reused across repeated profiling runs unless the caller forces a rebuild,
// since generating it is itself a multi-minute ffmpeg encode.
public sealed class SyntheticProfilingSourceBuilder
{
    public const int Width = 1920;
    public const int Height = 1080;

    private static readonly TimeSpan SegmentDuration = TimeSpan.FromSeconds(300);
    private static readonly TimeSpan FadeDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DissolveHalfDuration = TimeSpan.FromSeconds(150);
    private static readonly TimeSpan DissolveOverlap = TimeSpan.FromSeconds(3);

    // Cheap continuous motion: pans a 1920x1080 window across a larger,
    // still-cheap-to-render canvas (see the class remarks above for why this
    // replaces a per-pixel-computed generator). Bounds ((canvas - output) /
    // 2) +/- the same half-extent keep the crop window fully inside the
    // canvas at every t.
    private const string PanningCropFilter = "crop=1920:1080:x='(320+320*sin(2*PI*t/60))':y='(180+180*cos(2*PI*t/90))'";

    // Generous relative to the slowest segment measured directly on dev
    // hardware (~1.2x realtime for the densest content actually used here),
    // so a slower CI/dev machine doesn't turn a genuine still-in-progress
    // encode into a spurious ProcessTimeoutException - which, since it
    // derives from OperationCanceledException, would otherwise be
    // indistinguishable from a real Ctrl+C in this tool's own exit
    // reporting (see ExitCodeMapper).
    private static readonly TimeSpan SegmentEncodeTimeout = TimeSpan.FromMinutes(15);

    private readonly string _ffmpegPath;
    private readonly ProcessRunner _processRunner = new();

    public SyntheticProfilingSourceBuilder(string ffmpegPath)
    {
        _ffmpegPath = ffmpegPath;
    }

    // Total planned duration if every segment encodes to exactly its
    // requested length (real ffmpeg output can differ by a frame or two;
    // callers needing the exact figure should ffprobe the built file).
    public static readonly TimeSpan PlannedDuration = (SegmentDuration * 4) + (DissolveHalfDuration * 2) - DissolveOverlap;

    // Returns the cached file's path, building it first if it doesn't
    // already exist or forceRebuild is set. Never overwrites a source the
    // caller passed in some other way (CLAUDE.md rule 11/12 concerns user
    // input files, not this tool's own generated scratch artifact, but the
    // same "never silently clobber" posture is kept here too).
    public async Task<string> BuildAsync(string outputPath, bool forceRebuild, CancellationToken cancellationToken)
    {
        if (File.Exists(outputPath) && !forceRebuild)
        {
            return outputPath;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".";
        Directory.CreateDirectory(directory);

        var workingDirectory = Directory.CreateTempSubdirectory("sceneforge-profiling-source");
        try
        {
            var segments = new List<string>
            {
                await EncodeSegmentAsync(workingDirectory.FullName, "01_static", "testsrc2=size=1920x1080:rate=25", SegmentDuration, fade: null, extraFilter: null, cancellationToken).ConfigureAwait(false),
                await EncodeSegmentAsync(workingDirectory.FullName, "02_motion", "testsrc2=size=2560x1440:rate=25", SegmentDuration, fade: null, extraFilter: PanningCropFilter, cancellationToken).ConfigureAwait(false),
                await EncodeSegmentAsync(workingDirectory.FullName, "03_fadeout", "smptebars=size=1920x1080:rate=25", SegmentDuration, fade: FadeKind.Out, extraFilter: null, cancellationToken).ConfigureAwait(false),
                await EncodeSegmentAsync(workingDirectory.FullName, "04_fadein", "rgbtestsrc=size=2560x1440:rate=25", SegmentDuration, fade: FadeKind.In, extraFilter: PanningCropFilter, cancellationToken).ConfigureAwait(false),
                await EncodeDissolveAsync(workingDirectory.FullName, cancellationToken).ConfigureAwait(false),
            };

            await ConcatAsync(segments, outputPath, workingDirectory.FullName, cancellationToken).ConfigureAwait(false);
            return outputPath;
        }
        finally
        {
            workingDirectory.Delete(recursive: true);
        }
    }

    private async Task<string> EncodeSegmentAsync(string workingDirectory, string id, string lavfiSource, TimeSpan duration, FadeKind? fade, string? extraFilter, CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(workingDirectory, $"{id}.ts");
        var fadeFilter = fade switch
        {
            FadeKind.Out => $"fade=t=out:st={Seconds(duration - FadeDuration)}:d={Seconds(FadeDuration)}:color=black",
            FadeKind.In => $"fade=t=in:st=0:d={Seconds(FadeDuration)}:color=black",
            _ => null,
        };
        var filter = string.Join(',', new[] { extraFilter, fadeFilter }.Where(f => f is not null));

        var arguments = new List<string> { "-y", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-t", Seconds(duration), "-i", lavfiSource };
        if (filter.Length > 0)
        {
            arguments.AddRange(["-vf", filter]);
        }

        arguments.AddRange(["-pix_fmt", "yuv420p", "-c:v", "libx264", "-preset", "ultrafast", "-f", "mpegts", outputPath]);
        await RunFfmpegAsync(id, arguments, SegmentEncodeTimeout, cancellationToken).ConfigureAwait(false);
        return outputPath;
    }

    // One dissolve (xfade) between two half-length inputs, same technique
    // as SyntheticFixtureCatalog.BuildDissolveAsync, scaled up to
    // DissolveHalfDuration each.
    private async Task<string> EncodeDissolveAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(workingDirectory, "05_dissolve.ts");
        var offset = DissolveHalfDuration - DissolveOverlap;

        var arguments = new List<string>
        {
            "-y", "-hide_banner", "-loglevel", "error",
            "-f", "lavfi", "-t", Seconds(DissolveHalfDuration), "-i", "rgbtestsrc=size=1920x1080:rate=25",
            "-f", "lavfi", "-t", Seconds(DissolveHalfDuration), "-i", "pal75bars=size=1920x1080:rate=25",
            "-filter_complex", $"[0:v][1:v]xfade=transition=fade:duration={Seconds(DissolveOverlap)}:offset={Seconds(offset)}[v]",
            "-map", "[v]",
            "-pix_fmt", "yuv420p", "-c:v", "libx264", "-preset", "ultrafast", "-f", "mpegts", outputPath,
        };
        await RunFfmpegAsync("05_dissolve", arguments, SegmentEncodeTimeout, cancellationToken).ConfigureAwait(false);
        return outputPath;
    }

    private async Task ConcatAsync(IReadOnlyList<string> segments, string outputPath, string workingDirectory, CancellationToken cancellationToken)
    {
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        var listPath = Path.Combine(workingDirectory, "concat.txt");
        var listContents = string.Join('\n', segments.Select(s => $"file '{s.Replace("'", "'\\''")}'"));
        await File.WriteAllTextAsync(listPath, listContents, cancellationToken).ConfigureAwait(false);

        var arguments = new List<string>
        {
            "-y", "-hide_banner", "-loglevel", "error",
            "-f", "concat", "-safe", "0", "-i", listPath,
            "-c", "copy", outputPath,
        };
        await RunFfmpegAsync("concat", arguments, TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
    }

    private async Task RunFfmpegAsync(string id, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(
            new ProcessExecutionRequest
            {
                FileName = _ffmpegPath,
                Arguments = arguments,
                Timeout = timeout,
            },
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg profiling-source build step '{id}' failed (exit {result.ExitCode}):\n{result.StandardError}");
        }
    }

    private static string Seconds(TimeSpan value) => value.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture);

    private enum FadeKind
    {
        In,
        Out,
    }
}
