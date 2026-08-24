using SceneForge.Media.Domain;
using SceneForge.Media.Rendering;

namespace SceneForge.Infrastructure.Persistence;

public sealed record RenderSettingsRecord
{
    public required string OutputVideoPath { get; init; }

    public required AspectFitMode FitMode { get; init; }

    public required int OutputWidth { get; init; }

    public required int OutputHeight { get; init; }

    public required RationalFrameRate OutputFrameRate { get; init; }
}
