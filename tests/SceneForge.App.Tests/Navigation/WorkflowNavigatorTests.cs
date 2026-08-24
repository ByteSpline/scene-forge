using SceneForge.App.Navigation;

namespace SceneForge.App.Tests.Navigation;

public class WorkflowNavigatorTests
{
    [Fact]
    public void CurrentStep_Initially_IsWelcomeImport()
    {
        var navigator = new WorkflowNavigator();

        Assert.Equal(WorkflowStep.WelcomeImport, navigator.CurrentStep);
        Assert.False(navigator.CanGoBack);
    }

    [Fact]
    public void NavigateTo_DifferentStep_UpdatesCurrentStepAndRaisesEvent()
    {
        var navigator = new WorkflowNavigator();
        WorkflowStep? raised = null;
        navigator.StepChanged += (_, step) => raised = step;

        navigator.NavigateTo(WorkflowStep.AnalysisSettings);

        Assert.Equal(WorkflowStep.AnalysisSettings, navigator.CurrentStep);
        Assert.Equal(WorkflowStep.AnalysisSettings, raised);
        Assert.True(navigator.CanGoBack);
    }

    [Fact]
    public void NavigateTo_SameStep_IsNoOp()
    {
        var navigator = new WorkflowNavigator();
        var raiseCount = 0;
        navigator.StepChanged += (_, _) => raiseCount++;

        navigator.NavigateTo(WorkflowStep.WelcomeImport);

        Assert.Equal(0, raiseCount);
        Assert.False(navigator.CanGoBack);
    }

    [Fact]
    public void GoBack_AfterForwardNavigation_ReturnsToPreviousStepInOrder()
    {
        var navigator = new WorkflowNavigator();
        navigator.NavigateTo(WorkflowStep.AnalysisSettings);
        navigator.NavigateTo(WorkflowStep.AnalysisProgress);
        navigator.NavigateTo(WorkflowStep.SceneReview);

        navigator.GoBack();
        Assert.Equal(WorkflowStep.AnalysisProgress, navigator.CurrentStep);

        navigator.GoBack();
        Assert.Equal(WorkflowStep.AnalysisSettings, navigator.CurrentStep);

        navigator.GoBack();
        Assert.Equal(WorkflowStep.WelcomeImport, navigator.CurrentStep);
        Assert.False(navigator.CanGoBack);
    }

    [Fact]
    public void GoBack_WithNoHistory_IsNoOp()
    {
        var navigator = new WorkflowNavigator();

        navigator.GoBack();

        Assert.Equal(WorkflowStep.WelcomeImport, navigator.CurrentStep);
    }

    [Fact]
    public void Reset_AfterNavigating_ReturnsToWelcomeImportAndClearsHistory()
    {
        var navigator = new WorkflowNavigator();
        navigator.NavigateTo(WorkflowStep.Completion);

        navigator.Reset();

        Assert.Equal(WorkflowStep.WelcomeImport, navigator.CurrentStep);
        Assert.False(navigator.CanGoBack);
    }
}
