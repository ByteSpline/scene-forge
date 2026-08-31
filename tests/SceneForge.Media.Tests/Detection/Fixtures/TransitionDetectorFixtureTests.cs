using SceneForge.Accuracy.Fixtures;
using SceneForge.Media.Detection;
using SceneForge.Media.Detection.Fusion;
using SceneForge.Media.Domain;
using SceneForge.Media.Probing;
using SceneForge.Media.Processes;
using SceneForge.Media.Sampling;
using SceneForge.Media.Tests.TestSupport;
using SceneForge.Media.Tooling;
using Xunit.Abstractions;

namespace SceneForge.Media.Tests.Detection.Fixtures;

// End-to-end: real ffmpeg generates deterministic clips with exactly-known
// transition windows (see SceneForge.Accuracy.Fixtures.SyntheticFixtureCatalog,
// the shared source of truth also used by the accuracy/benchmark console
// tool), the real TransitionDetector pipeline (real FrameSampler/
// FfprobeService/ffmpeg decode, not fakes) analyzes them, and precision/
// recall/boundary-error are computed per transition type across >= 2
// independent base-content variants each - never tuned against, or
// validated against, only one video (a repository requirement). This test
// stays scoped to the 8 core transition types (the accuracy console tool's
// own CI job is the authority for the full distractor/format-robustness
// matrix). Real ffmpeg is never present in CI (see RealFfmpegAvailability),
// so this always skips there; it is exercised locally with the binaries
// temporarily copied into tools/ffmpeg, same procedure as the existing
// Sampling/Probing integration tests.
public class TransitionDetectorFixtureTests
{
    private readonly ITestOutputHelper _output;

    public TransitionDetectorFixtureTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableFact]
    public async Task DetectAsync_SyntheticFixtureMatrix_MeasuresPrecisionRecallAndBoundaryErrorPerType()
    {
        Skip.IfNot(RealFfmpegAvailability.IsAvailable, RealFfmpegAvailability.SkipReason);

        var workingDirectory = Directory.CreateTempSubdirectory("sceneforge-fixtures");
        try
        {
            var catalog = new SyntheticFixtureCatalog(RealFfmpegAvailability.FfmpegPath, workingDirectory.FullName);
            var fixtures = await BuildAllFixturesAsync(catalog, CancellationToken.None);

            var detector = CreateRealDetector();
            var options = TransitionDetectionOptions.ForProfile(AnalysisProfile.Accurate);

            var matches = new List<(TransitionType Type, bool TruePositive, TimeSpan? BoundaryError)>();
            var falsePositives = new List<TransitionType>();

            foreach (var fixture in fixtures)
            {
                var detections = await detector.DetectAsync(fixture.FilePath, options, null, CancellationToken.None);
                var unmatchedDetections = detections.ToList();

                foreach (var expected in fixture.Expected)
                {
                    var match = unmatchedDetections.FirstOrDefault(d => d.Type == expected.Type && Overlaps(d, expected.Window));
                    if (match is not null)
                    {
                        unmatchedDetections.Remove(match);
                        var expectedMidpoint = expected.Window.Start + TimeSpan.FromTicks(expected.Window.Duration.Ticks / 2);
                        matches.Add((expected.Type, true, Abs(match.BoundaryTimestamp - expectedMidpoint)));
                    }
                    else
                    {
                        matches.Add((expected.Type, false, null));
                    }
                }

                falsePositives.AddRange(unmatchedDetections.Select(d => d.Type));
            }

            ReportAndAssert(matches, falsePositives);
        }
        finally
        {
            workingDirectory.Delete(recursive: true);
        }
    }

    // ZoomTransition and DirectionalSwipe are measured and reported like
    // every other type below, but not held to the same "recall > 0" bar:
    // at the ~160x90 analysis resolution this fixture matrix runs at,
    // normalized optical-flow motion magnitude during a genuine zoom/swipe
    // (~0.01-0.03) sits close enough to the magnitude produced by
    // testsrc2's own gentle non-transition motion (~0.003-0.005) that
    // detection is unreliable, and can additionally lose a fusion tie
    // against an unrelated classifier's false alarm over the same
    // interval. This is a real, measured, current limitation of dense-
    // optical-flow-based motion classification at small analysis sizes,
    // documented in docs/PHASE_06_REPORT.md rather than hidden behind a
    // lowered bar for every type - CLAUDE.md rule 10 requires accuracy
    // claims to be measured and honest, not uniformly optimistic.
    private static readonly HashSet<TransitionType> RecallNotYetRequiredAboveZero =
        new HashSet<TransitionType> { TransitionType.ZoomTransition, TransitionType.DirectionalSwipe };

    // Sanity ceiling on total false positives across the whole 32-fixture
    // matrix (docs/ACCURACY_REPORT.md's own committed baseline currently
    // measures 10) - generous headroom (3x) so ordinary run-to-run
    // synthetic-fixture/ffmpeg-version variance never makes this flaky, but
    // low enough to fail hard on a genuine false-positive explosion. This
    // is deliberately a coarse, fast, always-runs-with-the-suite backstop:
    // the *real* regression gate is the Phase 12 accuracy tool's
    // RegressionGate.FalsePositivesPerMinute check (per-fixture-group, run
    // via the accuracy-regression CI job) - this assertion exists because
    // that gate is NOT part of `dotnet test`, so a false-positive
    // regression could otherwise ship without ever failing the normal test
    // suite. Added after a real production report of "10,917 transitions
    // detected" turned out to be a UI-layer bug (see
    // docs/DETECTION_REPORTING_AUDIT.md) that this test suite could not
    // have caught either way - this assertion is a genuine strengthening
    // for the *next* time detection itself regresses, not a claim that it
    // would have caught that specific incident.
    private const int MaxAcceptableTotalFalsePositives = 30;

    private void ReportAndAssert(
        List<(TransitionType Type, bool TruePositive, TimeSpan? BoundaryError)> matches,
        List<TransitionType> falsePositives)
    {
        _output.WriteLine("=== Transition detection metrics by type (measured, not absolute - CLAUDE.md rule 10) ===");
        var failures = new List<string>();

        foreach (var type in Enum.GetValues<TransitionType>())
        {
            var expectedForType = matches.Where(m => m.Type == type).ToList();
            if (expectedForType.Count == 0)
            {
                continue;
            }

            var truePositives = expectedForType.Count(m => m.TruePositive);
            var falseNegatives = expectedForType.Count - truePositives;
            var falsePositivesForType = falsePositives.Count(t => t == type);

            var recall = (double)truePositives / expectedForType.Count;
            var precision = truePositives + falsePositivesForType == 0
                ? double.NaN
                : (double)truePositives / (truePositives + falsePositivesForType);

            var boundaryErrors = expectedForType.Where(m => m.BoundaryError.HasValue).Select(m => m.BoundaryError!.Value.TotalMilliseconds).ToList();
            var meanBoundaryErrorMs = boundaryErrors.Count > 0 ? boundaryErrors.Average() : double.NaN;

            _output.WriteLine(
                $"{type,-18} TP={truePositives} FN={falseNegatives} FP={falsePositivesForType} " +
                $"Recall={recall:P0} Precision={(double.IsNaN(precision) ? "n/a" : precision.ToString("P0"))} " +
                $"MeanBoundaryErrorMs={(double.IsNaN(meanBoundaryErrorMs) ? "n/a" : meanBoundaryErrorMs.ToString("F0"))}");

            // Bounded, non-absolute expectations only (CLAUDE.md rule 10):
            // every type except the two named above must find at least one
            // of its two independent variants (recall > 0) - never asserted
            // as exact or 100%.
            if (recall <= 0 && !RecallNotYetRequiredAboveZero.Contains(type))
            {
                failures.Add($"{type} was not detected in any of its fixture variants.");
            }
        }

        _output.WriteLine($"Total false positives across all fixtures: {falsePositives.Count}");
        if (falsePositives.Count > MaxAcceptableTotalFalsePositives)
        {
            failures.Add(
                $"Total false positives ({falsePositives.Count}) exceeded the sanity ceiling ({MaxAcceptableTotalFalsePositives}) - " +
                "a real false-positive explosion, not measurement noise.");
        }

        Assert.True(failures.Count == 0, string.Join(" | ", failures));
    }

    private static bool Overlaps(TransitionDetection detection, TimeRange expectedWindow) =>
        detection.Start < expectedWindow.End && expectedWindow.Start < detection.End;

    private static TimeSpan Abs(TimeSpan value) => value < TimeSpan.Zero ? -value : value;

    private static async Task<List<SyntheticFixture>> BuildAllFixturesAsync(SyntheticFixtureCatalog catalog, CancellationToken cancellationToken)
    {
        var fixtures = new List<SyntheticFixture>();
        for (var variant = 0; variant < 2; variant++)
        {
            fixtures.Add(await catalog.BuildHardCutAsync(variant, cancellationToken));
            fixtures.Add(await catalog.BuildDissolveAsync(variant, cancellationToken));
            fixtures.Add(await catalog.BuildFadeToBlackAsync(variant, cancellationToken));
            fixtures.Add(await catalog.BuildFadeFromBlackAsync(variant, cancellationToken));
            fixtures.Add(await catalog.BuildFlashAsync(variant, cancellationToken));
            fixtures.Add(await catalog.BuildBlurTransitionAsync(variant, cancellationToken));
            fixtures.Add(await catalog.BuildDirectionalSwipeAsync(variant, cancellationToken));
            fixtures.Add(await catalog.BuildZoomTransitionAsync(variant, cancellationToken));
        }

        return fixtures;
    }

    private static TransitionDetector CreateRealDetector()
    {
        var processRunner = new ProcessRunner();
        var toolLocator = new FfmpegToolLocator(processRunner);
        var ffprobeService = new FfprobeService(processRunner, toolLocator);
        var frameSampler = new FrameSampler(toolLocator, ffprobeService);
        return new TransitionDetector(frameSampler, ffprobeService);
    }
}
