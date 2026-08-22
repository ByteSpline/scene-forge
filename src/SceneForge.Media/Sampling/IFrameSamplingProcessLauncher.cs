namespace SceneForge.Media.Sampling;

internal interface IFrameSamplingProcessLauncher
{
    Task<IFrameSamplingProcess> StartAsync(string ffmpegPath, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}
