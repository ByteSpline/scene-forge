using SceneForge.Media.Detection;
using SceneForge.Media.Domain;
using SceneForge.Media.Processes;

namespace SceneForge.Media.Tests.Detection.Fixtures;

// One classifier's worth of ground truth within a generated clip: the type
// it should be detected as, and the exact [Start, End] window ffmpeg was
// told to place that transition at (not a single point - see
// TransitionDetection's remarks on why a transition interval is never
// collapsed to a boundary point).
internal sealed record ExpectedTransition(TransitionType Type, TimeRange Window);

internal sealed record SyntheticFixture(string FilePath, IReadOnlyList<ExpectedTransition> Expected);

// Builds small, deterministic synthetic clips with known-exact transition
// windows using only ffmpeg's own generator sources (testsrc2/smptebars/
// rgbtestsrc - no external assets) and filters whose timing parameters
// (xfade's duration/offset, fade's st/d, boxblur's enable=between(t,..))
// place the transition at an exact, ffmpeg-guaranteed point in the output
// timeline. This is what "known ground-truth window" means here: the
// window comes from the command line that generated the file, not from any
// estimate.
internal sealed class SyntheticVideoFixtureBuilder
{
    private const string SourceA = "testsrc2=size=160x90:rate=25";
    private const string SourceB = "smptebars=size=160x90:rate=25";
    private const string SourceC = "rgbtestsrc=size=160x90:rate=25";

    private static readonly TimeSpan ClipDuration = TimeSpan.FromSeconds(3.0);
    private static readonly TimeSpan TransitionOffset = TimeSpan.FromSeconds(1.0);
    private static readonly TimeSpan TransitionDuration = TimeSpan.FromSeconds(0.5);

    private readonly string _ffmpegPath;
    private readonly string _outputDirectory;
    private readonly ProcessRunner _processRunner = new();

    public SyntheticVideoFixtureBuilder(string ffmpegPath, string outputDirectory)
    {
        _ffmpegPath = ffmpegPath;
        _outputDirectory = outputDirectory;
        Directory.CreateDirectory(outputDirectory);
    }

    // Variant 0 uses testsrc2->smptebars content, variant 1 uses
    // smptebars->rgbtestsrc - two independent base-content pairs per type,
    // so classifier thresholds are never validated against only one video.
    public async Task<SyntheticFixture> BuildHardCutAsync(int variant, CancellationToken cancellationToken)
    {
        var (a, b) = SourcesForVariant(variant);
        var name = $"hardcut_v{variant}";
        var boundary = TransitionOffset;
        var window = new TimeRange(boundary, boundary);

        await RunFfmpegAsync(
            name,
            [
                "-f", "lavfi", "-t", Seconds(boundary), "-i", a,
                "-f", "lavfi", "-t", Seconds(ClipDuration - boundary), "-i", b,
                "-filter_complex", "[0:v][1:v]concat=n=2:v=1:a=0[v]",
                "-map", "[v]",
            ],
            cancellationToken).ConfigureAwait(false);

        return new SyntheticFixture(OutputPath(name), [new ExpectedTransition(TransitionType.HardCut, window)]);
    }

    public async Task<SyntheticFixture> BuildDissolveAsync(int variant, CancellationToken cancellationToken)
    {
        var (a, b) = SourcesForVariant(variant);
        var name = $"dissolve_v{variant}";
        var window = new TimeRange(TransitionOffset, TransitionOffset + TransitionDuration);

        await RunFfmpegAsync(
            name,
            [
                "-f", "lavfi", "-t", Seconds(ClipDuration), "-i", a,
                "-f", "lavfi", "-t", Seconds(ClipDuration), "-i", b,
                "-filter_complex", $"[0:v][1:v]xfade=transition=fade:duration={Seconds(TransitionDuration)}:offset={Seconds(TransitionOffset)}[v]",
                "-map", "[v]",
            ],
            cancellationToken).ConfigureAwait(false);

        return new SyntheticFixture(OutputPath(name), [new ExpectedTransition(TransitionType.Dissolve, window)]);
    }

    // One clip, two ground-truth windows: fade-out to black immediately
    // followed by fade-in from black.
    public async Task<SyntheticFixture> BuildFadeBlackPairAsync(int variant, CancellationToken cancellationToken)
    {
        var (a, _) = SourcesForVariant(variant);
        var name = $"fadeblack_v{variant}";
        var fadeOutStart = TransitionOffset;
        var fadeInStart = TransitionOffset + TransitionDuration;
        var toBlackWindow = new TimeRange(fadeOutStart, fadeInStart);
        var fromBlackWindow = new TimeRange(fadeInStart, fadeInStart + TransitionDuration);

        // `fade`'s "outside the ramp" behavior is NOT a passthrough of the
        // source by default - type=in holds the fade color for every frame
        // before its own `st`, and type=out holds the color for every frame
        // after `st+d`, regardless of any earlier filter in the chain. Left
        // unscoped, a following fade=in silently recolors everything before
        // its own start back to solid black, corrupting the un-faded
        // portion at the start of the clip. Scoping each fade to its own
        // exact [st, st+d) window via `enable='between(t,...)'` makes it a
        // true no-op passthrough outside that window, so the two fades
        // compose correctly into a single continuous ramp down and back up.
        await RunFfmpegAsync(
            name,
            [
                "-f", "lavfi", "-t", Seconds(ClipDuration), "-i", a,
                "-vf", $"fade=t=out:st={Seconds(fadeOutStart)}:d={Seconds(TransitionDuration)}:color=black:enable='between(t,{Seconds(fadeOutStart)},{Seconds(fadeInStart)})'," +
                       $"fade=t=in:st={Seconds(fadeInStart)}:d={Seconds(TransitionDuration)}:color=black:enable='between(t,{Seconds(fadeInStart)},{Seconds(fadeInStart + TransitionDuration)})'",
            ],
            cancellationToken).ConfigureAwait(false);

        return new SyntheticFixture(
            OutputPath(name),
            [
                new ExpectedTransition(TransitionType.FadeToBlack, toBlackWindow),
                new ExpectedTransition(TransitionType.FadeFromBlack, fromBlackWindow),
            ]);
    }

    public async Task<SyntheticFixture> BuildFlashAsync(int variant, CancellationToken cancellationToken)
    {
        var (a, _) = SourcesForVariant(variant);
        var name = $"flash_v{variant}";
        // A brief, near-instant ramp up (rampDuration) followed by a genuine
        // hold at solid white (holdDuration) - without a hold, two opposing
        // ramps meet at a single instant with no plateau at all, and a fast
        // sample rate can step right over that instant without ever
        // observing a fully-white frame. The hold must comfortably exceed
        // one sample interval (0.125s at the Accurate profile's 8fps) so at
        // least one sampled frame reliably lands on it.
        var rampDuration = TimeSpan.FromSeconds(0.03);
        var holdDuration = TimeSpan.FromSeconds(0.2);
        var flashStart = TransitionOffset;
        var holdStart = flashStart + rampDuration;
        var rampBackStart = holdStart + holdDuration;
        var flashEnd = rampBackStart + rampDuration;
        var window = new TimeRange(flashStart, flashEnd);

        await RunFfmpegAsync(
            name,
            [
                "-f", "lavfi", "-t", Seconds(ClipDuration), "-i", a,
                "-vf", $"fade=t=out:st={Seconds(flashStart)}:d={Seconds(rampDuration)}:color=white:enable='between(t,{Seconds(flashStart)},{Seconds(rampBackStart)})'," +
                       $"fade=t=in:st={Seconds(rampBackStart)}:d={Seconds(rampDuration)}:color=white:enable='between(t,{Seconds(rampBackStart)},{Seconds(flashEnd)})'",
            ],
            cancellationToken).ConfigureAwait(false);

        return new SyntheticFixture(OutputPath(name), [new ExpectedTransition(TransitionType.Flash, window)]);
    }

    public async Task<SyntheticFixture> BuildBlurTransitionAsync(int variant, CancellationToken cancellationToken)
    {
        var (a, _) = SourcesForVariant(variant);
        var name = $"blur_v{variant}";
        var window = new TimeRange(TransitionOffset, TransitionOffset + TransitionDuration);

        await RunFfmpegAsync(
            name,
            [
                "-f", "lavfi", "-t", Seconds(ClipDuration), "-i", a,
                "-vf", $"boxblur=12:2:enable='between(t,{Seconds(TransitionOffset)},{Seconds(TransitionOffset + TransitionDuration)})'",
            ],
            cancellationToken).ConfigureAwait(false);

        return new SyntheticFixture(OutputPath(name), [new ExpectedTransition(TransitionType.BlurTransition, window)]);
    }

    // xfade's "wipe*" transitions reveal the second clip through a hard
    // traveling edge - genuinely different content on each side of that
    // edge, not the same content translating - which dense optical flow
    // (built on brightness-constancy of *moving* content) cannot track as
    // motion at all. "slide*" instead visually translates the actual pixel
    // content across the frame, which is the real directional-swipe
    // signature this classifier is built to catch.
    public async Task<SyntheticFixture> BuildDirectionalSwipeAsync(int variant, CancellationToken cancellationToken)
    {
        var (a, b) = SourcesForVariant(variant);
        var transition = variant == 0 ? "slideleft" : "slideright";
        var name = $"swipe_v{variant}";
        var window = new TimeRange(TransitionOffset, TransitionOffset + TransitionDuration);

        await RunFfmpegAsync(
            name,
            [
                "-f", "lavfi", "-t", Seconds(ClipDuration), "-i", a,
                "-f", "lavfi", "-t", Seconds(ClipDuration), "-i", b,
                "-filter_complex", $"[0:v][1:v]xfade=transition={transition}:duration={Seconds(TransitionDuration)}:offset={Seconds(TransitionOffset)}[v]",
                "-map", "[v]",
            ],
            cancellationToken).ConfigureAwait(false);

        return new SyntheticFixture(OutputPath(name), [new ExpectedTransition(TransitionType.DirectionalSwipe, window)]);
    }

    // xfade's "circleopen"/"zoomin" transitions are reveal-through-a-mask
    // effects (like the wipes above) with little genuine optical-flow-
    // trackable motion. A real "zoom transition" edit (crash zoom / Ken
    // Burns push) scales the *existing* frame content outward from its
    // center, which is exactly what this scale+crop construction does: the
    // content genuinely enlarges and moves outward during [offset,
    // offset+duration), then holds at the enlarged scale - the actual
    // radial-flow signature ZoomTransitionClassifier looks for.
    public async Task<SyntheticFixture> BuildZoomTransitionAsync(int variant, CancellationToken cancellationToken)
    {
        var (a, _) = SourcesForVariant(variant);
        var name = $"zoom_v{variant}";
        var window = new TimeRange(TransitionOffset, TransitionOffset + TransitionDuration);
        var start = Seconds(TransitionOffset);
        var end = Seconds(TransitionOffset + TransitionDuration);
        var progress = $"min(1,max(0,(t-{start})/({end}-{start})))";
        var scaleFactor = $"(1+1.5*{progress})";

        await RunFfmpegAsync(
            name,
            [
                "-f", "lavfi", "-t", Seconds(ClipDuration), "-i", a,
                "-vf", $"scale=w='iw*{scaleFactor}':h='ih*{scaleFactor}':eval=frame,crop=160:90:(iw-160)/2:(ih-90)/2",
            ],
            cancellationToken).ConfigureAwait(false);

        return new SyntheticFixture(OutputPath(name), [new ExpectedTransition(TransitionType.ZoomTransition, window)]);
    }

    private static (string A, string B) SourcesForVariant(int variant) =>
        variant == 0 ? (SourceA, SourceB) : (SourceB, SourceC);

    private string OutputPath(string name) => Path.Combine(_outputDirectory, $"{name}.mp4");

    private async Task RunFfmpegAsync(string name, IReadOnlyList<string> filterArguments, CancellationToken cancellationToken)
    {
        var outputPath = OutputPath(name);
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        var arguments = new List<string> { "-y", "-hide_banner", "-loglevel", "error" };
        arguments.AddRange(filterArguments);
        arguments.AddRange(["-pix_fmt", "yuv420p", "-c:v", "libx264", "-preset", "ultrafast", outputPath]);

        var result = await _processRunner.RunAsync(
            new ProcessExecutionRequest
            {
                FileName = _ffmpegPath,
                Arguments = arguments,
                Timeout = TimeSpan.FromSeconds(30),
            },
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg fixture build '{name}' failed (exit {result.ExitCode}):\n{result.StandardError}");
        }
    }

    private static string Seconds(TimeSpan value) => value.TotalSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
}
