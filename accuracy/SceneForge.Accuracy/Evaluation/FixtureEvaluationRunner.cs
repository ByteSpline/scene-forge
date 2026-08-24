using System.Diagnostics;
using SceneForge.Accuracy.Fixtures;
using SceneForge.Media.Detection;
using SceneForge.Media.Domain;
using SceneForge.Media.Probing;
using SceneForge.Media.Processes;
using SceneForge.Media.Sampling;
using SceneForge.Media.Tooling;

namespace SceneForge.Accuracy.Evaluation;

// Ties SyntheticFixtureCatalog, the real TransitionDetector pipeline (real
// FrameSampler/FfprobeService/ffmpeg decode - no fakes, same posture as
// TransitionDetectorFixtureTests), MetricsCalculator, and
// ResourceUsageSampler together into one EvaluationReport. Always rebuilds
// the fixture matrix fresh into a temp directory rather than reusing
// anything on disk, so a report is never stale relative to the current code
// (mirrors the existing xunit fixture test's own approach).
public static class FixtureEvaluationRunner
{
    public static async Task<EvaluationReport> RunAsync(
        string applicationBaseDirectory,
        AnalysisProfile profile,
        CancellationToken cancellationToken)
    {
        var ffmpegPath = Path.Combine(applicationBaseDirectory, "tools", "ffmpeg", "ffmpeg.exe");
        var workingDirectory = Directory.CreateTempSubdirectory("sceneforge-accuracy-fixtures");
        try
        {
            var catalog = new SyntheticFixtureCatalog(ffmpegPath, workingDirectory.FullName);
            var fixtures = await catalog.BuildAllAsync(cancellationToken).ConfigureAwait(false);

            var detector = CreateDetector(applicationBaseDirectory);
            var options = TransitionDetectionOptions.ForProfile(profile);

            var tallies = new Dictionary<FixtureGroup, GroupTally>();
            var totalSourceSeconds = 0.0;

            await using var sampler = new ResourceUsageSampler();
            var stopwatch = Stopwatch.StartNew();

            foreach (var fixture in fixtures)
            {
                var detections = await detector.DetectAsync(fixture.FilePath, options, null, cancellationToken).ConfigureAwait(false);
                var unmatched = detections.ToList();

                if (!tallies.TryGetValue(fixture.Group, out var tally))
                {
                    tally = new GroupTally();
                    tallies[fixture.Group] = tally;
                }

                foreach (var expected in fixture.Expected)
                {
                    var match = unmatched.FirstOrDefault(d => d.Type == expected.Type && Overlaps(d, expected.Window));
                    if (match is not null)
                    {
                        unmatched.Remove(match);
                        var expectedMidpoint = expected.Window.Start + TimeSpan.FromTicks(expected.Window.Duration.Ticks / 2);
                        var boundaryErrorMs = Math.Abs((match.BoundaryTimestamp - expectedMidpoint).TotalMilliseconds);
                        tally.Matches.Add(new TransitionMatchOutcome(true, boundaryErrorMs));
                    }
                    else
                    {
                        tally.Matches.Add(new TransitionMatchOutcome(false, null));
                    }
                }

                tally.FalsePositiveCount += unmatched.Count;
                tally.TotalSourceSeconds += fixture.SourceDurationSeconds;
                totalSourceSeconds += fixture.SourceDurationSeconds;
            }

            stopwatch.Stop();

            var groupInputs = tallies
                .Select(kv => new GroupEvaluationInput(kv.Key, kv.Value.Matches, kv.Value.FalsePositiveCount, kv.Value.TotalSourceSeconds))
                .ToList();
            var metrics = MetricsCalculator.Compute(groupInputs);

            var wallClockSeconds = stopwatch.Elapsed.TotalSeconds;
            var throughput = wallClockSeconds <= 0 ? 0.0 : totalSourceSeconds / wallClockSeconds;

            return new EvaluationReport(
                DateTimeOffset.UtcNow,
                await TryGetCommitShaAsync(cancellationToken).ConfigureAwait(false),
                profile,
                fixtures.Count,
                metrics,
                throughput,
                wallClockSeconds,
                sampler.PeakManagedMemoryBytes,
                sampler.PeakWorkingSetBytes);
        }
        finally
        {
            workingDirectory.Delete(recursive: true);
        }
    }

    private static bool Overlaps(TransitionDetection detection, TimeRange expectedWindow) =>
        detection.Start < expectedWindow.End && expectedWindow.Start < detection.End;

    private static TransitionDetector CreateDetector(string applicationBaseDirectory)
    {
        var processRunner = new ProcessRunner();
        var toolLocator = new FfmpegToolLocator(processRunner, applicationBaseDirectory);
        var ffprobeService = new FfprobeService(processRunner, toolLocator);
        var frameSampler = new FrameSampler(toolLocator, ffprobeService);
        return new TransitionDetector(frameSampler, ffprobeService);
    }

    // Best-effort only: a report without a commit sha (e.g. run outside a
    // git checkout) is still useful, so a git failure here must never fail
    // the whole evaluation run.
    private static async Task<string?> TryGetCommitShaAsync(CancellationToken cancellationToken)
    {
        try
        {
            var runner = new ProcessRunner();
            var result = await runner.RunAsync(
                new ProcessExecutionRequest
                {
                    FileName = "git",
                    Arguments = ["rev-parse", "HEAD"],
                    Timeout = TimeSpan.FromSeconds(5),
                },
                cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private sealed class GroupTally
    {
        public List<TransitionMatchOutcome> Matches { get; } = [];

        public int FalsePositiveCount { get; set; }

        public double TotalSourceSeconds { get; set; }
    }
}
