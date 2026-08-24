using SceneForge.Infrastructure.Persistence;
using SceneForge.Infrastructure.Tests.TestSupport;

namespace SceneForge.Infrastructure.Tests.Persistence;

public sealed class ProjectStoreTests : IDisposable
{
    private readonly TempDirectoryFixture _fixture = new();
    private readonly ProjectStore _store = new();

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsEveryField()
    {
        var projectId = Guid.NewGuid();
        var document = SampleDocumentBuilder.BuildFull(projectId, @"C:\videos\source.mp4", @"C:\audio\track.mp3");
        var path = Path.Combine(_fixture.Path, "project.sfproj");

        await _store.SaveAsync(document, path);
        var loaded = await _store.LoadAsync(path);

        Assert.Equal(document.ProjectId, loaded.ProjectId);
        Assert.Equal(document.Stage, loaded.Stage);
        Assert.Equal(document.VideoSource.FilePath, loaded.VideoSource.FilePath);
        Assert.Equal(document.VideoSource.SizeBytes, loaded.VideoSource.SizeBytes);
        Assert.Equal(document.AudioSource!.FilePath, loaded.AudioSource!.FilePath);
        Assert.Equal(document.AnalysisProfile, loaded.AnalysisProfile);
        Assert.Equal(document.DetectorConfigVersion, loaded.DetectorConfigVersion);
        Assert.Equal(document.OutputFrameRate, loaded.OutputFrameRate);
        Assert.Single(loaded.Detections!);
        Assert.Equal(document.Detections![0].Type, loaded.Detections![0].Type);
        Assert.Equal(document.Detections[0].Start, loaded.Detections[0].Start);
        Assert.Equal(document.Detections[0].ContributingSignals["HsvHistogramDistance"], loaded.Detections[0].ContributingSignals["HsvHistogramDistance"]);
        Assert.Single(loaded.Clips!);
        Assert.Equal(document.Clips![0].Score.Overall, loaded.Clips![0].Score.Overall);
        Assert.Single(loaded.ManualOverrides!);
        Assert.Equal(document.ManualOverrides![0].AdjustedStart, loaded.ManualOverrides![0].AdjustedStart);
        Assert.Equal(document.TimelineSeed, loaded.TimelineSeed);
        Assert.Equal(document.RenderSettings!.OutputVideoPath, loaded.RenderSettings!.OutputVideoPath);
        Assert.Equal(document.RenderSettings.FitMode, loaded.RenderSettings.FitMode);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_MinimalDocument_RoundTripsWithNullOptionalFields()
    {
        var document = SampleDocumentBuilder.BuildMinimal(Guid.NewGuid(), @"C:\videos\source.mp4");
        var path = Path.Combine(_fixture.Path, "minimal.sfproj");

        await _store.SaveAsync(document, path);
        var loaded = await _store.LoadAsync(path);

        Assert.Null(loaded.AudioSource);
        Assert.Null(loaded.Detections);
        Assert.Null(loaded.Clips);
        Assert.Null(loaded.ManualOverrides);
        Assert.Null(loaded.RenderSettings);
    }

    [Fact]
    public async Task SaveAsync_Twice_SecondSaveReplacesTheFirst()
    {
        var projectId = Guid.NewGuid();
        var path = Path.Combine(_fixture.Path, "project.sfproj");

        await _store.SaveAsync(SampleDocumentBuilder.BuildMinimal(projectId, "a.mp4") with { Stage = ProjectStage.Imported }, path);
        await _store.SaveAsync(SampleDocumentBuilder.BuildMinimal(projectId, "a.mp4") with { Stage = ProjectStage.Analyzed }, path);

        var loaded = await _store.LoadAsync(path);
        Assert.Equal(ProjectStage.Analyzed, loaded.Stage);
    }

    [Fact]
    public async Task LoadAsync_MissingFile_ThrowsFileNotFoundException()
    {
        var path = Path.Combine(_fixture.Path, "does-not-exist.sfproj");

        await Assert.ThrowsAsync<FileNotFoundException>(() => _store.LoadAsync(path));
    }

    [Fact]
    public async Task LoadAsync_EmptyFile_ThrowsProjectCorruptedException()
    {
        var path = Path.Combine(_fixture.Path, "empty.sfproj");
        await File.WriteAllTextAsync(path, string.Empty);

        await Assert.ThrowsAsync<ProjectCorruptedException>(() => _store.LoadAsync(path));
    }

    [Fact]
    public async Task LoadAsync_MalformedJson_ThrowsProjectCorruptedException()
    {
        var path = Path.Combine(_fixture.Path, "malformed.sfproj");
        await File.WriteAllTextAsync(path, "{ not valid json ][");

        await Assert.ThrowsAsync<ProjectCorruptedException>(() => _store.LoadAsync(path));
    }

    [Fact]
    public async Task LoadAsync_MissingRequiredField_ThrowsProjectCorruptedException()
    {
        var path = Path.Combine(_fixture.Path, "missing-field.sfproj");
        // Valid JSON object, but missing every required property (projectId,
        // stage, videoSource, ...).
        await File.WriteAllTextAsync(path, "{\"schemaVersion\": 1}");

        await Assert.ThrowsAsync<ProjectCorruptedException>(() => _store.LoadAsync(path));
    }

    [Fact]
    public async Task LoadAsync_UnsupportedSchemaVersion_ThrowsProjectSchemaVersionException()
    {
        var document = SampleDocumentBuilder.BuildMinimal(Guid.NewGuid(), "a.mp4") with { SchemaVersion = 999 };
        var path = Path.Combine(_fixture.Path, "future-schema.sfproj");
        await _store.SaveAsync(document, path);

        var ex = await Assert.ThrowsAsync<ProjectSchemaVersionException>(() => _store.LoadAsync(path));
        Assert.Equal(999, ex.FoundVersion);
        Assert.Equal(SceneForgeProjectDocument.CurrentSchemaVersion, ex.ExpectedVersion);
    }

    // Regression test (manual end-to-end testing, Step 1 Welcome & Import):
    // every real autosave goes through the exact production layout this test
    // recreates - a ProjectLayout whose ProjectsRoot and TempRoot are sibling
    // directories under one AppDataRoot (see ProjectLayout), with an
    // app-owned ITempFileRegistry rooted at TempRoot alongside it (see
    // App.xaml.cs's DI wiring). Saving a project checkpoint into
    // ProjectsRoot/<id>/project.sfproj must succeed regardless of that
    // sibling registry's existence: AtomicFileWriter's own sibling temp file
    // is a same-directory-as-target implementation detail, never a Temp-root
    // scratch file, so it must never be checked against the registry's
    // Temp-root allowlist. Before the fix, this failed on every single
    // autosave with "... is outside the app-owned temp directory ... and
    // cannot be registered for cleanup."
    [Fact]
    public async Task SaveAsync_MirrorsProductionLayoutWithSiblingTempRegistry_DoesNotThrow()
    {
        var layout = new ProjectLayout(_fixture.Path);
        _ = new TempFileRegistry(layout.TempRoot);

        var projectId = Guid.NewGuid();
        var document = SampleDocumentBuilder.BuildMinimal(projectId, @"C:\videos\source.mp4");
        var projectFilePath = layout.ProjectFilePath(projectId);

        await _store.SaveAsync(document, projectFilePath);

        var loaded = await _store.LoadAsync(projectFilePath);
        Assert.Equal(projectId, loaded.ProjectId);
    }

    public void Dispose() => _fixture.Dispose();
}
