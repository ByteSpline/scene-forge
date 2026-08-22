using SceneForge.Media.Validation;

namespace SceneForge.Media.Tests.Validation;

public sealed class MediaPathValidatorTests : IDisposable
{
    private readonly DirectoryInfo _tempDirectory = Directory.CreateTempSubdirectory("sceneforge-validate-");

    public void Dispose() => _tempDirectory.Delete(recursive: true);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateInputFile_NullOrWhitespace_ThrowsInvalidPath(string? path)
    {
        var exception = Assert.Throws<MediaValidationException>(() => MediaPathValidator.ValidateInputFile(path!));

        Assert.Equal(MediaValidationFailureReason.InvalidPath, exception.Reason);
    }

    [Fact]
    public void ValidateInputFile_FileDoesNotExist_ThrowsFileNotFound()
    {
        var missingPath = Path.Combine(_tempDirectory.FullName, "missing.mp4");

        var exception = Assert.Throws<MediaValidationException>(() => MediaPathValidator.ValidateInputFile(missingPath));

        Assert.Equal(MediaValidationFailureReason.FileNotFound, exception.Reason);
    }

    [Fact]
    public void ValidateInputFile_PathIsDirectory_ThrowsPathIsDirectory()
    {
        var exception = Assert.Throws<MediaValidationException>(() => MediaPathValidator.ValidateInputFile(_tempDirectory.FullName));

        Assert.Equal(MediaValidationFailureReason.PathIsDirectory, exception.Reason);
    }

    [Fact]
    public void ValidateInputFile_ExistingFile_ReturnsFullPath()
    {
        var filePath = Path.Combine(_tempDirectory.FullName, "clip.mp4");
        File.WriteAllBytes(filePath, [1, 2, 3]);

        var result = MediaPathValidator.ValidateInputFile(filePath);

        Assert.Equal(Path.GetFullPath(filePath), result);
    }

    [Fact]
    public void ValidateInputFile_RelativePath_ResolvesToFullPath()
    {
        var filePath = Path.Combine(_tempDirectory.FullName, "clip.mp4");
        File.WriteAllBytes(filePath, [1, 2, 3]);
        var originalDirectory = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(_tempDirectory.FullName);

            var result = MediaPathValidator.ValidateInputFile("clip.mp4");

            Assert.Equal(Path.GetFullPath(filePath), result);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
        }
    }

    [Fact]
    public void ValidateInputFile_ContainsControlCharacter_ThrowsInvalidPath()
    {
        var exception = Assert.Throws<MediaValidationException>(() => MediaPathValidator.ValidateInputFile("bad\0path.mp4"));

        Assert.Equal(MediaValidationFailureReason.InvalidPath, exception.Reason);
    }
}
