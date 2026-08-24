using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SceneForge.App.Navigation;

namespace SceneForge.App.ViewModels;

// The application shell: owns which step ViewModel is currently displayed
// and the always-visible chrome (step indicator, back button). Resolves a
// fresh step ViewModel from the DI container every time
// IWorkflowNavigator.StepChanged fires, rather than caching instances - each
// step ViewModel re-reads whatever it needs from the shared
// Session.WorkflowSession in its own constructor, so state survives
// navigation even though the ViewModel instance itself does not (see
// Session.WorkflowSession's remarks).
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWorkflowNavigator _navigator;

    [ObservableProperty]
    private object? currentViewModel;

    [ObservableProperty]
    private WorkflowStep currentStep;

    [ObservableProperty]
    private bool isBackAllowed;

    public MainWindowViewModel(IServiceProvider serviceProvider, IWorkflowNavigator navigator)
    {
        _serviceProvider = serviceProvider;
        _navigator = navigator;
        _navigator.StepChanged += OnStepChanged;
        OnStepChanged(this, _navigator.CurrentStep);
    }

    [RelayCommand]
    private void GoBack() => _navigator.GoBack();

    private void OnStepChanged(object? sender, WorkflowStep step)
    {
        CurrentStep = step;

        // Interrupting an in-progress render or re-visiting a finished
        // workflow via the shell's back button would leave session state
        // inconsistent with what the current screen shows - both are
        // reachable only by the screen's own Cancel/Start-over commands.
        IsBackAllowed = _navigator.CanGoBack
            && step != WorkflowStep.RenderProgress
            && step != WorkflowStep.Completion;

        CurrentViewModel = step switch
        {
            WorkflowStep.WelcomeImport => _serviceProvider.GetRequiredService<WelcomeImportViewModel>(),
            WorkflowStep.AnalysisSettings => _serviceProvider.GetRequiredService<AnalysisSettingsViewModel>(),
            WorkflowStep.AnalysisProgress => _serviceProvider.GetRequiredService<AnalysisProgressViewModel>(),
            WorkflowStep.SceneReview => _serviceProvider.GetRequiredService<SceneReviewViewModel>(),
            WorkflowStep.TimelineSummary => _serviceProvider.GetRequiredService<TimelineSummaryViewModel>(),
            WorkflowStep.ExportSettings => _serviceProvider.GetRequiredService<ExportSettingsViewModel>(),
            WorkflowStep.RenderProgress => _serviceProvider.GetRequiredService<RenderProgressViewModel>(),
            WorkflowStep.Completion => _serviceProvider.GetRequiredService<CompletionViewModel>(),
            _ => throw new InvalidOperationException($"Unhandled workflow step '{step}'."),
        };
    }
}
