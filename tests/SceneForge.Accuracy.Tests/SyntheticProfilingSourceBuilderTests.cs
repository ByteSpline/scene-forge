using SceneForge.Accuracy.Profiling;
using SceneForge.Media.Processes;

namespace SceneForge.Accuracy.Tests;

// Regression coverage for a strict-review finding: ConcatAsync used to pass
// outputPath directly as ffmpeg's own output argument for the concat step -
// the exact same path BuildAsync's "already built, reuse it" check trusts
// unconditionally on every future call. A failure partway through that
// ffmpeg invocation (a real Ctrl+C, a crash, a timeout) would leave a
// truncated file sitting at outputPath, which every subsequent BuildAsync
// call would then silently treat as a complete, valid cached source
// (CLAUDE.md rule 10: nothing about this should be silent). Fixed by
// writing to a temp path next to outputPath and only File.Move-ing it in
// once ffmpeg has exited successfully - these tests exercise both the
// failure path (outputPath must never come into existence) and the success
// path (outputPath must end up with the real content, not just an empty
// marker) without spawning real ffmpeg.
public class SyntheticProfilingSourceBuilderTests
{
    [Fact]
    public async Task BuildAsync_ConcatStepFails_OutputPathIsNeverCreated()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"sceneforge-profiling-test-{Guid.NewGuid():N}.mp4");
        try
        {
            var processRunner = new RecordingProcessRunner(request =>
                IsConcatStep(request)
                    ? FailAfterPartiallyWritingOutputFile(request)
                    : SucceedAndTouchOutputFile(request));
            var builder = new SyntheticProfilingSourceBuilder("ffmpeg.exe", processRunner);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => builder.BuildAsync(outputPath, forceRebuild: false, CancellationToken.None));

            Assert.False(File.Exists(outputPath));
            // No stray "<name>.tmp-<guid>.mp4" left behind next to outputPath either.
            var directory = Path.GetDirectoryName(outputPath)!;
            var baseName = Path.GetFileNameWithoutExtension(outputPath);
            Assert.Empty(Directory.GetFiles(directory, $"{baseName}.tmp-*.mp4"));
        }
        finally
        {
            // Defensive only: a passing test never creates outputPath at
            // all, but a future regression here (this is exactly the
            // scenario this test exists to catch) must not also leave a
            // stray file behind for the next test run to trip over.
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    [Fact]
    public async Task BuildAsync_ConcatStepSucceeds_NeverTargetsOutputPathDirectlyAndMovesRealContentIn()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"sceneforge-profiling-test-{Guid.NewGuid():N}.mp4");
        try
        {
            var processRunner = new RecordingProcessRunner(SucceedAndTouchOutputFile);
            var builder = new SyntheticProfilingSourceBuilder("ffmpeg.exe", processRunner);

            var result = await builder.BuildAsync(outputPath, forceRebuild: false, CancellationToken.None);

            Assert.Equal(outputPath, result);
            Assert.True(File.Exists(outputPath));
            Assert.Equal("fake ffmpeg output", await File.ReadAllTextAsync(outputPath));

            var concatRequest = Assert.Single(processRunner.Requests, IsConcatStep);
            var concatOutputArgument = concatRequest.Arguments[^1];
            Assert.NotEqual(outputPath, concatOutputArgument);
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    private static bool IsConcatStep(ProcessExecutionRequest request) => request.Arguments.Contains("concat");

    // Simulates what a real ffmpeg process killed mid-encode actually does:
    // some bytes already flushed to its output path before the process
    // died, not a clean "nothing written" failure - a "return ExitCode: 1
    // and write nothing" fake would pass even against the pre-fix code
    // (which also wrote nothing to outputPath on a clean, no-bytes-written
    // failure), so it would never have caught the real bug.
    private static ProcessExecutionResult FailAfterPartiallyWritingOutputFile(ProcessExecutionRequest request)
    {
        var outputArgument = request.Arguments[^1];
        File.WriteAllText(outputArgument, "truncated, partially-written output");
        return new ProcessExecutionResult { ExitCode = 1, StandardOutput = string.Empty, StandardError = "simulated mid-encode failure", Elapsed = TimeSpan.Zero };
    }

    // Simulates a successful ffmpeg run by writing recognizable content to
    // whatever output path the request's last argument names - the same
    // shape every step in SyntheticProfilingSourceBuilder uses (output path
    // is always the final argument).
    private static ProcessExecutionResult SucceedAndTouchOutputFile(ProcessExecutionRequest request)
    {
        var outputArgument = request.Arguments[^1];
        if (!IsConcatStep(request))
        {
            File.WriteAllText(outputArgument, "fake segment");
        }
        else
        {
            File.WriteAllText(outputArgument, "fake ffmpeg output");
        }

        return new ProcessExecutionResult { ExitCode = 0, StandardOutput = string.Empty, StandardError = string.Empty, Elapsed = TimeSpan.Zero };
    }

    private sealed class RecordingProcessRunner(Func<ProcessExecutionRequest, ProcessExecutionResult> handler) : IProcessRunner
    {
        public List<ProcessExecutionRequest> Requests { get; } = [];

        public Task<ProcessExecutionResult> RunAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(handler(request));
        }
    }
}
