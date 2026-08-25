using SceneForge.Media.Sampling;

namespace SceneForge.Accuracy.Profiling;

// One stage's own wall-clock cost and throughput within a full pipeline
// profiling run. ItemCount is frames analyzed for Detect/Extract, or 1 for
// Render (a single ffmpeg invocation, not a per-frame loop from this
// process's point of view).
public sealed record StageProfile(double WallClockSeconds, int ItemCount, string Notes);

// Full before/after evidence for one AnalysisProfile against one input file:
// per-stage timing plus whole-run resource usage (peak managed memory, peak
// working set, this process's own CPU time, free-disk-space delta on the
// output drive). CPU time is this .NET process's own TotalProcessorTime -
// it includes every in-process OpenCvSharp/managed cost (all of Detect and
// Extract run in-process) but NOT the separate ffmpeg child processes'
// own CPU usage (decode, and the Render stage's encode) - ProcessRunner
// launches ffmpeg as an independent OS process, and .NET's Process API has
// no supported way to attribute a child process's CPU time back to this
// one. That decode/encode cost is still fully reflected in each stage's
// wall-clock time and the run's overall throughput, just not double-counted
// into this specific CPU-time figure - disclosed here rather than implied
// to be a complete picture (CLAUDE.md rule 10).
public sealed record PipelineProfileReport(
    DateTimeOffset TimestampUtc,
    string? CommitSha,
    AnalysisProfile Profile,
    string InputFilePath,
    double InputDurationSeconds,
    StageProfile Detect,
    StageProfile Extract,
    StageProfile? Render,
    int DetectionsFound,
    int ClipsAccepted,
    int ClipsRejected,
    bool? RenderOutputValid,
    double TotalWallClockSeconds,
    double ThroughputSourceSecondsPerWallClockSecond,
    long PeakManagedMemoryBytes,
    long PeakWorkingSetBytes,
    double ProcessCpuTimeSeconds,
    long FreeDiskBytesBeforeOutputDrive,
    long FreeDiskBytesAfterOutputDrive);
