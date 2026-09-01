using SceneForge.App.Navigation;
using SceneForge.App.Session;
using SceneForge.App.Tests.TestSupport;
using SceneForge.App.ViewModels;
using SceneForge.Infrastructure.Persistence;
using SceneForge.Media.Probing;
using SceneForge.Media.Sampling;

namespace SceneForge.App.Tests.ViewModels;

public class AnalysisProgressViewModelTests
{
    [Fact]
    public void Construction_HappyPath_RunsPipelineSynchronouslyAndNavigatesToSceneReview()
    {
        var session = BuildSessionReadyForAnalysis();
        var detection = TransitionDetectionBuilder.Build(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5));
        var transitionDetector = new FakeTransitionDetector { Result = [detection] };
        var acceptedClip = CleanClipBuilder.Build(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(9), accepted: true);
        var extractor = new FakeCleanClipExtractor
        {
            Result = new SceneForge.Media.Extraction.CleanClipExtractionResult
            {
                RemainingCleanRanges = [],
                AcceptedClips = [acceptedClip],
                RejectedClips = [],
                Clusters = [],
            },
        };
        var navigator = new WorkflowNavigator();
        var persistence = new FakeProjectPersistenceCoordinator();

        var vm = new AnalysisProgressViewModel(session, new FakeFfprobeService(), transitionDetector, extractor, navigator, persistence);

        Assert.False(vm.IsRunning);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal(WorkflowStep.SceneReview, navigator.CurrentStep);
        Assert.NotNull(session.Detections);
        Assert.Single(session.Detections!);
        Assert.NotNull(session.SceneRangeResult);
        Assert.NotNull(session.ExtractionResult);
        Assert.Equal(1, vm.ClipsAccepted);
        Assert.Equal(1, vm.TransitionsFound);
        Assert.Contains(ProjectStage.Analyzed, persistence.BegunStages);
        Assert.Contains(ProjectStage.Analyzed, persistence.CheckpointedStages);
    }

    // Regression coverage for a real, reported bug (see
    // docs/DETECTION_REPORTING_AUDIT.md): TransitionsFound used to stay
    // bound to the raw, pre-fusion candidate stream
    // (TransitionDetectionProgress.RawCandidatesSoFar) even after
    // DetectAsync completed - a live progress indicator that
    // TransitionFuser's own remarks document as growing far larger than
    // the real, deduplicated transition count (every classifier re-scans
    // its sliding window on every sampled frame), never corrected once the
    // real count was known. A real 3-minute video measured
    // RawCandidatesSoFar=4004 against a real fused DetectAsync return
    // value of 109 - the mechanism behind a production report of "10,917
    // transitions detected" on unrelated footage. This test simulates that
    // exact gap (a raw count in the thousands, a real result count in the
    // single digits) and asserts the settled, post-completion value is the
    // real one.
    [Fact]
    public void Construction_RawCandidateProgressFarExceedsFinalResult_TransitionsFoundEndsUpAtTheRealFusedCount()
    {
        // Progress<T>.Report posts through whatever SynchronizationContext
        // was current when the Progress<T> was constructed; installing a
        // synchronous stand-in for a real UI dispatcher here makes that
        // ordering deterministic for this test (see
        // ImmediateSynchronizationContext's own remarks) instead of racing
        // against the default ThreadPool fallback.
        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new ImmediateSynchronizationContext());
        try
        {
            var session = BuildSessionReadyForAnalysis();
            var detections = Enumerable.Range(0, 5)
                .Select(i => TransitionDetectionBuilder.Build(TimeSpan.FromSeconds(i * 5), TimeSpan.FromSeconds((i * 5) + 1)))
                .ToArray();
            var transitionDetector = new FakeTransitionDetector { Result = detections, RawCandidatesSoFarOverride = 4004 };
            var navigator = new WorkflowNavigator();

            var vm = new AnalysisProgressViewModel(session, new FakeFfprobeService(), transitionDetector, new FakeCleanClipExtractor(), navigator, new FakeProjectPersistenceCoordinator());

            Assert.Equal(5, vm.TransitionsFound);
            Assert.NotEqual(4004, vm.TransitionsFound);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void Construction_DetectorThrowsRecognizedFailure_SetsErrorMessageAndDoesNotNavigate()
    {
        var session = BuildSessionReadyForAnalysis();
        var transitionDetector = new ThrowingTransitionDetector(new FfprobeExecutionException("ffprobe exploded"));
        var navigator = new WorkflowNavigator();

        var vm = new AnalysisProgressViewModel(session, new FakeFfprobeService(), transitionDetector, new FakeCleanClipExtractor(), navigator, new FakeProjectPersistenceCoordinator());

        Assert.False(vm.IsRunning);
        Assert.Equal("ffprobe exploded", vm.ErrorMessage);
        Assert.Equal(WorkflowStep.WelcomeImport, navigator.CurrentStep);
    }

    [Fact]
    public async Task CancelCommand_WhileDetectionInProgress_StopsRunAndReportsCanceled()
    {
        var session = BuildSessionReadyForAnalysis();
        var gate = new TaskCompletionSource<bool>();
        var transitionDetector = new FakeTransitionDetector { Gate = gate };
        var navigator = new WorkflowNavigator();
        var persistence = new FakeProjectPersistenceCoordinator();

        var vm = new AnalysisProgressViewModel(session, new FakeFfprobeService(), transitionDetector, new FakeCleanClipExtractor(), navigator, persistence);

        // The gate keeps DetectAsync suspended, so the constructor's
        // fire-and-forget RunCommand invocation has not completed yet.
        Assert.True(vm.IsRunning);
        Assert.True(vm.CancelCommand.CanExecute(null));

        vm.CancelCommand.Execute(null);
        gate.SetResult(true);
        await (vm.RunCommand.ExecutionTask ?? Task.CompletedTask);

        Assert.False(vm.IsRunning);
        Assert.Equal("Analysis canceled.", vm.StatusText);
        Assert.Equal(WorkflowStep.WelcomeImport, navigator.CurrentStep);

        // The stage was marked started (BeginStageAsync) but cancellation
        // happened before extraction ever completed, so CheckpointAsync was
        // never reached - the previous stage's on-disk checkpoint (if any)
        // is retained untouched (CLAUDE.md rule 5's "retain the last valid
        // checkpoint" - see docs/PHASE_11_REPORT.md).
        Assert.Contains(ProjectStage.Analyzed, persistence.BegunStages);
        Assert.DoesNotContain(ProjectStage.Analyzed, persistence.CheckpointedStages);
    }

    private static WorkflowSession BuildSessionReadyForAnalysis()
    {
        const string videoPath = "video.mp4";
        const string audioPath = "audio.m4a";
        return new WorkflowSession
        {
            VideoFilePath = videoPath,
            AudioFilePath = audioPath,
            VideoMediaInfo = MediaInfoBuilder.Video(videoPath, TimeSpan.FromSeconds(30)),
            AudioMediaInfo = MediaInfoBuilder.Audio(audioPath, TimeSpan.FromSeconds(30)),
            AnalysisProfile = AnalysisProfile.Fast,
        };
    }

    private sealed class ThrowingTransitionDetector(Exception exception) : SceneForge.Media.Detection.ITransitionDetector
    {
        public Task<IReadOnlyList<SceneForge.Media.Detection.TransitionDetection>> DetectAsync(
            string filePath,
            SceneForge.Media.Detection.TransitionDetectionOptions options,
            IProgress<SceneForge.Media.Detection.TransitionDetectionProgress>? progress,
            CancellationToken cancellationToken) =>
            throw exception;

        public Task<IReadOnlyList<SceneForge.Media.Detection.TransitionDetection>> DetectAsync(
            string filePath,
            SceneForge.Media.Domain.MediaInfo mediaInfo,
            SceneForge.Media.Detection.TransitionDetectionOptions options,
            IProgress<SceneForge.Media.Detection.TransitionDetectionProgress>? progress,
            CancellationToken cancellationToken) =>
            throw exception;
    }
}
