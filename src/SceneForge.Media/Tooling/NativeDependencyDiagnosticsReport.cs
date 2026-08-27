namespace SceneForge.Media.Tooling;

public sealed record NativeDependencyDiagnosticsReport
{
    public required IReadOnlyList<NativeComponentCheckResult> Results { get; init; }

    public bool AllPassed => Results.All(r => r.IsAvailable);
}
