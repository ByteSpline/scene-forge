using System.IO;
using SceneForge.App.Session;
using SceneForge.Infrastructure.Logging;
using SceneForge.Infrastructure.Persistence;
using SceneForge.Media.Detection.Fusion;
using SceneForge.Media.Extraction;

namespace SceneForge.App.Persistence;

public sealed class ProjectPersistenceCoordinator : IProjectPersistenceCoordinator
{
    private readonly IProjectStore _projectStore;
    private readonly IAutosaveService _autosave;
    private readonly IStaleSourceDetector _staleSourceDetector;
    private readonly ITempFileRegistry _tempFileRegistry;
    private readonly ProjectLayout _layout;
    private readonly IAppLogger _logger;

    public ProjectPersistenceCoordinator(
        IProjectStore projectStore,
        IAutosaveService autosave,
        IStaleSourceDetector staleSourceDetector,
        ITempFileRegistry tempFileRegistry,
        ProjectLayout layout,
        IAppLogger logger)
    {
        _projectStore = projectStore;
        _autosave = autosave;
        _staleSourceDetector = staleSourceDetector;
        _tempFileRegistry = tempFileRegistry;
        _layout = layout;
        _logger = logger;
    }

    public async Task BeginStageAsync(WorkflowSession session, ProjectStage stage, CancellationToken cancellationToken = default)
    {
        try
        {
            await _autosave.BeginStageAsync(session.ProjectId, stage, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRecognizedPersistenceFailure(ex))
        {
            _logger.Log(LogLevel.Warning, $"Autosave could not mark stage {stage} as started for project {session.ProjectId}.", ex);
        }
    }

    public async Task CheckpointAsync(WorkflowSession session, ProjectStage stage, CancellationToken cancellationToken = default)
    {
        try
        {
            var document = await BuildDocumentAsync(session, stage, cancellationToken).ConfigureAwait(false);
            await _autosave.CompleteStageAsync(document, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRecognizedPersistenceFailure(ex))
        {
            _logger.Log(LogLevel.Warning, $"Autosave checkpoint for stage {stage} failed for project {session.ProjectId}; continuing without a saved checkpoint.", ex);
        }
    }

    public async Task FinalizeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _tempFileRegistry.CleanupAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRecognizedPersistenceFailure(ex))
        {
            _logger.Log(LogLevel.Warning, "Temp file cleanup on completion failed.", ex);
        }
    }

    private async Task<SceneForgeProjectDocument> BuildDocumentAsync(WorkflowSession session, ProjectStage stage, CancellationToken cancellationToken)
    {
        var videoPath = session.VideoFilePath
            ?? throw new InvalidOperationException("Cannot checkpoint a project before a source video has been imported.");

        var videoFingerprint = _staleSourceDetector.Capture(videoPath);
        var audioFingerprint = session.AudioFilePath is { } audioPath ? _staleSourceDetector.Capture(audioPath) : null;

        var createdUtc = DateTimeOffset.UtcNow;
        var projectFilePath = _layout.ProjectFilePath(session.ProjectId);
        if (File.Exists(projectFilePath))
        {
            try
            {
                var existing = await _projectStore.LoadAsync(projectFilePath, cancellationToken).ConfigureAwait(false);
                createdUtc = existing.CreatedUtc;
            }
            catch (ProjectPersistenceException)
            {
                // The prior checkpoint is unreadable - treat this save as
                // starting a fresh CreatedUtc rather than failing the
                // checkpoint entirely.
            }
        }

        return new SceneForgeProjectDocument
        {
            ProjectId = session.ProjectId,
            CreatedUtc = createdUtc,
            LastModifiedUtc = DateTimeOffset.UtcNow,
            Stage = stage,
            VideoSource = videoFingerprint,
            AudioSource = audioFingerprint,
            AnalysisProfile = session.AnalysisProfile,
            DetectorConfigVersion = TransitionDetectionProfileVersion.V1,
            OutputFrameRate = session.OutputFrameRate,
            Detections = session.Detections,
            Clips = BuildClipMetadata(session),
            ManualOverrides = BuildManualOverrides(session),
            TimelineSeed = session.Seed,
            RenderSettings = BuildRenderSettings(session),
        };
    }

    private static List<ClipMetadataRecord>? BuildClipMetadata(WorkflowSession session)
    {
        if (session.ExtractionResult is not { } extraction)
        {
            return null;
        }

        var combined = new List<CleanClip>(extraction.AcceptedClips.Count + extraction.RejectedClips.Count);
        combined.AddRange(extraction.AcceptedClips);
        combined.AddRange(extraction.RejectedClips);

        return combined.Select((clip, index) => new ClipMetadataRecord
        {
            Index = index,
            IsAccepted = clip.Score.Accepted,
            RangeStart = clip.Range.Start,
            RangeEnd = clip.Range.End,
            SourceSceneIndex = clip.SourceSceneIndex,
            Score = clip.Score,
            ClusterId = clip.ClusterId,
        }).ToList();
    }

    private static List<ManualOverrideRecord>? BuildManualOverrides(WorkflowSession session)
    {
        if (session.ClipOverrides.Count == 0)
        {
            return null;
        }

        return session.ClipOverrides.Select(kvp => new ManualOverrideRecord
        {
            ClipIndex = kvp.Key,
            IsIncluded = kvp.Value.IsIncluded,
            AdjustedStart = kvp.Value.AdjustedRange.Start,
            AdjustedEnd = kvp.Value.AdjustedRange.End,
        }).ToList();
    }

    private static RenderSettingsRecord? BuildRenderSettings(WorkflowSession session)
    {
        if (session.OutputVideoPath is not { } outputPath)
        {
            return null;
        }

        return new RenderSettingsRecord
        {
            OutputVideoPath = outputPath,
            FitMode = session.FitMode,
            OutputWidth = session.OutputWidth,
            OutputHeight = session.OutputHeight,
            OutputFrameRate = session.OutputFrameRate,
        };
    }

    private static bool IsRecognizedPersistenceFailure(Exception ex) => ex is
        IOException or
        UnauthorizedAccessException or
        ProjectPersistenceException;
}
