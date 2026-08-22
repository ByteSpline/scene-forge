namespace SceneForge.Media.Tooling;

public sealed record FfmpegToolPaths
{
    public required string FfprobePath { get; init; }

    public required string FfmpegPath { get; init; }
}
