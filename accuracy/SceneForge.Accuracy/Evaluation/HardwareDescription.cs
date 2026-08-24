namespace SceneForge.Accuracy.Evaluation;

// Documents the machine a RegressionBaseline was captured on (CLAUDE.md
// rule 9: benchmark evidence must be traceable to real hardware) - purely
// descriptive, never compared or gated on by RegressionGate.
public sealed record HardwareDescription(
    string CpuName,
    int LogicalProcessorCount,
    double TotalMemoryGigabytes,
    string OperatingSystem,
    string DotNetSdkVersion);
