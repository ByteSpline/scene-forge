using SceneForge.App.Navigation;
using SceneForge.App.Session;
using SceneForge.App.ViewModels;
using SceneForge.Media.Domain;
using SceneForge.Media.Sampling;

namespace SceneForge.App.Tests.ViewModels;

public class AnalysisSettingsViewModelTests
{
    [Fact]
    public void Constructor_DefaultSession_SelectsBalancedProfileAndThirtyFps()
    {
        var session = new WorkflowSession();

        var vm = new AnalysisSettingsViewModel(session, new WorkflowNavigator());

        Assert.Equal(AnalysisProfile.Balanced, vm.SelectedProfile);
        Assert.Equal(new RationalFrameRate(30, 1), vm.SelectedFrameRate.Value);
        Assert.Equal(1, vm.Seed);
    }

    [Fact]
    public void StartAnalysisCommand_CommitsSelectionsToSessionAndNavigates()
    {
        var session = new WorkflowSession();
        var navigator = new WorkflowNavigator();
        var vm = new AnalysisSettingsViewModel(session, navigator)
        {
            SelectedProfile = AnalysisProfile.Accurate,
            Seed = 42,
        };
        vm.SelectedFrameRate = vm.AvailableFrameRates.Single(f => f.Value.Equals(new RationalFrameRate(60, 1)));

        vm.StartAnalysisCommand.Execute(null);

        Assert.Equal(AnalysisProfile.Accurate, session.AnalysisProfile);
        Assert.Equal(42, session.Seed);
        Assert.Equal(new RationalFrameRate(60, 1), session.OutputFrameRate);
        Assert.Equal(WorkflowStep.AnalysisProgress, navigator.CurrentStep);
    }
}
