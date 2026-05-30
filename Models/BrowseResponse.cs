namespace Jellyfin.Plugin.StorageManager.Models;

public class BrowseResponse
{
    public required string CurrentPath { get; set; }
    public string? ParentPath { get; set; }
    public bool IsMediaDrive { get; set; }
    public required List<FileSystemEntry> Entries { get; set; }
}
