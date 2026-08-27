using SceneForge.App.Tests.TestSupport;
using SceneForge.App.ViewModels;

namespace SceneForge.App.Tests.ViewModels;

public class StartupDiagnosticsViewModelTests
{
    [Fact]
    public void Construction_AllChecksPass_PopulatesResultsAndAllPassed()
    {
        var service = new FakeNativeDependencyDiagnosticsService(FakeNativeDependencyDiagnosticsService.Passing());

        var vm = new StartupDiagnosticsViewModel(service);

        Assert.False(vm.IsRunning);
        Assert.True(vm.HasRun);
        Assert.True(vm.AllPassed);
        Assert.Equal(3, vm.Results.Count);
        Assert.Equal(1, service.CallCount);
    }

    [Fact]
    public void Construction_ACheckFails_AllPassedIsFalse()
    {
        var service = new FakeNativeDependencyDiagnosticsService(FakeNativeDependencyDiagnosticsService.Failing());

        var vm = new StartupDiagnosticsViewModel(service);

        Assert.True(vm.HasRun);
        Assert.False(vm.AllPassed);
        Assert.Contains(vm.Results, r => !r.IsAvailable);
    }

    [Fact]
    public void RetryCommand_RunsDiagnosticsAgainAndReplacesResults()
    {
        var service = new FakeNativeDependencyDiagnosticsService(
            FakeNativeDependencyDiagnosticsService.Failing(),
            FakeNativeDependencyDiagnosticsService.Passing());
        var vm = new StartupDiagnosticsViewModel(service);
        Assert.False(vm.AllPassed);

        vm.RetryCommand.Execute(null);

        Assert.True(vm.AllPassed);
        Assert.Equal(3, vm.Results.Count);
        Assert.Equal(2, service.CallCount);
    }
}
