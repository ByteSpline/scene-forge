using SceneForge.Infrastructure.Persistence;
using SceneForge.Infrastructure.Tests.TestSupport;

namespace SceneForge.Infrastructure.Tests.Persistence;

public sealed class TempFileRegistryTests : IDisposable
{
    private readonly TempDirectoryFixture _fixture = new();

    [Fact]
    public void Register_PathOutsideRoot_Throws()
    {
        var registry = new TempFileRegistry(Path.Combine(_fixture.Path, "root"));
        var outsidePath = Path.Combine(_fixture.Path, "outside", "file.tmp");

        Assert.Throws<InvalidOperationException>(() => registry.Register(outsidePath));
    }

    [Fact]
    public async Task CleanupAsync_DeletesRegisteredFilesAndClearsRegistry()
    {
        var registry = new TempFileRegistry(_fixture.Path);
        var filePath = Path.Combine(_fixture.Path, "temp.dat");
        File.WriteAllText(filePath, "data");
        registry.Register(filePath);

        await registry.CleanupAsync();

        Assert.False(File.Exists(filePath));
        Assert.Empty(registry.RegisteredFiles);
    }

    [Fact]
    public async Task CleanupAsync_RegisteredFileAlreadyDeleted_DoesNotThrow()
    {
        var registry = new TempFileRegistry(_fixture.Path);
        var filePath = Path.Combine(_fixture.Path, "already-gone.dat");
        registry.Register(filePath);

        await registry.CleanupAsync();

        Assert.Empty(registry.RegisteredFiles);
    }

    [Fact]
    public void Unregister_RemovesFromRegisteredFiles()
    {
        var registry = new TempFileRegistry(_fixture.Path);
        var filePath = Path.Combine(_fixture.Path, "file.dat");
        registry.Register(filePath);

        registry.Unregister(filePath);

        Assert.Empty(registry.RegisteredFiles);
    }

    [Fact]
    public async Task SweepOrphansAsync_DeletesUnregisteredFile_LeavesRegisteredFileAlone()
    {
        var registry = new TempFileRegistry(_fixture.Path);
        var orphanPath = Path.Combine(_fixture.Path, "orphan.dat");
        var registeredPath = Path.Combine(_fixture.Path, "registered.dat");
        File.WriteAllText(orphanPath, "orphan");
        File.WriteAllText(registeredPath, "kept");
        registry.Register(registeredPath);

        await registry.SweepOrphansAsync();

        Assert.False(File.Exists(orphanPath));
        Assert.True(File.Exists(registeredPath));
    }

    [Fact]
    public void ManifestPersistsAcrossInstances()
    {
        var filePath = Path.Combine(_fixture.Path, "survives-restart.dat");

        var first = new TempFileRegistry(_fixture.Path);
        first.Register(filePath);

        var second = new TempFileRegistry(_fixture.Path);

        Assert.Contains(filePath, second.RegisteredFiles);
    }

    [Fact]
    public async Task SweepOrphansAsync_NeverEnumeratesOutsideRootDirectory()
    {
        var rootPath = Path.Combine(_fixture.Path, "root");
        var registry = new TempFileRegistry(rootPath);
        var outsideFile = Path.Combine(_fixture.Path, "sibling.dat");
        File.WriteAllText(outsideFile, "must survive");

        await registry.SweepOrphansAsync();

        Assert.True(File.Exists(outsideFile));
    }

    public void Dispose() => _fixture.Dispose();
}
