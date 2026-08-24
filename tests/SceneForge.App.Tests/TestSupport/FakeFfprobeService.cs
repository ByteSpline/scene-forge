using SceneForge.Media.Domain;
using SceneForge.Media.Probing;

namespace SceneForge.App.Tests.TestSupport;

internal sealed class FakeFfprobeService : IFfprobeService
{
    public Dictionary<string, MediaInfo> ResultsByPath { get; } = [];

    public Task<MediaInfo> ProbeAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!ResultsByPath.TryGetValue(filePath, out var info))
        {
            throw new InvalidOperationException($"No fake probe result configured for '{filePath}'.");
        }

        return Task.FromResult(info);
    }
}
