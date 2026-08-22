using SceneForge.Media.Validation;

namespace SceneForge.Media.Tests.Validation;

public sealed class OutputDirectoryValidatorTests : IDisposable
{
    private readonly DirectoryInfo _tempDirectory = Directory.CreateTempSubdirectory("sceneforge-output-");

    public void Dispose() => _tempDirectory.Delete(recursive: true);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EnsureWritable_NullOrWhitespace_ThrowsInvalidPath(string? path)
    {
        var exception = Assert.Throws<MediaValidationException>(() => OutputDirectoryValidator.EnsureWritable(path!));

        Assert.Equal(MediaValidationFailureReason.InvalidPath, exception.Reason);
    }

    [Fact]
    public void EnsureWritable_ExistingWritableDirectory_ReturnsFullPath()
    {
        var result = OutputDirectoryValidator.EnsureWritable(_tempDirectory.FullName);

        Assert.Equal(Path.GetFullPath(_tempDirectory.FullName), result);
    }

    [Fact]
    public void EnsureWritable_DirectoryDoesNotExist_CreatesIt()
    {
        var newDirectory = Path.Combine(_tempDirectory.FullName, "nested", "output");

        var result = OutputDirectoryValidator.EnsureWritable(newDirectory);

        Assert.True(Directory.Exists(result));
    }

    [Fact]
    public void EnsureWritable_DoesNotLeaveProbeFileBehind()
    {
        OutputDirectoryValidator.EnsureWritable(_tempDirectory.FullName);

        Assert.Empty(Directory.GetFiles(_tempDirectory.FullName));
    }

    [Fact]
    public void EnsureWritable_PathIsAFile_ThrowsPathIsDirectory()
    {
        var filePath = Path.Combine(_tempDirectory.FullName, "not-a-directory.txt");
        File.WriteAllBytes(filePath, [1]);

        var exception = Assert.Throws<MediaValidationException>(() => OutputDirectoryValidator.EnsureWritable(filePath));

        Assert.Equal(MediaValidationFailureReason.PathIsDirectory, exception.Reason);
    }

    [Fact]
    public void EnsureDoesNotOverwriteInput_SamePath_Throws()
    {
        var filePath = Path.Combine(_tempDirectory.FullName, "clip.mp4");

        var exception = Assert.Throws<MediaValidationException>(() => OutputDirectoryValidator.EnsureDoesNotOverwriteInput(filePath, filePath));

        Assert.Equal(MediaValidationFailureReason.OutputWouldOverwriteInput, exception.Reason);
    }

    [Fact]
    public void EnsureDoesNotOverwriteInput_SamePathDifferentCasing_Throws()
    {
        var filePath = Path.Combine(_tempDirectory.FullName, "clip.mp4");
        var upperCasePath = filePath.ToUpperInvariant();

        var exception = Assert.Throws<MediaValidationException>(() => OutputDirectoryValidator.EnsureDoesNotOverwriteInput(upperCasePath, filePath));

        Assert.Equal(MediaValidationFailureReason.OutputWouldOverwriteInput, exception.Reason);
    }

    [Fact]
    public void EnsureDoesNotOverwriteInput_DifferentPaths_DoesNotThrow()
    {
        var inputPath = Path.Combine(_tempDirectory.FullName, "input.mp4");
        var outputPath = Path.Combine(_tempDirectory.FullName, "output.mp4");

        OutputDirectoryValidator.EnsureDoesNotOverwriteInput(outputPath, inputPath);
    }
}
