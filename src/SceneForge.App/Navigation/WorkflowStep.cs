namespace SceneForge.App.Navigation;

// The eight screens of the required workflow, in the fixed order the phase
// brief specifies. MainWindowViewModel resolves exactly one ViewModel per
// step from the DI container each time IWorkflowNavigator raises StepChanged
// - see WorkflowNavigator.
public enum WorkflowStep
{
    WelcomeImport,
    AnalysisSettings,
    AnalysisProgress,
    SceneReview,
    TimelineSummary,
    ExportSettings,
    RenderProgress,
    Completion,
}
