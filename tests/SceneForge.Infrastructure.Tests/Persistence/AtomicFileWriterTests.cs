using SceneForge.Infrastructure.Persistence;
using SceneForge.Infrastructure.Tests.TestSupport;

namespace SceneForge.Infrastructure.Tests.Persistence;

public sealed class AtomicFileWriterTests : IDisposable
{
    private readonly TempDirectoryFixture _fixture = new();

    [Fact]
    public async Task WriteAsync_NewFile_CreatesTargetWithWrittenContent()
    {
        var targetPath = Path.Combine(_fixture.Path, "new.txt");

        await AtomicFileWriter.WriteAsync(targetPath, async (stream, ct) =>
        {
            await using var writer = new StreamWriter(stream, leaveOpen: true);
            await writer.WriteAsync("hello");
        });

        Assert.True(File.Exists(targetPath));
        Assert.Equal("hello", await File.ReadAllTextAsync(targetPath));
    }

    [Fact]
    public async Task WriteAsync_ExistingFile_ReplacesContentAtomically()
    {
        var targetPath = Path.Combine(_fixture.Path, "existing.txt");
        await File.WriteAllTextAsync(targetPath, "old content");

        await AtomicFileWriter.WriteAsync(targetPath, async (stream, ct) =>
        {
            await using var writer = new StreamWriter(stream, leaveOpen: true);
            await writer.WriteAsync("new content");
        });

        Assert.Equal("new content", await File.ReadAllTextAsync(targetPath));
    }

    [Fact]
    public async Task WriteAsync_Success_LeavesNoTempFileBehind()
    {
        var targetPath = Path.Combine(_fixture.Path, "clean.txt");

        await AtomicFileWriter.WriteAsync(targetPath, async (stream, ct) =>
        {
            await using var writer = new StreamWriter(stream, leaveOpen: true);
            await writer.WriteAsync("content");
        });

        var remainingFiles = Directory.GetFiles(_fixture.Path);
        Assert.Single(remainingFiles);
        Assert.Equal(targetPath, remainingFiles[0]);
    }

    [Fact]
    public async Task WriteAsync_WriteBodyThrows_TargetFileNeverCreatedAndTempFileCleanedUp()
    {
        var targetPath = Path.Combine(_fixture.Path, "never-created.txt");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AtomicFileWriter.WriteAsync(targetPath, (stream, ct) => throw new InvalidOperationException("boom")));

        Assert.False(File.Exists(targetPath));
        Assert.Empty(Directory.GetFiles(_fixture.Path));
    }

    [Fact]
    public async Task WriteAsync_ExistingTargetAndWriteBodyThrows_OriginalContentPreserved()
    {
        var targetPath = Path.Combine(_fixture.Path, "preserved.txt");
        await File.WriteAllTextAsync(targetPath, "must survive");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AtomicFileWriter.WriteAsync(targetPath, (stream, ct) => throw new InvalidOperationException("boom")));

        Assert.Equal("must survive", await File.ReadAllTextAsync(targetPath));
    }

    // Regression test: AtomicFileWriter's own sibling temp file must never
    // be checked against an ITempFileRegistry's Temp-root allowlist, even
    // when the write target sits in a directory that is a sibling of (not
    // nested under) that registry's root - the exact shape of
    // ProjectLayout's ProjectsRoot vs. TempRoot in production. See
    // ProjectStoreTests's SaveAsync_MirrorsProductionLayoutWithSiblingTempRegistry_DoesNotThrow
    // for the full production-layout reproduction.
    [Fact]
    public async Task WriteAsync_TargetDirectoryIsSiblingOfUnrelatedTempRegistryRoot_DoesNotThrow()
    {
        var registryRoot = Path.Combine(_fixture.Path, "Temp");
        _ = new TempFileRegistry(registryRoot);
        var targetDirectory = Path.Combine(_fixture.Path, "Projects", "some-project");
        var targetPath = Path.Combine(targetDirectory, "project.sfproj");

        await AtomicFileWriter.WriteAsync(targetPath, async (stream, ct) =>
        {
            await using var writer = new StreamWriter(stream, leaveOpen: true);
            await writer.WriteAsync("hello");
        });

        Assert.True(File.Exists(targetPath));
        Assert.Equal("hello", await File.ReadAllTextAsync(targetPath));
    }

    public void Dispose() => _fixture.Dispose();
}
