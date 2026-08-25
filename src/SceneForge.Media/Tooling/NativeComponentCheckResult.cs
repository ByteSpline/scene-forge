namespace SceneForge.Media.Tooling;

public sealed record NativeComponentCheckResult
{
    public required string ComponentName { get; init; }

    public required bool IsAvailable { get; init; }

    public required string Detail { get; init; }

    // Only set when IsAvailable is false - concrete, user-actionable next
    // step rather than a raw exception message (CLAUDE.md: explainable
    // processing flows, not opaque failures).
    public string? RemediationGuidance { get; init; }
}
