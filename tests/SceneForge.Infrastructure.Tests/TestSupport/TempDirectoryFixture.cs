namespace SceneForge.Infrastructure.Tests.TestSupport;

// One isolated, uniquely-named directory under the OS temp path per test
// instance - never the real per-machine app data folder - so persistence
// tests can freely write/rotate/delete files without touching anything a
// real SceneForge installation owns. Deleted recursively on Dispose.
public sealed class TempDirectoryFixture : IDisposable
{
    public string Path { get; }

    public TempDirectoryFixture()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SceneForgeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup only - a locked file left by a failed
            // test should not fail the whole run.
        }
    }
}
