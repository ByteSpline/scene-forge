namespace SceneForge.App.Navigation;

public sealed class WorkflowNavigator : IWorkflowNavigator
{
    private readonly Stack<WorkflowStep> _history = new();

    public WorkflowStep CurrentStep { get; private set; } = WorkflowStep.WelcomeImport;

    public bool CanGoBack => _history.Count > 0;

    public event EventHandler<WorkflowStep>? StepChanged;

    public void NavigateTo(WorkflowStep targetStep)
    {
        if (targetStep == CurrentStep)
        {
            return;
        }

        _history.Push(CurrentStep);
        CurrentStep = targetStep;
        StepChanged?.Invoke(this, targetStep);
    }

    public void GoBack()
    {
        if (!CanGoBack)
        {
            return;
        }

        CurrentStep = _history.Pop();
        StepChanged?.Invoke(this, CurrentStep);
    }

    public void Reset()
    {
        _history.Clear();
        CurrentStep = WorkflowStep.WelcomeImport;
        StepChanged?.Invoke(this, CurrentStep);
    }
}
