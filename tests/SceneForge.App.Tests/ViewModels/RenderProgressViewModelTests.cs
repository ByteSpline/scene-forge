using SceneForge.App.Navigation;
using SceneForge.App.Session;
using SceneForge.App.Tests.TestSupport;
using SceneForge.App.ViewModels;
using SceneForge.Infrastructure.Persistence;
using SceneForge.Media.Domain;
using SceneForge.Media.Rendering;
using SceneForge.Media.Tooling;

namespace SceneForge.App.Tests.ViewModels;

public class RenderProgressViewModelTests
{
    [Fact]
    public void Construction_HappyPath_RunsRenderSynchronouslyAndNavigatesToCompletion()
    {
        var session = BuildSessionWithRenderPlan();
        var renderService = new FakeFFmpegRenderService();
        var navigator = new WorkflowNavigator();
        var persistence = new FakeProjectPersistenceCoordinator();

        var vm = new RenderProgressViewModel(session, renderService, navigator, persistence);

        Assert.False(vm.IsRunning);
        Assert.Null(vm.ErrorMessage);
        Assert.NotNull(session.RenderResult);
        Assert.Equal(WorkflowStep.Completion, navigator.CurrentStep);
        Assert.Equal(session.OutputVideoPath, renderService.LastOutputFilePath);
        Assert.Contains(ProjectStage.Completed, persistence.BegunStages);
        Assert.Contains(ProjectStage.Completed, persistence.CheckpointedStages);
        Assert.Equal(1, persistence.FinalizeCallCount);
    }

    [Fact]
    public void Construction_RenderThrowsRecognizedFailure_SetsACalmNonTechnicalErrorMessageAndDoesNotNavigate()
    {
        var session = BuildSessionWithRenderPlan();
        var renderService = new FakeFFmpegRenderService
        {
            ExceptionToThrow = new RenderExecutionException("ffmpeg render with encoder 'libx264' failed (exit code 1): stderr garbage"),
        };
        var navigator = new WorkflowNavigator();

        var vm = new RenderProgressViewModel(session, renderService, navigator, new FakeProjectPersistenceCoordinator());

        Assert.False(vm.IsRunning);
        // A genuinely unrecoverable failure still reaches the user, but as
        // calm, plain-language text - never the raw exception message
        // (exit codes / stderr excerpts), per the product requirement that
        // this screen must never look like a crash.
        Assert.NotNull(vm.ErrorMessage);
        Assert.DoesNotContain("exit code", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stderr", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("We couldn't finish your render.", vm.StatusText);
        Assert.Null(session.RenderResult);
        Assert.Equal(WorkflowStep.WelcomeImport, navigator.CurrentStep);
    }

    [Theory]
    [InlineData(typeof(FfmpegToolsMissingFake))]
    [InlineData(typeof(FfmpegToolsIncompatibleFake))]
    public void Construction_RenderThrowsToolLocatorFailure_IsRecognizedAndShowsACalmMessage_NotAnUnhandledCrash(Type exceptionFactoryType)
    {
        // Regression coverage: FfmpegToolsNotFoundException/
        // FfmpegToolsIncompatibleException were previously NOT in
        // IsRecognizedRenderFailure's type list, so they propagated past
        // this ViewModel entirely - an unhandled crash, which is strictly
        // worse than the red "Render failed" screen this whole change is
        // about avoiding.
        var factory = (IExceptionFactory)Activator.CreateInstance(exceptionFactoryType)!;
        var session = BuildSessionWithRenderPlan();
        var renderService = new FakeFFmpegRenderService { ExceptionToThrow = factory.Create() };
        var navigator = new WorkflowNavigator();

        var vm = new RenderProgressViewModel(session, renderService, navigator, new FakeProjectPersistenceCoordinator());

        Assert.False(vm.IsRunning);
        Assert.NotNull(vm.ErrorMessage);
        Assert.Equal(WorkflowStep.WelcomeImport, navigator.CurrentStep);
    }

    private interface IExceptionFactory
    {
        Exception Create();
    }

    private sealed class FfmpegToolsMissingFake : IExceptionFactory
    {
        public Exception Create() => new FfmpegToolsNotFoundException(["ffmpeg.exe"]);
    }

    private sealed class FfmpegToolsIncompatibleFake : IExceptionFactory
    {
        public Exception Create() => new FfmpegToolsIncompatibleException("ffmpeg.exe", "unexpected banner");
    }

    [Fact]
    public void Construction_RenderThrowsInsufficientDiskSpace_MentionsDiskSpaceWithoutRawByteCounts()
    {
        var session = BuildSessionWithRenderPlan();
        var renderService = new FakeFFmpegRenderService
        {
            ExceptionToThrow = new SceneForge.Core.Resources.InsufficientDiskSpaceException(@"C:\out", 500_000_000, 10_000),
        };
        var navigator = new WorkflowNavigator();

        var vm = new RenderProgressViewModel(session, renderService, navigator, new FakeProjectPersistenceCoordinator());

        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("disk space", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("500", vm.ErrorMessage);
    }

    [Fact]
    public async Task CancelCommand_WhileRenderInProgress_StopsRunAndReportsCanceled()
    {
        var session = BuildSessionWithRenderPlan();
        var gate = new TaskCompletionSource<bool>();
        var renderService = new FakeFFmpegRenderService { Gate = gate };
        var navigator = new WorkflowNavigator();

        var vm = new RenderProgressViewModel(session, renderService, navigator, new FakeProjectPersistenceCoordinator());

        Assert.True(vm.IsRunning);
        vm.CancelCommand.Execute(null);
        gate.SetResult(true);
        await (vm.RunCommand.ExecutionTask ?? Task.CompletedTask);

        Assert.False(vm.IsRunning);
        Assert.Equal("Render canceled.", vm.StatusText);
        Assert.Equal(WorkflowStep.WelcomeImport, navigator.CurrentStep);
    }

    private static WorkflowSession BuildSessionWithRenderPlan()
    {
        var plan = new RenderPlan
        {
            SourceFilePath = "video.mp4",
            Segments =
            [
                new RenderSegment
                {
                    Position = 0,
                    SourceStart = TimeSpan.Zero,
                    SourceDuration = TimeSpan.FromSeconds(3),
                    IsTrimmed = false,
                },
            ],
            OutputSpec = new RenderOutputSpec { FrameRate = new RationalFrameRate(30, 1) },
            Audio = new RenderAudioTrackSpec { FilePath = "audio.m4a", TrimDuration = TimeSpan.FromSeconds(3) },
            SourceRotationDegrees = 0,
            PlannedVideoDuration = TimeSpan.FromSeconds(3),
        };

        return new WorkflowSession
        {
            RenderPlan = plan,
            OutputVideoPath = @"C:\out\video.mp4",
        };
    }
}
