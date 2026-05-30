using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.StorageManager.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Manually specified paths treated as the manageable media drive(s).
    /// Leave empty to auto-detect from Jellyfin library paths.
    /// </summary>
    public string[] MediaDrivePaths { get; set; } = Array.Empty<string>();

    /// <summary>
    /// When true, auto-detection from Jellyfin library roots supplements any manual paths.
    /// </summary>
    public bool AutoDetectMediaDrive { get; set; } = true;

    /// <summary>
    /// Require admin password confirmation before any delete operation.
    /// </summary>
    public bool RequirePasswordForDelete { get; set; } = true;

    /// <summary>
    /// Show non-media drives (OS/cache) in the overview. They will not be manageable.
    /// </summary>
    public bool ShowNonMediaDrives { get; set; } = true;
}
