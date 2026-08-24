namespace SceneForge.Infrastructure.Persistence;

// Every on-disk path this persistence layer touches, derived from one root
// directory - constructed with an explicit root (rather than reading
// Environment.SpecialFolder itself) so tests can point every service at an
// isolated temp directory instead of the real per-machine app data folder.
// DefaultAppDataRoot is what production DI wiring (App.xaml.cs) actually
// passes.
public sealed class ProjectLayout
{
    public string AppDataRoot { get; }

    public string ProjectsRoot { get; }

    public string TempRoot { get; }

    public string LogsRoot { get; }

    public ProjectLayout(string appDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);

        AppDataRoot = Path.GetFullPath(appDataRoot);
        ProjectsRoot = Path.Combine(AppDataRoot, "Projects");
        TempRoot = Path.Combine(AppDataRoot, "Temp");
        LogsRoot = Path.Combine(AppDataRoot, "Logs");
    }

    public static string DefaultAppDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SceneForge");

    public string ProjectDirectory(Guid projectId) => Path.Combine(ProjectsRoot, projectId.ToString("N"));

    public string ProjectFilePath(Guid projectId) => Path.Combine(ProjectDirectory(projectId), "project.sfproj");

    public string InProgressMarkerPath(Guid projectId) => Path.Combine(ProjectDirectory(projectId), "project.inprogress");
}
