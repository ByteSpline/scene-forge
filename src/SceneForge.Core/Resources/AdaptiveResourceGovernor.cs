namespace SceneForge.Core.Resources;

public sealed class AdaptiveResourceGovernor : IAdaptiveResourceGovernor
{
    private readonly IDriveInfoProvider _driveInfoProvider;
    private readonly int _processorCount;

    public AdaptiveResourceGovernor()
        : this(new DriveInfoProvider(), Environment.ProcessorCount)
    {
    }

    internal AdaptiveResourceGovernor(IDriveInfoProvider driveInfoProvider, int processorCount)
    {
        ArgumentNullException.ThrowIfNull(driveInfoProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(processorCount, 1);

        _driveInfoProvider = driveInfoProvider;
        _processorCount = processorCount;
    }

    // Tightened from the original "leave one core free" design after a real
    // hang was traced (with Task Manager) to ffmpeg/OpenCV saturating every
    // core: the product requirement is now a hard ~35% ceiling on this
    // machine's total CPU capacity, not just "leave one core spare". Floor
    // (never round) so MaxWorkers/_processorCount can never exceed
    // CpuBudgetFraction - see AdaptiveResourceGovernorTests's
    // MaxWorkers_NeverExceedsThirtyFivePercentCpuBudget for the invariant
    // this guarantees. The Max(1, ...) floor is a separate, unavoidable
    // exception for machines with too few logical CPUs to represent any
    // sub-35% budget as a whole worker (1-2 logical CPUs) - forward
    // progress requires at least one worker even though that necessarily
    // exceeds the ratio on those machines.
    internal const double CpuBudgetFraction = 0.35;

    public int MaxWorkers => Math.Max(1, (int)Math.Floor(_processorCount * CpuBudgetFraction));

    public void EnsureSufficientDiskSpace(string path, long requiredBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(requiredBytes);

        var availableBytes = _driveInfoProvider.GetAvailableFreeBytes(path);
        if (availableBytes < requiredBytes)
        {
            throw new InsufficientDiskSpaceException(path, requiredBytes, availableBytes);
        }
    }
}
