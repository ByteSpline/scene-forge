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

    public int MaxWorkers => Math.Max(1, _processorCount - 1);

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
