namespace SceneForge.Core.Resources;

// Seam around System.IO.DriveInfo so AdaptiveResourceGovernor's disk-space
// policy is testable without depending on the real filesystem's actual free
// space at test-run time.
public interface IDriveInfoProvider
{
    // path need not exist yet (e.g. an output file that hasn't been created)
    // - only its drive/root needs to be resolvable.
    long GetAvailableFreeBytes(string path);
}
