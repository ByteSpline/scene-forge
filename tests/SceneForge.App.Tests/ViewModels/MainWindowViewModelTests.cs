using Microsoft.Extensions.DependencyInjection;
using SceneForge.App.Navigation;
using SceneForge.App.Persistence;
using SceneForge.App.Services;
using SceneForge.App.Session;
using SceneForge.App.Tests.TestSupport;
using SceneForge.App.ViewModels;
using SceneForge.Media.Extraction;
using SceneForge.Media.Planning;
using SceneForge.Media.Probing;
using SceneForge.Media.Rendering;

namespace SceneForge.App.Tests.ViewModels;

// MainWindowViewModel is the shell that resolves one ViewModel per workflow
// step from DI on every IWorkflowNavigator.StepChanged - this exercises that
// resolution end to end (every step's real ViewModel type, wired against
// fakes for every Media/App-layer dependency) rather than any one step's own
// logic (covered by that step's own ViewModel test class).
public class MainWindowViewModelTests
{
    [Theory]
    [InlineData(WorkflowStep.WelcomeImport, typeof(WelcomeImportViewModel))]
    [InlineData(WorkflowStep.AnalysisSettings, typeof(AnalysisSettingsViewModel))]
    [InlineData(WorkflowStep.AnalysisProgress, typeof(AnalysisProgressViewModel))]
    [InlineData(WorkflowStep.SceneReview, typeof(SceneReviewViewModel))]
    [InlineData(WorkflowStep.TimelineSummary, typeof(TimelineSummaryViewModel))]
    [InlineData(WorkflowStep.ExportSettings, typeof(ExportSettingsViewModel))]
    [InlineData(WorkflowStep.RenderProgress, typeof(RenderProgressViewModel))]
    [InlineData(WorkflowStep.Completion, typeof(CompletionViewModel))]
    public void OnStepChanged_EveryWorkflowStep_ResolvesTheMatchingViewModelType(WorkflowStep step, Type expectedViewModelType)
    {
        using var provider = BuildServiceProvider();
        var navigator = provider.GetRequiredService<IWorkflowNavigator>();
        var shell = provider.GetRequiredService<MainWindowViewModel>();

        navigator.NavigateTo(step);

        Assert.IsType(expectedViewModelType, shell.CurrentViewModel);
        Assert.Equal(step, shell.CurrentStep);
    }

    [Theory]
    [InlineData(WorkflowStep.RenderProgress)]
    [InlineData(WorkflowStep.Completion)]
    public void IsBackAllowed_OnRenderProgressOrCompletion_IsFalseEvenWithHistory(WorkflowStep step)
    {
        using var provider = BuildServiceProvider();
        var navigator = provider.GetRequiredService<IWorkflowNavigator>();
        var shell = provider.GetRequiredService<MainWindowViewModel>();

        navigator.NavigateTo(WorkflowStep.AnalysisSettings);
        navigator.NavigateTo(step);

        Assert.False(shell.IsBackAllowed);
    }

    [Fact]
    public void IsBackAllowed_OnAnOrdinaryStepWithHistory_IsTrue()
    {
        using var provider = BuildServiceProvider();
        var navigator = provider.GetRequiredService<IWorkflowNavigator>();
        var shell = provider.GetRequiredService<MainWindowViewModel>();

        navigator.NavigateTo(WorkflowStep.AnalysisSettings);

        Assert.True(shell.IsBackAllowed);
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IFfprobeService>(new FakeFfprobeService());
        services.AddSingleton<SceneForge.Media.Detection.ITransitionDetector>(new FakeTransitionDetector());
        services.AddSingleton<ICleanClipExtractor>(new FakeCleanClipExtractor());
        services.AddSingleton<ITimelinePlanner, TimelinePlanner>();
        services.AddSingleton<IRenderPlanBuilder, RenderPlanBuilder>();
        services.AddSingleton<IFFmpegRenderService>(new FakeFFmpegRenderService());

        services.AddSingleton<IDialogService, FakeDialogService>();
        services.AddSingleton<IThumbnailCacheService, FakeThumbnailCacheService>();
        services.AddSingleton<IWorkflowNavigator, WorkflowNavigator>();
        services.AddSingleton<WorkflowSession>();
        services.AddSingleton<IProjectPersistenceCoordinator, FakeProjectPersistenceCoordinator>();

        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<WelcomeImportViewModel>();
        services.AddTransient<AnalysisSettingsViewModel>();
        services.AddTransient<AnalysisProgressViewModel>();
        services.AddTransient<SceneReviewViewModel>();
        services.AddTransient<TimelineSummaryViewModel>();
        services.AddTransient<ExportSettingsViewModel>();
        services.AddTransient<RenderProgressViewModel>();
        services.AddTransient<CompletionViewModel>();

        return services.BuildServiceProvider();
    }
}
