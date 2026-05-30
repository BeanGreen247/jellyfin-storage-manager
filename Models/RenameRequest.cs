namespace Jellyfin.Plugin.StorageManager.Models;

public class RenameRequest
{
    public required string OldPath { get; set; }
    public required string NewName { get; set; }
}
