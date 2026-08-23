using SceneForge.Media.Detection;
using SceneForge.Media.Detection.Export;

namespace SceneForge.Media.Tests.Detection.Export;

public class TransitionDetectionCsvWriterTests
{
    private static TransitionDetection Detection(string reason = "simple reason") => new()
    {
        Type = TransitionType.Dissolve,
        Start = TimeSpan.FromSeconds(1.2345),
        Peak = TimeSpan.FromSeconds(1.5),
        End = TimeSpan.FromSeconds(1.8),
        BoundaryTimestamp = TimeSpan.FromSeconds(1.5),
        Confidence = 0.876,
        ContributingSignals = new Dictionary<string, double> { ["StructuralDifference"] = 0.42 },
        DiagnosticReason = reason,
    };

    [Fact]
    public async Task WriteAsync_NoDetections_WritesOnlyHeader()
    {
        using var writer = new StringWriter();

        await TransitionDetectionCsvWriter.WriteAsync([], writer);

        var lines = writer.ToString().TrimEnd('\r', '\n').Split('\n');
        Assert.Single(lines);
        Assert.StartsWith("Type,StartSeconds,PeakSeconds", lines[0]);
    }

    [Fact]
    public async Task WriteAsync_OneDetection_WritesFormattedRow()
    {
        using var writer = new StringWriter();

        await TransitionDetectionCsvWriter.WriteAsync([Detection()], writer);

        var lines = writer.ToString().TrimEnd('\r', '\n').Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("Dissolve,1.234,1.500,1.800,1.500,0.566,0.8760,StructuralDifference=0.4200,simple reason", lines[1]);
    }

    [Fact]
    public async Task WriteAsync_ReasonContainingComma_IsQuotedAndEscaped()
    {
        using var writer = new StringWriter();

        await TransitionDetectionCsvWriter.WriteAsync([Detection("reason, with a comma and \"quotes\"")], writer);

        var content = writer.ToString();
        Assert.Contains("\"reason, with a comma and \"\"quotes\"\"\"", content);
    }

    [Fact]
    public async Task WriteToFileAsync_NewPath_WritesFile()
    {
        var directory = Directory.CreateTempSubdirectory("sceneforge-csv-tests");
        try
        {
            var path = Path.Combine(directory.FullName, "detections.csv");

            await TransitionDetectionCsvWriter.WriteToFileAsync([Detection()], path);

            Assert.True(File.Exists(path));
            var content = await File.ReadAllTextAsync(path);
            Assert.Contains("Dissolve", content);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task WriteToFileAsync_ExistingPath_ThrowsAndDoesNotOverwrite()
    {
        var directory = Directory.CreateTempSubdirectory("sceneforge-csv-tests");
        try
        {
            var path = Path.Combine(directory.FullName, "detections.csv");
            await File.WriteAllTextAsync(path, "pre-existing content");

            await Assert.ThrowsAsync<TransitionDetectionException>(() => TransitionDetectionCsvWriter.WriteToFileAsync([Detection()], path));

            Assert.Equal("pre-existing content", await File.ReadAllTextAsync(path));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
