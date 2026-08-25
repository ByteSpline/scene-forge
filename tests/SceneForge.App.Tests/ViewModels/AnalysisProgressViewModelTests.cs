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
        Assert.Contains(ProjectStage.Analyzed, persistence.BegunStages);
        Assert.Contains(ProjectStage.Analyzed, persistence.CheckpointedStages);
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
