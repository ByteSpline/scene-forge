using SceneForge.Infrastructure.Persistence;
using SceneForge.Infrastructure.Tests.TestSupport;

namespace SceneForge.Infrastructure.Tests.Persistence;

public sealed class StaleSourceDetectorTests : IDisposable
{
    private readonly TempDirectoryFixture _fixture = new();
    private readonly StaleSourceDetector _detector = new();

    [Fact]
    public void Capture_MissingFile_ThrowsFileNotFoundException()
    {
        var path = Path.Combine(_fixture.Path, "missing.mp4");

        Assert.Throws<FileNotFoundException>(() => _detector.Capture(path));
    }

    [Fact]
    public void Capture_ExistingFile_ReturnsMatchingFingerprint()
    {
        var path = Path.Combine(_fixture.Path, "source.mp4");
        File.WriteAllBytes(path, [1, 2, 3, 4]);

        var fingerprint = _detector.Capture(path);

        Assert.Equal(4, fingerprint.SizeBytes);
        Assert.Equal(new FileInfo(path).LastWriteTimeUtc, fingerprint.LastWriteTimeUtc);
    }

    [Fact]
    public void CheckFreshness_FileUnchanged_ReturnsFresh()
    {
        var path = Path.Combine(_fixture.Path, "unchanged.mp4");
        File.WriteAllBytes(path, [1, 2, 3]);
        var fingerprint = _detector.Capture(path);

        var result = _detector.CheckFreshness(fingerprint);

        Assert.Equal(SourceFreshnessStatus.Fresh, result.Status);
        Assert.False(result.IsStale);
    }

    [Fact]
    public void CheckFreshness_FileNoLongerExists_ReturnsMissing()
    {
        var path = Path.Combine(_fixture.Path, "will-vanish.mp4");
        File.WriteAllBytes(path, [1, 2, 3]);
        var fingerprint = _detector.Capture(path);
        File.Delete(path);

        var result = _detector.CheckFreshness(fingerprint);

        Assert.Equal(SourceFreshnessStatus.Missing, result.Status);
        Assert.True(result.IsStale);
    }

    [Fact]
    public void CheckFreshness_FileSizeChanged_ReturnsChanged()
    {
        var path = Path.Combine(_fixture.Path, "resized.mp4");
        File.WriteAllBytes(path, [1, 2, 3]);
        var fingerprint = _detector.Capture(path);
        File.WriteAllBytes(path, [1, 2, 3, 4, 5]);

        var result = _detector.CheckFreshness(fingerprint);

        Assert.Equal(SourceFreshnessStatus.Changed, result.Status);
    }

    [Fact]
    public void CheckFreshness_LastWriteTimeChangedButSizeSame_ReturnsChanged()
    {
        var path = Path.Combine(_fixture.Path, "retouched.mp4");
        File.WriteAllBytes(path, [1, 2, 3]);
        var fingerprint = _detector.Capture(path);
        File.SetLastWriteTimeUtc(path, fingerprint.LastWriteTimeUtc.UtcDateTime.AddHours(1));

        var result = _detector.CheckFreshness(fingerprint);

        Assert.Equal(SourceFreshnessStatus.Changed, result.Status);
    }

    public void Dispose() => _fixture.Dispose();
}
