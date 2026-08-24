using SceneForge.Infrastructure.Persistence;
using SceneForge.Media.Detection;
using SceneForge.Media.Detection.Fusion;
using SceneForge.Media.Domain;
using SceneForge.Media.Extraction;
using SceneForge.Media.Rendering;
using SceneForge.Media.Sampling;

namespace SceneForge.Infrastructure.Tests.TestSupport;

// Builds a fully-populated SceneForgeProjectDocument - every optional field
// set - so round-trip/corruption tests exercise the real shape a project
// reaches once analysis, review, and export settings have all run, not just
// the minimal required fields.
public static class SampleDocumentBuilder
{
    public static SceneForgeProjectDocument BuildFull(Guid projectId, string videoPath, string? audioPath = null)
    {
        var now = DateTimeOffset.UtcNow;

        return new SceneForgeProjectDocument
        {
            ProjectId = projectId,
            CreatedUtc = now,
            LastModifiedUtc = now,
            Stage = ProjectStage.RenderConfigured,
            VideoSource = new SourceFingerprint { FilePath = videoPath, SizeBytes = 1234, LastWriteTimeUtc = now },
            AudioSource = audioPath is null
                ? null
                : new SourceFingerprint { FilePath = audioPath, SizeBytes = 567, LastWriteTimeUtc = now },
            AnalysisProfile = AnalysisProfile.Balanced,
            DetectorConfigVersion = TransitionDetectionProfileVersion.V1,
            OutputFrameRate = new RationalFrameRate(30, 1),
            Detections =
            [
                new TransitionDetection
                {
                    Type = TransitionType.HardCut,
                    Start = TimeSpan.FromSeconds(9.9),
                    Peak = TimeSpan.FromSeconds(10.0),
                    End = TimeSpan.FromSeconds(10.1),
                    BoundaryTimestamp = TimeSpan.FromSeconds(10.0),
                    Confidence = 0.95,
                    ContributingSignals = new Dictionary<string, double> { ["HsvHistogramDistance"] = 0.8 },
                    DiagnosticReason = "Sharp single-frame discontinuity.",
                },
            ],
            Clips =
            [
                new ClipMetadataRecord
                {
                    Index = 0,
                    IsAccepted = true,
                    RangeStart = TimeSpan.FromSeconds(1),
                    RangeEnd = TimeSpan.FromSeconds(4),
                    SourceSceneIndex = 0,
                    ClusterId = 2,
                    Score = new ClipScore
                    {
                        Duration = 0.8,
                        Sharpness = 0.7,
                        Stability = 0.9,
                        Exposure = 0.6,
                        FreezeRisk = 0.1,
                        TransitionDistance = 0.75,
                        OverlaySuspicion = 0.05,
                        Overall = 0.82,
                        Accepted = true,
                        Reasons = [],
                    },
                },
            ],
            ManualOverrides =
            [
                new ManualOverrideRecord
                {
                    ClipIndex = 0,
                    IsIncluded = true,
                    AdjustedStart = TimeSpan.FromSeconds(1.2),
                    AdjustedEnd = TimeSpan.FromSeconds(3.8),
                },
            ],
            TimelineSeed = 7,
            RenderSettings = new RenderSettingsRecord
            {
                OutputVideoPath = @"C:\output\video.mp4",
                FitMode = AspectFitMode.Letterbox,
                OutputWidth = 1920,
                OutputHeight = 1080,
                OutputFrameRate = new RationalFrameRate(30, 1),
            },
        };
    }

    public static SceneForgeProjectDocument BuildMinimal(Guid projectId, string videoPath)
    {
        var now = DateTimeOffset.UtcNow;

        return new SceneForgeProjectDocument
        {
            ProjectId = projectId,
            CreatedUtc = now,
            LastModifiedUtc = now,
            Stage = ProjectStage.Imported,
            VideoSource = new SourceFingerprint { FilePath = videoPath, SizeBytes = 1234, LastWriteTimeUtc = now },
        };
    }
}
