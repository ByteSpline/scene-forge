namespace SceneForge.Infrastructure.Persistence;

public interface IStaleSourceDetector
{
    // Throws FileNotFoundException if filePath does not currently exist.
    SourceFingerprint Capture(string filePath);

    SourceFreshnessResult CheckFreshness(SourceFingerprint recorded);
}
