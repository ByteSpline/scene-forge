using System.Globalization;
using SceneForge.Media.Detection;
using SceneForge.Media.Domain;
using SceneForge.Media.Processes;

namespace SceneForge.Accuracy.Fixtures;

// Builds small, deterministic synthetic clips with known-exact ground truth
// using only ffmpeg's own generator sources (testsrc2/smptebars/rgbtestsrc/
// pal75bars/color - no external assets) and filters whose timing parameters
// place transitions (or their deliberate absence) at an exact,
// ffmpeg-guaranteed point in the output timeline. Supersedes the narrower
// SyntheticVideoFixtureBuilder that used to live in
// tests/SceneForge.Media.Tests/Detection/Fixtures - this is now the single
// source of truth for both the xunit fixture-matrix test and the
// accuracy/benchmark console tool.
//
// Every fixture carries exactly one FixtureGroup, and every expected
// transition inside a fixture shares that fixture's own TransitionType (or
// the fixture has none at all, for distractors). This is a deliberate
// simplification versus an earlier design that let one fixture span two
// groups (e.g. a single clip with both a fade-to-black and a fade-from-
// black window): splitting fade-to-black and fade-from-black into two
// separate one-transition fixtures makes false-positive attribution
// unambiguous everywhere downstream (a stray detection while analyzing a
// fixture always belongs to that fixture's one Group), at the cost of one
// extra cheap ffmpeg encode.
public sealed class SyntheticFixtureCatalog
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

    public SyntheticFixtureCatalog(string ffmpegPath, string outputDirectory)
    {
        _ffmpegPath = ffmpegPath;
        _outputDirectory = outputDirectory;
        Directory.CreateDirectory(outputDirectory);
    }

    // The full matrix: 2 independent-content variants of each of the 8
    // transition groups, 2 variants of each of the 4 distractor groups, 2
    // variants of the variable-frame-rate hard cut, 3 resolutions and 3
    // rotations of the mixed-resolution/rotated hard cut - roughly 32
    // fixtures total, each a few seconds of 160x90-scale video.
    public async Task<IReadOnlyList<SyntheticFixture>> BuildAllAsync(CancellationToken cancellationToken)
    {
        var fixtures = new List<SyntheticFixture>();
        for (var variant = 0; variant < 2; variant++)
        {
            fixtures.Add(await BuildHardCutAsync(variant, cancellationToken).ConfigureAwait(false));
            fixtures.Add(await BuildFadeToBlackAsync(variant, cancellationToken).ConfigureAwait(false));
            fixtures.Add(await BuildFadeFromBlackAsync(variant, cancellationToken).ConfigureAwait(false));
            fixtures.Add(await BuildDissolveAsync(variant, cancellationToken).ConfigureAwait(false));
            fixtures.Add(await BuildFlashAsync(variant, cancellationToken).ConfigureAwait(false));
            fixtures.Add(await BuildBlurTransitionAsync(variant, cancellationToken).ConfigureAwait(false));
            fixtures.Add(await BuildZoomTransitionAsync(variant, cancellationToken).ConfigureAwait(false));
            fixtures.Add(await BuildDirectionalSwipeAsync(variant, cancellationToken).ConfigureAwait(false));

            fixtures.Add(await BuildBlackHoldAsync(variant, cancellationToken).ConfigureAwait(false));
            fixtures.Add(await BuildFrozenFrameAsync(variant, cancellationToken).ConfigureAwait(false));
            fixtures.Add(await BuildStaticShotAsync(variant, cancellationToken).ConfigureAwait(false));
            fixtures.Add(await BuildRapidMotionAsync(variant, cancellationToken).ConfigureAwait(false));

            fixtures.Add(await BuildVariableFrameRateHardCutAsync(variant, cancellationToken).ConfigureAwait(false));
        }

        var resolutions = new (int Width, int Height)[] { (160, 90), (320, 180), (640, 360) };
        for (var i = 0; i < resolutions.Length; i++)
        {
            fixtures.Add(await BuildMixedResolutionHardCutAsync(i % 2, resolutions[i].Width, resolutions[i].Height, cancellationToken).ConfigureAwait(false));
        }

        var rotations = new[] { 90, 180, 270 };
        for (var i = 0; i < rotations.Length; i++)
        {
            fixtures.Add(await BuildRotatedHardCutAsync(i % 2, rotations[i], cancellationToken).ConfigureAwait(false));
        }

        return fixtures;
    }

    // Variant 0 uses testsrc2->smptebars content, variant 1 uses
    // smptebars->rgbtestsrc - two independent base-content pairs per type,
    // so classifier thresholds are never validated against only one video.
    public async Task<SyntheticFixture> BuildHardCutAsync(int variant, CancellationToken cancellationToken)
    {
        var (a, b) = SourcesForVariant(variant);
        var id = $"hardcut_v{variant}";
        var window = new TimeRange(TransitionOffset, TransitionOffset);

        await RunFfmpegAsync(
            id,
            [
                "-f", "lavfi", "-t", Seconds(TransitionOffset), "-i", a,
                "-f", "lavfi", "-t", Seconds(ClipDuration - TransitionOffset), "-i", b,
                "-filter_complex", "[0:v][1:v]concat=n=2:v=1:a=0[v]",
                "-map", "[v]",
            ],
            cancellationToken).ConfigureAwait(false);

        return Fixture(id, FixtureGroup.HardCut, [new ExpectedTransition(TransitionType.HardCut, window)]);
    }

    public async Task<SyntheticFixture> BuildDissolveAsync(int variant, CancellationToken cancellationToken)
    {
        var (a, b) = SourcesForVariant(variant);
        var id = $"dissolve_v{variant}";
        var window = new TimeRange(TransitionOffset, TransitionOffset + TransitionDuration);

        await RunFfmpegAsync(
            id,
            [
                "-f", "lavfi", "-t", Seconds(ClipDuration), "-i", a,
                "-f", "lavfi", "-t", Seconds(ClipDuration), "-i", b,
                "-filter_complex", $"[0:v][1:v]xfade=transition=fade:duration={Seconds(TransitionDuration)}:offset={Seconds(TransitionOffset)}[v]",
                "-map", "[v]",
            ],
            cancellationToken).ConfigureAwait(false);

        return Fixture(id, FixtureGroup.Dissolve, [new ExpectedTransition(TransitionType.Dissolve, window)]);
    }

    // fade=t=out's own "hold the fade color for every frame after st+d"
    // behavior does exactly what a standalone fade-to-black fixture needs -
    // no enable= scoping required once this is its own fixture rather than
    // sharing a filter chain with a following fade=in.
    public async Task<SyntheticFixture> BuildFadeToBlackAsync(int variant, CancellationToken cancellationToken)
    {
        var (a, _) = SourcesForVariant(variant);
        var id = $"fadetoblack_v{variant}";
        var window = new TimeRange(TransitionOffset, TransitionOffset + TransitionDuration);

        await RunFfmpegAsync(
            id,
            [
                "-f", "lavfi", "-t", Seconds(ClipDuration), "-i", a,
                "-vf", $"fade=t=out:st={Seconds(TransitionOffset)}:d={Seconds(TransitionDuration)}:color=black",
            ],
            cancellationToken).ConfigureAwait(false);

        return Fixture(id, FixtureGroup.FadeToBlack, [new ExpectedTransition(TransitionType.FadeToBlack, window)]);
    }

    // fade=t=in's own "hold the fade color for every frame before st"
    // behavior gives a clean black hold from 0 to TransitionOffset for free.
    public async Task<SyntheticFixture> BuildFadeFromBlackAsync(int variant, CancellationToken cancellationToken)
    {
        var (a, _) = SourcesForVariant(variant);
        var id = $"fadefromblack_v{variant}";
        var window = new TimeRange(TransitionOffset, TransitionOffset + TransitionDuration);

        await RunFfmpegAsync(
            id,
            [
                "-f", "lavfi", "-t", Seconds(ClipDuration), "-i", a,
                "-vf", $"fade=t=in:st={Seconds(TransitionOffset)}:d={Seconds(TransitionDuration)}:color=black",
            ],
            cancellationToken).ConfigureAwait(false);

        return Fixture(id, FixtureGroup.FadeFromBlack, [new ExpectedTransition(TransitionType.FadeFromBlack, window)]);
    }

    public async Task<SyntheticFixture> BuildFlashAsync(int variant, CancellationToken cancellationToken)
    {
        var (a, _) = SourcesForVariant(variant);
        var id = $"flash_v{variant}";
        // A brief, near-instant ramp up (rampDuration) followed by a genuine
        // hold at solid white (holdDuration) - without a hold, two opposing
        // ramps meet at a single instant that a fast sample rate can step
        // over without ever observing a fully-white frame.
        var rampDuration = TimeSpan.FromSeconds(0.03);
        var holdDuration = TimeSpan.FromSeconds(0.2);
        var flashStart = TransitionOffset;
        var holdStart = flashStart + rampDuration;
        var rampBackStart = holdStart + holdDuration;
        var flashEnd = rampBackStart + rampDuration;
        var window = new TimeRange(flashStart, flashEnd);

        await RunFfmpegAsync(
            id,
            [
                "-f", "lavfi", "-t", Seconds(ClipDuration), "-i", a,
                "-vf", $"fade=t=out:st={Seconds(flashStart)}:d={Seconds(rampDuration)}:color=white:enable='between(t,{Seconds(flashStart)},{Seconds(rampBackStart)})'," +
                       $"fade=t=in:st={Seconds(rampBackStart)}:d={Seconds(rampDuration)}:color=white:enable='between(t,{Seconds(rampBackStart)},{Seconds(flashEnd)})'",
            ],
            cancellationToken).ConfigureAwait(false);

        return Fixture(id, FixtureGroup.Flash, [new ExpectedTransition(TransitionType.Flash, window)]);
    }

    public async Task<SyntheticFixture> BuildBlurTransitionAsync(int variant, CancellationToken cancellationToken)
    {
        var (a, _) = SourcesForVariant(variant);
        var id = $"blur_v{variant}";
        var window = new TimeRange(TransitionOffset, TransitionOffset + TransitionDuration);

        await RunFfmpegAsync(
            id,
            [
                "-f", "lavfi", "-t", Seconds(ClipDuration), "-i", a,
                "-vf", $"boxblur=12:2:enable='between(t,{Seconds(TransitionOffset)},{Seconds(TransitionOffset + TransitionDuration)})'",
            ],
            cancellationToken).ConfigureAwait(false);

        return Fixture(id, FixtureGroup.BlurTransition, [new ExpectedTransition(TransitionType.BlurTransition, window)]);
    }

    public async Task<SyntheticFixture> BuildDirectionalSwipeAsync(int variant, CancellationToken cancellationToken)
    {
        var (a, b) = SourcesForVariant(variant);
        var transition = variant == 0 ? "slideleft" : "slideright";
        var id = $"swipe_v{variant}";
        var window = new TimeRange(TransitionOffset, TransitionOffset + TransitionDuration);

        await RunFfmpegAsync(
            id,
            [
                "-f", "lavfi", "-t", Seconds(ClipDuration), "-i", a,
                "-f", "lavfi", "-t", Seconds(ClipDuration), "-i", b,
                "-filter_complex", $"[0:v][1:v]xfade=transition={transition}:duration={Seconds(TransitionDuration)}:offset={Seconds(TransitionOffset)}[v]",
                "-map", "[v]",
            ],
            cancellationToken).ConfigureAwait(false);

        return Fixture(id, FixtureGroup.DirectionalSwipe, [new ExpectedTransition(TransitionType.DirectionalSwipe, window)]);
    }

    public async Task<SyntheticFixture> BuildZoomTransitionAsync(int variant, CancellationToken cancellationToken)
    {
        var (a, _) = SourcesForVariant(variant);
        var id = $"zoom_v{variant}";
        var window = new TimeRange(TransitionOffset, TransitionOffset + TransitionDuration);
        var start = Seconds(TransitionOffset);
        var end = Seconds(TransitionOffset + TransitionDuration);
        var progress = $"min(1,max(0,(t-{start})/({end}-{start})))";
        var scaleFactor = $"(1+1.5*{progress})";

        await RunFfmpegAsync(
            id,
            [
                "-f", "lavfi", "-t", Seconds(ClipDuration), "-i", a,
                "-vf", $"scale=w='iw*{scaleFactor}':h='ih*{scaleFactor}':eval=frame,crop=160:90:(iw-160)/2:(ih-90)/2",
            ],
            cancellationToken).ConfigureAwait(false);

        return Fixture(id, FixtureGroup.ZoomTransition, [new ExpectedTransition(TransitionType.ZoomTransition, window)]);
    }

    // Solid black for the whole clip - no fade in or out, so a black hold
    // must never be reported as FadeToBlack/FadeFromBlack (there is no
    // ramp, only a flat, unchanging frame).
    public async Task<SyntheticFixture> BuildBlackHoldAsync(int variant, CancellationToken cancellationToken)
    {
        var id = $"blackhold_v{variant}";
        var rate = variant == 0 ? 25 : 30;

        await RunFfmpegAsync(
            id,
            ["-f", "lavfi", "-t", Seconds(ClipDuration), "-i", $"color=c=black:s=160x90:r={rate}"],
            cancellationToken).ConfigureAwait(false);

        return Fixture(id, FixtureGroup.BlackHold, []);
    }

    // The `loop` filter repeats a fixed window of already-decoded frames
    // (size=1 -> just the first frame) for the rest of the output - a
    // genuine held/duplicated frame, not merely low motion, which is
    // exactly what an editorial "freeze frame" looks like at the pixel
    // level and must not be mistaken for a hard cut.
    public async Task<SyntheticFixture> BuildFrozenFrameAsync(int variant, CancellationToken cancellationToken)
    {
        var (a, _) = SourcesForVariant(variant);
        var id = $"frozen_v{variant}";

        await RunFfmpegAsync(
            id,
            [
                "-f", "lavfi", "-t", Seconds(ClipDuration), "-i", a,
                "-vf", "loop=loop=-1:size=1:start=0",
                // loop=-1 repeats the held frame forever - the input-side
                // -t above only bounds how much of the *source* is read
                // before looping starts, not the (now infinite) filtered
                // output, so the output duration needs its own -t.
                "-t", Seconds(ClipDuration),
            ],
            cancellationToken).ConfigureAwait(false);

        return Fixture(id, FixtureGroup.FrozenFrame, []);
    }

    // Color bars are a genuinely static, unmoving test pattern (unlike
    // testsrc2, which has its own gentle internal motion) - a tripod-shot
    // stand-in with near-zero but real per-frame pixel noise from
    // encoding, distinct from BuildFrozenFrameAsync's byte-identical hold.
    public async Task<SyntheticFixture> BuildStaticShotAsync(int variant, CancellationToken cancellationToken)
    {
        var id = $"static_v{variant}";
        var source = variant == 0 ? "smptebars=size=160x90:rate=25" : "pal75bars=size=160x90:rate=25";

        await RunFfmpegAsync(
            id,
            ["-f", "lavfi", "-t", Seconds(ClipDuration), "-i", source],
            cancellationToken).ConfigureAwait(false);

        return Fixture(id, FixtureGroup.StaticShot, []);
    }

    // Continuous motion across the *entire* clip (no bounded window, unlike
    // BuildZoomTransitionAsync) - real, ordinary camera motion (a crash
    // zoom or a fast pan) that ZoomTransitionClassifier/
    // DirectionalSwipeClassifier must not mistake for a transition, since
    // both are built on the same dense-optical-flow motion magnitude a
    // genuine pan/zoom also produces.
    public async Task<SyntheticFixture> BuildRapidMotionAsync(int variant, CancellationToken cancellationToken)
    {
        var (a, _) = SourcesForVariant(variant);
        var id = $"rapidmotion_v{variant}";
        var duration = Seconds(ClipDuration);

        var filter = variant == 0
            ? $"scale=w='iw*(1+3*(t/{duration}))':h='ih*(1+3*(t/{duration}))':eval=frame,crop=160:90:(iw-160)/2:(ih-90)/2"
            : $"scale=480x270,crop=160:90:x='(iw-160)*(t/{duration})':y=0";

        await RunFfmpegAsync(
            id,
            [
                "-f", "lavfi", "-t", Seconds(ClipDuration), "-i", a,
                "-vf", filter,
            ],
            cancellationToken).ConfigureAwait(false);

        return Fixture(id, FixtureGroup.RapidMotion, []);
    }

    // Same hard-cut construction as BuildHardCutAsync, but each segment is
    // declared at a different source frame rate (12fps / 30fps) so ffprobe
    // reports a variable-frame-rate stream (avg_frame_rate != r_frame_rate)
    // - exercising VideoStreamInfo.IsVariableFrameRate/RealBaseFrameRate.
    public async Task<SyntheticFixture> BuildVariableFrameRateHardCutAsync(int variant, CancellationToken cancellationToken)
    {
        var (a, b) = SourcesForVariantAtRate(variant, 12, 30);
        var id = $"vfr_hardcut_v{variant}";
        var window = new TimeRange(TransitionOffset, TransitionOffset);

        await RunFfmpegAsync(
            id,
            [
                "-f", "lavfi", "-t", Seconds(TransitionOffset), "-i", a,
                "-f", "lavfi", "-t", Seconds(ClipDuration - TransitionOffset), "-i", b,
                "-filter_complex", "[0:v][1:v]concat=n=2:v=1:a=0[v]",
                "-map", "[v]",
                "-fps_mode", "vfr",
            ],
            cancellationToken).ConfigureAwait(false);

        return Fixture(id, FixtureGroup.VariableFrameRate, [new ExpectedTransition(TransitionType.HardCut, window)]);
    }

    // Same hard-cut construction, rendered at width x height instead of the
    // fixed 160x90 every other fixture uses - proves the detector's own
    // downscale-to-analysis-resolution behavior is resolution-independent.
    public async Task<SyntheticFixture> BuildMixedResolutionHardCutAsync(int variant, int width, int height, CancellationToken cancellationToken)
    {
        var (a, b) = SourcesForVariantAtSize(variant, width, height);
        var id = $"mixedres_{width}x{height}_v{variant}";
        var window = new TimeRange(TransitionOffset, TransitionOffset);

        await RunFfmpegAsync(
            id,
            [
                "-f", "lavfi", "-t", Seconds(TransitionOffset), "-i", a,
                "-f", "lavfi", "-t", Seconds(ClipDuration - TransitionOffset), "-i", b,
                "-filter_complex", "[0:v][1:v]concat=n=2:v=1:a=0[v]",
                "-map", "[v]",
            ],
            cancellationToken).ConfigureAwait(false);

        return Fixture(id, FixtureGroup.MixedResolution, [new ExpectedTransition(TransitionType.HardCut, window)]);
    }

    // Same hard-cut construction, tagged with rotation metadata (as a
    // phone-shot-in-portrait file would carry) - proves the detector reads
    // through VideoStreamInfo.RotationDegrees rather than being thrown off
    // by it.
    public async Task<SyntheticFixture> BuildRotatedHardCutAsync(int variant, int rotationDegrees, CancellationToken cancellationToken)
    {
        var (a, b) = SourcesForVariant(variant);
        var id = $"rotated_{rotationDegrees}_v{variant}";
        var window = new TimeRange(TransitionOffset, TransitionOffset);

        await RunFfmpegAsync(
            id,
            [
                "-f", "lavfi", "-t", Seconds(TransitionOffset), "-i", a,
                "-f", "lavfi", "-t", Seconds(ClipDuration - TransitionOffset), "-i", b,
                "-filter_complex", "[0:v][1:v]concat=n=2:v=1:a=0[v]",
                "-map", "[v]",
                "-metadata:s:v:0", $"rotate={rotationDegrees}",
            ],
            cancellationToken).ConfigureAwait(false);

        return Fixture(id, FixtureGroup.Rotated, [new ExpectedTransition(TransitionType.HardCut, window)]);
    }

    private static (string A, string B) SourcesForVariant(int variant) =>
        variant == 0 ? (SourceA, SourceB) : (SourceB, SourceC);

    private static (string A, string B) SourcesForVariantAtRate(int variant, int rateA, int rateB)
    {
        var (a, b) = SourcesForVariant(variant);
        return (ReplaceRate(a, rateA), ReplaceRate(b, rateB));
    }

    private static (string A, string B) SourcesForVariantAtSize(int variant, int width, int height)
    {
        var (a, b) = SourcesForVariant(variant);
        return (ReplaceSize(a, width, height), ReplaceSize(b, width, height));
    }

    private static string ReplaceRate(string source, int rate) =>
        System.Text.RegularExpressions.Regex.Replace(source, "rate=\\d+", $"rate={rate}");

    private static string ReplaceSize(string source, int width, int height) =>
        System.Text.RegularExpressions.Regex.Replace(source, "size=\\d+x\\d+", $"size={width}x{height}");

    private SyntheticFixture Fixture(string id, FixtureGroup group, IReadOnlyList<ExpectedTransition> expected) =>
        new(id, group, OutputPath(id), ClipDuration.TotalSeconds, expected);

    private string OutputPath(string id) => Path.Combine(_outputDirectory, $"{id}.mp4");

    private async Task RunFfmpegAsync(string id, IReadOnlyList<string> filterArguments, CancellationToken cancellationToken)
    {
        var outputPath = OutputPath(id);
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
                // Generous relative to these clips' actual encode cost (well
                // under a second on typical dev hardware) to absorb slower
                // CI/dev machines without making a one-off slow encode flake
                // the whole fixture matrix.
                Timeout = TimeSpan.FromSeconds(60),
            },
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg fixture build '{id}' failed (exit {result.ExitCode}):\n{result.StandardError}");
        }
    }

    private static string Seconds(TimeSpan value) => value.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture);
}
