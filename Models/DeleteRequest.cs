namespace Jellyfin.Plugin.StorageManager.Models;

public class DeleteRequest
{
    public required string Path { get; set; }
    public required string Username { get; set; }
    public required string Password { get; set; }
}
