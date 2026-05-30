namespace Jellyfin.Plugin.StorageManager.Models;

public class DriveInfoModel
{
    public required string Name { get; set; }
    public required string RootPath { get; set; }
    public long TotalSize { get; set; }
    public long FreeSpace { get; set; }
    public long UsedSpace { get; set; }
    public double UsedPercentage { get; set; }
    public bool IsMediaDrive { get; set; }
    public bool IsManageable { get; set; }
    public required string DriveType { get; set; }
    public string DriveFormat { get; set; } = string.Empty;
    public string VolumeLabel { get; set; } = string.Empty;
}
