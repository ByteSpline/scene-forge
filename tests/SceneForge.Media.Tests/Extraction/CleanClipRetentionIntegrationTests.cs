using SceneForge.Core.Resources;
using SceneForge.Media.Domain;
using SceneForge.Media.Extraction;
using SceneForge.Media.Probing;
using SceneForge.Media.Processes;
using SceneForge.Media.Sampling;
using SceneForge.Media.Tests.TestSupport;
using SceneForge.Media.Tooling;
using Xunit.Abstractions;

namespace SceneForge.Media.Tests.Extraction;

// Regression coverage for a real, measured over-exclusion bug reported from
// production use: a 320-scene source video yielded only 18-19 usable
// clips. Root-caused (see docs/CLEAN_CLIP_RETENTION_AUDIT.md) to
// CleanClipScoringOptions.Default's old TransitionSafeDistance (2s)
// requiring a candidate be at least MinAcceptableFactorScore (0.3) x 2s =
// 600ms from the nearest excluded interval just to avoid automatic
// rejection on that one factor alone, while BoundaryGuard (250ms) never
// pushed a candidate more than 250ms away - so no scene shorter than
// ~8.5s could ever produce an accepted clip, regardless of footage
// quality. This test builds a real, synthetic multi-transition source
// (five real fade-to-black-and-back "transition" zones, each 0.5s -
// matching SyntheticFixtureCatalog's own real transition contamination
// width, the ground truth docs/ACCURACY_REPORT.md is measured against -
// separating clean scenes of deliberately mixed length, some short enough
// to have been entirely discarded under the old defaults) and proves,
// against real ffmpeg-decoded frames:
//   1. The new defaults retain significantly more clean footage than the
//      old ones did, on the same real source.
//   2. Regardless of which defaults are used, no accepted clip's range
//      ever overlaps a real transition zone - the correctness guarantee
//      (CLAUDE.md rule 10's "never claim 100% accuracy" is about
//      detection, not this: IntervalSubtractor's exclusion is exact
//      interval math, not a statistical claim).
public sealed class CleanClipRetentionIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _outputDirectory;

    public CleanClipRetentionIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _outputDirectory = Path.Combine(Path.GetTempPath(), "SceneForgeCleanClipRetentionTests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDirectory))
        {
            Directory.Delete(_outputDirectory, recursive: true);
        }
    }

    // Deliberately mixed clean-scene lengths: 2.6s and 2.7s are the
    // motivating case (a real clean segment too short to ever survive the
    // old defaults' effective ~8.5s floor, but well-formed real footage
    // that should now be usable); 1.4s stays deliberately below even the
    // NEW floor (MinClipDuration 2s + 2*BoundaryGuard 0.25s = 2.5s) to
    // prove this fix does not simply accept everything; 9.0s and 6.0s are
    // comfortably usable under both old and new defaults, so the test
    // also confirms nothing regresses for footage that was already fine.
    private static readonly double[] SceneLengthsSeconds = [2.6, 9.0, 2.7, 1.4, 6.0];
    private static readonly TimeSpan TransitionRampDuration = TimeSpan.FromSeconds(0.1);
    private static readonly TimeSpan TransitionHoldDuration = TimeSpan.FromSeconds(0.3); // ramp + hold + ramp = 0.5s total, matching SyntheticFixtureCatalog.TransitionDuration
    private static readonly TimeSpan FuserStyleBuffer = TimeSpan.FromMilliseconds(100); // mirrors TransitionDetectionProfile.PreBufferDuration/PostBufferDuration

    private readonly record struct SceneWindow(TimeRange Range, double LengthSeconds);

    [SkippableFact]
    public async Task ExtractAsync_RealFfmpegMultiTransitionSource_NewDefaultsRetainSignificantlyMoreCleanFootage_AndNeverIncludeContaminatedFrames()
    {
        Skip.IfNot(RealFfmpegAvailability.IsAvailable, RealFfmpegAvailability.SkipReason);
        Directory.CreateDirectory(_outputDirectory);

        var (videoPath, totalDuration, scenes, transitionZones) = await BuildMultiTransitionSourceAsync();
        _output.WriteLine($"Total duration: {totalDuration.TotalSeconds:F2}s, scenes: {scenes.Count}, transition zones: {transitionZones.Count}");

        // Excluded intervals as SceneRangeCalculator would produce them in
        // production: the real transition zone padded by a fuser-style
        // buffer on each side (TransitionDetectionProfile.PreBufferDuration/
        // PostBufferDuration), clamped to the video bounds.
        var excludedIntervals = transitionZones
            .Select(z => new ExcludedInterval
            {
                Range = new TimeRange(
                    Max(TimeSpan.Zero, z.Start - FuserStyleBuffer),
                    Min(totalDuration, z.End + FuserStyleBuffer)),
                Kind = ExclusionKind.Transition,
                Reason = "test fixture",
            })
            .ToList();

        var extractor = CreateExtractor();
        var baseOptions = new CleanClipExtractionOptions
        {
            SamplingOptions = new FrameSamplingOptions { AnalysisWidthPixels = 160, SampleFramesPerSecond = 10.0 },
            SceneRanges = [new TimeRange(TimeSpan.Zero, totalDuration)],
            ExcludedIntervals = excludedIntervals,
        };

        var newDefaultsResult = await extractor.ExtractAsync(videoPath, baseOptions with { Scoring = CleanClipScoringOptions.Default }, progress: null, CancellationToken.None);
        var oldDefaultsResult = await extractor.ExtractAsync(
            videoPath,
            baseOptions with { Scoring = CleanClipScoringOptions.Default with { MinClipDuration = TimeSpan.FromSeconds(3), TransitionSafeDistance = TimeSpan.FromSeconds(2) } },
            progress: null,
            CancellationToken.None);

        var newFootage = newDefaultsResult.AcceptedClips.Sum(c => c.Range.Duration.TotalSeconds);
        var oldFootage = oldDefaultsResult.AcceptedClips.Sum(c => c.Range.Duration.TotalSeconds);
        _output.WriteLine($"New defaults: {newDefaultsResult.AcceptedClips.Count} accepted clips, {newFootage:F2}s footage");
        _output.WriteLine($"Old defaults: {oldDefaultsResult.AcceptedClips.Count} accepted clips, {oldFootage:F2}s footage");
        foreach (var clip in newDefaultsResult.AcceptedClips)
        {
            _output.WriteLine($"  new-accepted: {clip.Range.Start.TotalSeconds:F2}-{clip.Range.End.TotalSeconds:F2}s (overall={clip.Score.Overall:F2})");
        }

        foreach (var clip in newDefaultsResult.RejectedClips)
        {
            _output.WriteLine($"  new-rejected: {clip.Range.Start.TotalSeconds:F2}-{clip.Range.End.TotalSeconds:F2}s, reasons: {string.Join(", ", clip.Score.Reasons.Where(r => !r.Passed).Select(r => r.Detail))}");
        }

        // The core, motivating improvement: significantly more clean
        // footage retained from the exact same real source.
        Assert.True(newDefaultsResult.AcceptedClips.Count > oldDefaultsResult.AcceptedClips.Count,
            $"expected more accepted clips with the new defaults ({newDefaultsResult.AcceptedClips.Count}) than the old ones ({oldDefaultsResult.AcceptedClips.Count})");
        Assert.True(newFootage > oldFootage * 1.5,
            $"expected the new defaults to retain at least 50% more footage ({newFootage:F2}s) than the old defaults ({oldFootage:F2}s)");

        // The specific motivating cases: the 2.6s and 2.7s clean scenes
        // must now produce at least one accepted clip (they could not
        // under the old defaults - assert that too, so this test would
        // have failed to demonstrate the bug if it regressed).
        var scene0 = scenes[0]; // 2.6s
        var scene2 = scenes[2]; // 2.7s
        Assert.Contains(newDefaultsResult.AcceptedClips, c => IsWithin(c.Range, scene0.Range));
        Assert.Contains(newDefaultsResult.AcceptedClips, c => IsWithin(c.Range, scene2.Range));
        Assert.DoesNotContain(oldDefaultsResult.AcceptedClips, c => IsWithin(c.Range, scene0.Range));
        Assert.DoesNotContain(oldDefaultsResult.AcceptedClips, c => IsWithin(c.Range, scene2.Range));

        // The deliberately-too-short-even-for-the-new-floor scene (1.4s)
        // must still produce nothing under either configuration - this fix
        // relaxes a disproportionate margin, it does not remove the floor.
        var scene3 = scenes[3]; // 1.4s
        Assert.DoesNotContain(newDefaultsResult.AcceptedClips, c => scene3.Range.Overlaps(c.Range));
        Assert.DoesNotContain(oldDefaultsResult.AcceptedClips, c => scene3.Range.Overlaps(c.Range));

        // The correctness guarantee: not one accepted (or rejected) clip,
        // under either configuration, ever overlaps a real transition
        // zone - checked against the actual detected/padded exclusion
        // intervals, not just the geometric candidate windows.
        var allClips = newDefaultsResult.AcceptedClips
            .Concat(newDefaultsResult.RejectedClips)
            .Concat(oldDefaultsResult.AcceptedClips)
            .Concat(oldDefaultsResult.RejectedClips);
        Assert.All(allClips, clip => Assert.All(excludedIntervals, exclusion => Assert.False(clip.Range.Overlaps(exclusion.Range))));
    }

    private async Task<(string VideoPath, TimeSpan TotalDuration, IReadOnlyList<SceneWindow> Scenes, IReadOnlyList<TimeRange> TransitionZones)> BuildMultiTransitionSourceAsync()
    {
        var scenes = new List<SceneWindow>();
        var transitionZones = new List<TimeRange>();
        var cursor = TimeSpan.Zero;
        var fadeFilters = new List<string>();

        for (var i = 0; i < SceneLengthsSeconds.Length; i++)
        {
            var sceneStart = cursor;
            var sceneLength = TimeSpan.FromSeconds(SceneLengthsSeconds[i]);
            cursor += sceneLength;
            scenes.Add(new SceneWindow(new TimeRange(sceneStart, cursor), SceneLengthsSeconds[i]));

            if (i == SceneLengthsSeconds.Length - 1)
            {
                continue;
            }

            var transitionStart = cursor;
            var holdStart = transitionStart + TransitionRampDuration;
            var rampBackStart = holdStart + TransitionHoldDuration;
            var transitionEnd = rampBackStart + TransitionRampDuration;
            transitionZones.Add(new TimeRange(transitionStart, transitionEnd));

            fadeFilters.Add(
                $"fade=t=out:st={Seconds(transitionStart)}:d={Seconds(TransitionRampDuration)}:color=black:enable='between(t,{Seconds(transitionStart)},{Seconds(rampBackStart)})'," +
                $"fade=t=in:st={Seconds(rampBackStart)}:d={Seconds(TransitionRampDuration)}:color=black:enable='between(t,{Seconds(rampBackStart)},{Seconds(transitionEnd)})'");

            cursor = transitionEnd;
        }

        var totalDuration = cursor;
        var videoPath = Path.Combine(_outputDirectory, "multi_transition_source.mp4");

        var processRunner = new ProcessRunner();
        var arguments = new List<string>
        {
            "-hide_banner", "-y", "-f", "lavfi", "-i", $"testsrc2=size=320x240:rate=25",
            "-t", Seconds(totalDuration),
        };
        if (fadeFilters.Count > 0)
        {
            arguments.AddRange(["-vf", string.Join(',', fadeFilters)]);
        }

        arguments.AddRange(["-c:v", "libx264", "-pix_fmt", "yuv420p", videoPath]);

        var result = await processRunner.RunAsync(
            new ProcessExecutionRequest { FileName = RealFfmpegAvailability.FfmpegPath, Arguments = arguments },
            CancellationToken.None);
        Assert.True(result.ExitCode == 0, $"Failed to synthesize the multi-transition source: {result.StandardError}");

        return (videoPath, totalDuration, scenes, transitionZones);
    }

    private static string Seconds(TimeSpan value) => value.TotalSeconds.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    private static bool IsWithin(TimeRange inner, TimeRange outer) => inner.Start >= outer.Start && inner.End <= outer.End;

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private static CleanClipExtractor CreateExtractor()
    {
        var processRunner = new ProcessRunner();
        var toolLocator = new FfmpegToolLocator(processRunner);
        var ffprobeService = new FfprobeService(processRunner, toolLocator);
        var frameSampler = new FrameSampler(toolLocator, ffprobeService, new AdaptiveResourceGovernor());
        return new CleanClipExtractor(frameSampler, ffprobeService);
    }
}
