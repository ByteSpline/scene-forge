namespace SceneForge.App.Navigation;

// Drives which of the eight workflow screens is current. A single, App-
// lifetime singleton (registered in App.xaml.cs) shared by every step
// ViewModel and by MainWindowViewModel, which is the only thing that
// actually reacts to StepChanged (see MainWindowViewModel.OnStepChanged).
public interface IWorkflowNavigator
{
    WorkflowStep CurrentStep { get; }

    bool CanGoBack { get; }

    event EventHandler<WorkflowStep>? StepChanged;

    void NavigateTo(WorkflowStep targetStep);

    void GoBack();

    // Returns to WelcomeImport and clears back-navigation history - used by
    // Completion's "Start over" command. Does not itself clear
    // Session.WorkflowSession; callers reset session state separately (see
    // CompletionViewModel).
    void Reset();
}
