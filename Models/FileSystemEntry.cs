namespace Jellyfin.Plugin.StorageManager.Models;

public class FileSystemEntry
{
    public required string Name { get; set; }
    public required string Path { get; set; }
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public int ChildCount { get; set; }
}
