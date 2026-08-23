using System.Text.Json;
using SceneForge.Media.Detection;
using SceneForge.Media.Detection.Export;

namespace SceneForge.Media.Tests.Detection.Export;

public class TransitionDetectionJsonWriterTests
{
    private static TransitionDetection Detection() => new()
    {
        Type = TransitionType.Flash,
        Start = TimeSpan.FromSeconds(2.0),
        Peak = TimeSpan.FromSeconds(2.1),
        End = TimeSpan.FromSeconds(2.2),
        BoundaryTimestamp = TimeSpan.FromSeconds(2.1),
        Confidence = 0.95,
        ContributingSignals = new Dictionary<string, double> { ["WhiteScore"] = 0.9 },
        DiagnosticReason = "brief white spike",
    };

    [Fact]
    public async Task WriteAsync_RoundTrips_TimestampsAsSecondsAndAllFields()
    {
        using var stream = new MemoryStream();

        await TransitionDetectionJsonWriter.WriteAsync([Detection()], stream);

        stream.Position = 0;
        using var document = await JsonDocument.ParseAsync(stream);
        var element = document.RootElement[0];

        Assert.Equal("Flash", element.GetProperty("type").GetString());
        Assert.Equal(2.0, element.GetProperty("start").GetDouble());
        Assert.Equal(2.2, element.GetProperty("end").GetDouble());
        Assert.Equal(0.95, element.GetProperty("confidence").GetDouble());
        Assert.Equal("brief white spike", element.GetProperty("diagnosticReason").GetString());
        Assert.Equal(0.9, element.GetProperty("contributingSignals").GetProperty("WhiteScore").GetDouble());
    }

    [Fact]
    public async Task WriteAsync_EmptyList_WritesEmptyJsonArray()
    {
        using var stream = new MemoryStream();

        await TransitionDetectionJsonWriter.WriteAsync([], stream);

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        Assert.Equal("[]", content);
    }

    [Fact]
    public async Task WriteToFileAsync_NewPath_WritesFile()
    {
        var directory = Directory.CreateTempSubdirectory("sceneforge-json-tests");
        try
        {
            var path = Path.Combine(directory.FullName, "detections.json");

            await TransitionDetectionJsonWriter.WriteToFileAsync([Detection()], path);

            Assert.True(File.Exists(path));
            var content = await File.ReadAllTextAsync(path);
            Assert.Contains("\"Flash\"", content);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task WriteToFileAsync_ExistingPath_ThrowsAndDoesNotOverwrite()
    {
        var directory = Directory.CreateTempSubdirectory("sceneforge-json-tests");
        try
        {
            var path = Path.Combine(directory.FullName, "detections.json");
            await File.WriteAllTextAsync(path, "pre-existing content");

            await Assert.ThrowsAsync<TransitionDetectionException>(() => TransitionDetectionJsonWriter.WriteToFileAsync([Detection()], path));

            Assert.Equal("pre-existing content", await File.ReadAllTextAsync(path));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
