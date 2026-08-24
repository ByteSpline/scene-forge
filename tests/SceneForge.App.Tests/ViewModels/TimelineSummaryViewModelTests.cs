using SceneForge.App.Navigation;
using SceneForge.App.Session;
using SceneForge.App.Tests.TestSupport;
using SceneForge.App.ViewModels;
using SceneForge.Media.Domain;
using SceneForge.Media.Planning;

namespace SceneForge.App.Tests.ViewModels;

public class TimelineSummaryViewModelTests
{
    [Fact]
    public void Constructor_NoReviewedClips_SetsErrorMessageAndDisablesContinue()
    {
        var session = new WorkflowSession { AudioMediaInfo = MediaInfoBuilder.Audio("audio.m4a", TimeSpan.FromSeconds(6)) };

        var vm = new TimelineSummaryViewModel(session, new TimelinePlanner(), new WorkflowNavigator());

        Assert.NotNull(vm.ErrorMessage);
        Assert.False(vm.ContinueCommand.CanExecute(null));
        Assert.Null(session.TimelinePlan);
    }

    [Fact]
    public void Constructor_ReviewedClipsCoveringTargetDuration_BuildsCompletePlan()
    {
        var session = BuildSessionWithReviewedClips();

        var vm = new TimelineSummaryViewModel(session, new TimelinePlanner(), new WorkflowNavigator());

        Assert.Null(vm.ErrorMessage);
        Assert.NotNull(session.TimelinePlan);
        Assert.True(vm.IsComplete);
        Assert.Null(vm.FeasibilityWarning);
        Assert.Equal(3, vm.Placements.Count);
        Assert.True(vm.ContinueCommand.CanExecute(null));
    }

    [Fact]
    public void ReshuffleCommand_IncrementsSeedAndRebuildsPlanIntoSession()
    {
        var session = BuildSessionWithReviewedClips();
        var vm = new TimelineSummaryViewModel(session, new TimelinePlanner(), new WorkflowNavigator());
        var originalSeed = vm.Seed;

        vm.ReshuffleCommand.Execute(null);

        Assert.Equal(originalSeed + 1, vm.Seed);
        Assert.Equal(vm.Seed, session.Seed);
        Assert.NotNull(session.TimelinePlan);
    }

    [Fact]
    public void ContinueCommand_Execute_NavigatesToExportSettings()
    {
        var session = BuildSessionWithReviewedClips();
        var navigator = new WorkflowNavigator();
        var vm = new TimelineSummaryViewModel(session, new TimelinePlanner(), navigator);

        vm.ContinueCommand.Execute(null);

        Assert.Equal(WorkflowStep.ExportSettings, navigator.CurrentStep);
    }

    private static WorkflowSession BuildSessionWithReviewedClips()
    {
        var clips = new List<SceneForge.Media.Extraction.CleanClip>
        {
            CleanClipBuilder.Build(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2), accepted: true, sourceSceneIndex: 0),
            CleanClipBuilder.Build(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(12), accepted: true, sourceSceneIndex: 1),
            CleanClipBuilder.Build(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(22), accepted: true, sourceSceneIndex: 2),
        };

        return new WorkflowSession
        {
            ReviewedClips = clips,
            AudioMediaInfo = MediaInfoBuilder.Audio("audio.m4a", TimeSpan.FromSeconds(6)),
            OutputFrameRate = new RationalFrameRate(30, 1),
            Seed = 1,
        };
    }
}
