using System.IO;
using SceneForge.App.Navigation;
using SceneForge.App.Session;
using SceneForge.App.Tests.TestSupport;
using SceneForge.App.ViewModels;
using SceneForge.Media.Domain;
using SceneForge.Media.Extraction;
using SceneForge.Media.Planning;
using SceneForge.Media.Rendering;

namespace SceneForge.App.Tests.ViewModels;

public sealed class ExportSettingsViewModelTests : IDisposable
{
    private readonly string _videoPath = CreateTempFile();
    private readonly string _audioPath = CreateTempFile();

    public void Dispose()
    {
        TryDelete(_videoPath);
        TryDelete(_audioPath);
    }

    [Fact]
    public void Constructor_DefaultSession_SelectsFirstResolutionAndLetterbox()
    {
        var session = BuildSessionWithPlan();

        var vm = new ExportSettingsViewModel(session, new RenderPlanBuilder(), new FakeDialogService(), new WorkflowNavigator());

        Assert.Equal(1920, vm.SelectedResolution.Width);
        Assert.Equal(1080, vm.SelectedResolution.Height);
        Assert.Equal(AspectFitMode.Letterbox, vm.SelectedFitMode);
        Assert.Null(vm.OutputVideoPath);
        Assert.Contains("30", vm.FrameRateSummary);
    }

    [Fact]
    public void BrowseOutputPathCommand_SetsOutputVideoPathFromDialog()
    {
        var session = BuildSessionWithPlan();
        var dialogService = new FakeDialogService { SavePathToReturn = @"C:\out\video.mp4" };
        var vm = new ExportSettingsViewModel(session, new RenderPlanBuilder(), dialogService, new WorkflowNavigator());

        vm.BrowseOutputPathCommand.Execute(null);

        Assert.Equal(@"C:\out\video.mp4", vm.OutputVideoPath);
    }

    [Fact]
    public void ContinueCommand_CanExecute_RequiresOutputPath()
    {
        var session = BuildSessionWithPlan();
        var vm = new ExportSettingsViewModel(session, new RenderPlanBuilder(), new FakeDialogService(), new WorkflowNavigator());
        Assert.False(vm.ContinueCommand.CanExecute(null));

        vm.OutputVideoPath = @"C:\out\video.mp4";

        Assert.True(vm.ContinueCommand.CanExecute(null));
    }

    [Fact]
    public void ContinueCommand_Execute_BuildsRenderPlanAndNavigatesToRenderProgress()
    {
        var session = BuildSessionWithPlan();
        var navigator = new WorkflowNavigator();
        var vm = new ExportSettingsViewModel(session, new RenderPlanBuilder(), new FakeDialogService(), navigator)
        {
            OutputVideoPath = @"C:\out\video.mp4",
        };

        vm.ContinueCommand.Execute(null);

        Assert.Null(vm.ErrorMessage);
        Assert.NotNull(session.RenderPlan);
        Assert.Equal(@"C:\out\video.mp4", session.OutputVideoPath);
        Assert.Equal(WorkflowStep.RenderProgress, navigator.CurrentStep);
    }

    [Fact]
    public void ContinueCommand_Execute_SourceShorterThanPlacementRequires_SetsErrorMessageAndDoesNotNavigate()
    {
        var session = BuildSessionWithPlan();
        // Shrink the probed source duration below what the plan's placements
        // actually require, forcing RenderPlanBuilder to reject the request.
        session.VideoMediaInfo = session.VideoMediaInfo! with
        {
            VideoStreams = [session.VideoMediaInfo!.VideoStreams[0] with { Duration = TimeSpan.FromSeconds(1) }],
        };
        var navigator = new WorkflowNavigator();
        var vm = new ExportSettingsViewModel(session, new RenderPlanBuilder(), new FakeDialogService(), navigator)
        {
            OutputVideoPath = @"C:\out\video.mp4",
        };

        vm.ContinueCommand.Execute(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Null(session.RenderPlan);
        Assert.Equal(WorkflowStep.WelcomeImport, navigator.CurrentStep);
    }

    private WorkflowSession BuildSessionWithPlan()
    {
        var clip = CleanClipBuilder.Build(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(3), accepted: true);
        var reviewedClips = new List<CleanClip> { clip };

        var plan = new TimelinePlanner().Plan(new TimelinePlanRequest
        {
            AvailableClips = reviewedClips,
            TargetAudioDuration = TimeSpan.FromSeconds(3),
            OutputTimeBase = new RationalFrameRate(30, 1),
            Seed = 1,
        });

        return new WorkflowSession
        {
            VideoFilePath = _videoPath,
            AudioFilePath = _audioPath,
            VideoMediaInfo = MediaInfoBuilder.Video(_videoPath, TimeSpan.FromSeconds(30)),
            AudioMediaInfo = MediaInfoBuilder.Audio(_audioPath, TimeSpan.FromSeconds(3)),
            OutputFrameRate = new RationalFrameRate(30, 1),
            ReviewedClips = reviewedClips,
            TimelinePlan = plan,
        };
    }

    private static string CreateTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sceneforge-test-{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(path, [0]);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
