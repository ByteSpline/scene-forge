using SceneForge.App.Navigation;
using SceneForge.App.Session;
using SceneForge.App.Tests.TestSupport;
using SceneForge.App.ViewModels;
using SceneForge.Media.Domain;
using SceneForge.Media.Rendering;

namespace SceneForge.App.Tests.ViewModels;

public class RenderProgressViewModelTests
{
    [Fact]
    public void Construction_HappyPath_RunsRenderSynchronouslyAndNavigatesToCompletion()
    {
        var session = BuildSessionWithRenderPlan();
        var renderService = new FakeFFmpegRenderService();
        var navigator = new WorkflowNavigator();

        var vm = new RenderProgressViewModel(session, renderService, navigator);

        Assert.False(vm.IsRunning);
        Assert.Null(vm.ErrorMessage);
        Assert.NotNull(session.RenderResult);
        Assert.Equal(WorkflowStep.Completion, navigator.CurrentStep);
        Assert.Equal(session.OutputVideoPath, renderService.LastOutputFilePath);
    }

    [Fact]
    public void Construction_RenderThrowsRecognizedFailure_SetsErrorMessageAndDoesNotNavigate()
    {
        var session = BuildSessionWithRenderPlan();
        var renderService = new FakeFFmpegRenderService
        {
            ExceptionToThrow = new RenderExecutionException("hardware and software encoders both failed"),
        };
        var navigator = new WorkflowNavigator();

        var vm = new RenderProgressViewModel(session, renderService, navigator);

        Assert.False(vm.IsRunning);
        Assert.Equal("hardware and software encoders both failed", vm.ErrorMessage);
        Assert.Null(session.RenderResult);
        Assert.Equal(WorkflowStep.WelcomeImport, navigator.CurrentStep);
    }

    [Fact]
    public async Task CancelCommand_WhileRenderInProgress_StopsRunAndReportsCanceled()
    {
        var session = BuildSessionWithRenderPlan();
        var gate = new TaskCompletionSource<bool>();
        var renderService = new FakeFFmpegRenderService { Gate = gate };
        var navigator = new WorkflowNavigator();

        var vm = new RenderProgressViewModel(session, renderService, navigator);

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
