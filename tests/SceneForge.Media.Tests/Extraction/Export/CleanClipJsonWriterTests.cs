using System.Text.Json;
using SceneForge.Media.Domain;
using SceneForge.Media.Extraction;
using SceneForge.Media.Extraction.Export;

namespace SceneForge.Media.Tests.Extraction.Export;

public class CleanClipJsonWriterTests
{
    private static CleanClipExtractionResult Result()
    {
        var acceptedScore = new ClipScore
        {
            Duration = 1.0,
            Sharpness = 0.9,
            Stability = 0.9,
            Exposure = 0.9,
            FreezeRisk = 0.0,
            TransitionDistance = 1.0,
            OverlaySuspicion = 0.0,
            Overall = 0.9,
            Accepted = true,
            Reasons =
            [
                new ScoreReason { Factor = "Sharpness", Passed = true, Code = null, Detail = "sharp enough" },
            ],
        };
        var rejectedScore = acceptedScore with
        {
            Accepted = false,
            Overall = 0.1,
            Reasons =
            [
                new ScoreReason { Factor = "FreezeRisk", Passed = false, Code = RejectionReason.HighFreezeRisk, Detail = "too static" },
            ],
        };

        var descriptor = new PerceptualDescriptor
        {
            PerceptualHash = 0x1234,
            ColorHistogram = [0.5f, 0.5f],
            EdgeHistogram = [0.1f, 0.1f],
            Motion = MotionClass.Subtle,
        };

        var accepted = new CleanClip
        {
            Range = new TimeRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6)),
            SourceSceneIndex = 0,
            Score = acceptedScore,
            Descriptor = descriptor,
            ClusterId = 0,
        };
        var rejected = new CleanClip
        {
            Range = new TimeRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(14)),
            SourceSceneIndex = 1,
            Score = rejectedScore,
            Descriptor = descriptor,
        };

        return new CleanClipExtractionResult
        {
            RemainingCleanRanges = [new TimeRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(20))],
            AcceptedClips = [accepted],
            RejectedClips = [rejected],
            Clusters =
            [
                new VisualCluster { ClusterId = 0, MemberClipIndices = [0], Representative = descriptor },
            ],
        };
    }

    [Fact]
    public async Task WriteAsync_RoundTrips_TimestampsAsSecondsAndAllSections()
    {
        using var stream = new MemoryStream();

        await CleanClipJsonWriter.WriteAsync(Result(), stream);

        stream.Position = 0;
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;

        Assert.Equal(2.0, root.GetProperty("remainingCleanRanges")[0].GetProperty("start").GetDouble());
        Assert.Equal(20.0, root.GetProperty("remainingCleanRanges")[0].GetProperty("end").GetDouble());

        var acceptedClip = root.GetProperty("acceptedClips")[0];
        Assert.Equal(2.0, acceptedClip.GetProperty("range").GetProperty("start").GetDouble());
        Assert.True(acceptedClip.GetProperty("score").GetProperty("accepted").GetBoolean());
        Assert.Equal(0, acceptedClip.GetProperty("clusterId").GetInt32());

        var rejectedClip = root.GetProperty("rejectedClips")[0];
        Assert.False(rejectedClip.GetProperty("score").GetProperty("accepted").GetBoolean());
        Assert.Equal(
            "HighFreezeRisk",
            rejectedClip.GetProperty("score").GetProperty("reasons")[0].GetProperty("code").GetString());

        Assert.Equal(1, root.GetProperty("clusters").GetArrayLength());
    }

    [Fact]
    public async Task WriteAsync_EmptyResult_WritesEmptyCollections()
    {
        var empty = new CleanClipExtractionResult
        {
            RemainingCleanRanges = [],
            AcceptedClips = [],
            RejectedClips = [],
            Clusters = [],
        };
        using var stream = new MemoryStream();

        await CleanClipJsonWriter.WriteAsync(empty, stream);

        stream.Position = 0;
        using var document = await JsonDocument.ParseAsync(stream);

        Assert.Equal(0, document.RootElement.GetProperty("acceptedClips").GetArrayLength());
        Assert.Equal(0, document.RootElement.GetProperty("rejectedClips").GetArrayLength());
    }

    [Fact]
    public async Task WriteToFileAsync_NewPath_WritesFile()
    {
        var directory = Directory.CreateTempSubdirectory("sceneforge-cleanclip-json-tests");
        try
        {
            var path = Path.Combine(directory.FullName, "clean-clips.json");

            await CleanClipJsonWriter.WriteToFileAsync(Result(), path);

            Assert.True(File.Exists(path));
            var content = await File.ReadAllTextAsync(path);
            Assert.Contains("acceptedClips", content);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task WriteToFileAsync_ExistingPath_ThrowsAndDoesNotOverwrite()
    {
        var directory = Directory.CreateTempSubdirectory("sceneforge-cleanclip-json-tests");
        try
        {
            var path = Path.Combine(directory.FullName, "clean-clips.json");
            await File.WriteAllTextAsync(path, "pre-existing content");

            await Assert.ThrowsAsync<CleanClipExtractionException>(() => CleanClipJsonWriter.WriteToFileAsync(Result(), path));

            Assert.Equal("pre-existing content", await File.ReadAllTextAsync(path));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
