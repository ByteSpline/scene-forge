namespace SceneForge.Core.Resources;

internal sealed class DriveInfoProvider : IDriveInfoProvider
{
    public long GetAvailableFreeBytes(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        var drive = new DriveInfo(string.IsNullOrEmpty(root) ? fullPath : root);
        return drive.AvailableFreeSpace;
    }
}
