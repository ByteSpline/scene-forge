using SceneForge.Media.Domain;

namespace SceneForge.Media.Probing;

public interface IFfprobeService
{
    Task<MediaInfo> ProbeAsync(string filePath, CancellationToken cancellationToken);
}
