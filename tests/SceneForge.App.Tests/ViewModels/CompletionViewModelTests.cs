using SceneForge.App.Navigation;
using SceneForge.App.Session;
using SceneForge.App.Tests.TestSupport;
using SceneForge.App.ViewModels;
using SceneForge.Media.Rendering;

namespace SceneForge.App.Tests.ViewModels;

public class CompletionViewModelTests
{
    [Fact]
    public void Constructor_WithRenderResult_ReflectsEncoderAndVerification()
    {
        var session = new WorkflowSession
        {
            OutputVideoPath = @"C:\out\video.mp4",
            RenderResult = new RenderResult
            {
                OutputFilePath = @"C:\out\video.mp4",
                Encoder = new VideoEncoderSelection { Kind = VideoEncoderKind.SoftwareX264, FfmpegEncoderName = "libx264", IsHardwareAccelerated = false },
                FellBackToSoftwareEncoder = true,
                Elapsed = TimeSpan.FromSeconds(12),
                Verification = new RenderVerificationResult
                {
                    HasExpectedVideoStream = true,
                    HasExactlyOneAudioStream = true,
                    ExpectedDuration = TimeSpan.FromSeconds(3),
                    ActualDuration = TimeSpan.FromSeconds(3),
                    DurationDelta = TimeSpan.Zero,
                    DurationTolerance = TimeSpan.FromMilliseconds(50),
                    DurationWithinTolerance = true,
                    FirstFrameDecodable = true,
                    MiddleFrameDecodable = true,
                    LastFrameDecodable = true,
                },
            },
        };

        var vm = new CompletionViewModel(session, new FakeDialogService(), new WorkflowNavigator());

        Assert.Equal(@"C:\out\video.mp4", vm.OutputFilePath);
        Assert.Contains("libx264", vm.EncoderDescription);
        Assert.Contains("software", vm.EncoderDescription);
        Assert.True(vm.FellBackToSoftwareEncoder);
        Assert.Equal(TimeSpan.FromSeconds(12), vm.Elapsed);
        Assert.True(vm.VerificationPassed);
        Assert.Empty(vm.VerificationFailures);
    }

    [Fact]
    public void Constructor_WithoutRenderResult_FallsBackToOutputPathAndUnknownEncoder()
    {
        var session = new WorkflowSession { OutputVideoPath = @"C:\out\video.mp4" };

        var vm = new CompletionViewModel(session, new FakeDialogService(), new WorkflowNavigator());

        Assert.Equal(@"C:\out\video.mp4", vm.OutputFilePath);
        Assert.Equal("Unknown", vm.EncoderDescription);
        Assert.False(vm.VerificationPassed);
    }

    [Fact]
    public void OpenOutputFolderCommand_RevealsOutputFilePathInDialogService()
    {
        var session = new WorkflowSession { OutputVideoPath = @"C:\out\video.mp4" };
        var dialogService = new FakeDialogService();
        var vm = new CompletionViewModel(session, dialogService, new WorkflowNavigator());

        vm.OpenOutputFolderCommand.Execute(null);

        Assert.Equal([@"C:\out\video.mp4"], dialogService.RevealedPaths);
    }

    [Fact]
    public void StartOverCommand_ResetsSessionAndNavigator()
    {
        var session = new WorkflowSession { OutputVideoPath = @"C:\out\video.mp4", Seed = 99 };
        var navigator = new WorkflowNavigator();
        navigator.NavigateTo(WorkflowStep.Completion);
        var vm = new CompletionViewModel(session, new FakeDialogService(), navigator);

        vm.StartOverCommand.Execute(null);

        Assert.Null(session.OutputVideoPath);
        Assert.Equal(1, session.Seed);
        Assert.Equal(WorkflowStep.WelcomeImport, navigator.CurrentStep);
        Assert.False(navigator.CanGoBack);
    }
}
