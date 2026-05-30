using Jellyfin.Data.Enums;
using Jellyfin.Plugin.StorageManager.Models;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StorageManager.Api;

[ApiController]
[Route("StorageManager")]
[Authorize]
public class StorageController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<StorageController> _logger;
    private readonly ISessionManager _sessionManager;
    private readonly IUserManager _userManager;
    private readonly IAuthorizationContext _authContext;

    public StorageController(
        ILibraryManager libraryManager,
        ILogger<StorageController> logger,
        ISessionManager sessionManager,
        IUserManager userManager,
        IAuthorizationContext authContext)
    {
        _libraryManager = libraryManager;
        _logger = logger;
        _sessionManager = sessionManager;
        _userManager = userManager;
        _authContext = authContext;
    }

    // -------------------------------------------------------------------------
    // GET /StorageManager/drives
    // Returns info for all ready drives. Non-media drives are marked read-only.
    // -------------------------------------------------------------------------
    [HttpGet("drives")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<DriveInfoModel>>> GetDrives()
    {
        if (!await IsAdminAsync().ConfigureAwait(false))
            return Forbid();

        var config = Plugin.Instance!.Configuration;
        var mediaPaths = GetMediaDrivePaths();

        var drives = DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Where(d => config.ShowNonMediaDrives || IsDriveMedia(d, mediaPaths))
            .Select(d => MapDrive(d, mediaPaths))
            .ToList();

        return Ok(drives);
    }

    // -------------------------------------------------------------------------
    // GET /StorageManager/browse?path={path}
    // Lists files and folders at the given path (must be on a media drive).
    // -------------------------------------------------------------------------
    [HttpGet("browse")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BrowseResponse>> Browse([FromQuery] string path)
    {
        if (!await IsAdminAsync().ConfigureAwait(false))
            return Forbid();

        if (!IsPathOnMediaDrive(path))
            return StatusCode(StatusCodes.Status403Forbidden, "Path is not on a manageable media drive");

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return BadRequest("Invalid path");
        }

        if (!Directory.Exists(fullPath))
            return BadRequest("Directory does not exist");

        var entries = new List<FileSystemEntry>();

        try
        {
            foreach (var dir in Directory.GetDirectories(fullPath))
            {
                try
                {
                    var info = new DirectoryInfo(dir);
                    var childCount = 0;
                    try { childCount = Directory.GetFileSystemEntries(dir).Length; }
                    catch { /* access denied — leave at 0 */ }

                    entries.Add(new FileSystemEntry
                    {
                        Name = info.Name,
                        Path = info.FullName,
                        IsDirectory = true,
                        Size = 0,
                        LastModified = info.LastWriteTimeUtc,
                        ChildCount = childCount,
                    });
                }
                catch { /* skip unreadable entries */ }
            }

            foreach (var file in Directory.GetFiles(fullPath))
            {
                try
                {
                    var info = new FileInfo(file);
                    entries.Add(new FileSystemEntry
                    {
                        Name = info.Name,
                        Path = info.FullName,
                        IsDirectory = false,
                        Size = info.Length,
                        LastModified = info.LastWriteTimeUtc,
                    });
                }
                catch { /* skip unreadable entries */ }
            }
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Access denied to this directory");
        }

        var mediaPaths = GetMediaDrivePaths();
        var parentDir = Directory.GetParent(fullPath)?.FullName;

        return Ok(new BrowseResponse
        {
            CurrentPath = fullPath,
            ParentPath = parentDir is not null && IsPathOnMediaDrive(parentDir) ? parentDir : null,
            IsMediaDrive = mediaPaths.Any(mp => fullPath.StartsWith(mp, StringComparison.OrdinalIgnoreCase)),
            Entries = entries
                .OrderByDescending(e => e.IsDirectory)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        });
    }

    // -------------------------------------------------------------------------
    // POST /StorageManager/rename
    // Renames a file or folder on the media drive.
    // -------------------------------------------------------------------------
    [HttpPost("rename")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> Rename([FromBody] RenameRequest request)
    {
        if (!await IsAdminAsync().ConfigureAwait(false))
            return Forbid();

        if (!IsPathOnMediaDrive(request.OldPath))
            return StatusCode(StatusCodes.Status403Forbidden, "Path is not on a manageable media drive");

        if (string.IsNullOrWhiteSpace(request.NewName) ||
            request.NewName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return BadRequest("New name contains invalid characters");

        string oldFull;
        try { oldFull = Path.GetFullPath(request.OldPath); }
        catch { return BadRequest("Invalid source path"); }

        var parentDir = Path.GetDirectoryName(oldFull);
        if (parentDir is null)
            return BadRequest("Cannot resolve parent directory");

        var newFull = Path.Combine(parentDir, request.NewName);

        if (System.IO.File.Exists(newFull) || Directory.Exists(newFull))
            return BadRequest("A file or folder with that name already exists");

        try
        {
            if (Directory.Exists(oldFull))
                Directory.Move(oldFull, newFull);
            else if (System.IO.File.Exists(oldFull))
                System.IO.File.Move(oldFull, newFull);
            else
                return BadRequest("Source path not found");

            _logger.LogInformation("Storage Manager: renamed {OldPath} → {NewPath}", oldFull, newFull);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Storage Manager: rename failed {OldPath}", oldFull);
            return StatusCode(StatusCodes.Status500InternalServerError, "Rename operation failed");
        }
    }

    // -------------------------------------------------------------------------
    // POST /StorageManager/delete
    // Deletes a file or folder. Requires admin password confirmation.
    // -------------------------------------------------------------------------
    [HttpPost("delete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> Delete([FromBody] DeleteRequest request)
    {
        if (!await IsAdminAsync().ConfigureAwait(false))
            return Forbid();

        if (!IsPathOnMediaDrive(request.Path))
            return StatusCode(StatusCodes.Status403Forbidden, "Path is not on a manageable media drive");

        var config = Plugin.Instance!.Configuration;
        if (config.RequirePasswordForDelete)
        {
            var verified = await VerifyAdminPasswordAsync(request.Username, request.Password)
                .ConfigureAwait(false);
            if (!verified)
                return Unauthorized("Invalid credentials");
        }

        string fullPath;
        try { fullPath = Path.GetFullPath(request.Path); }
        catch { return BadRequest("Invalid path"); }

        try
        {
            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
                _logger.LogInformation("Storage Manager: deleted directory {Path}", fullPath);
            }
            else if (System.IO.File.Exists(fullPath))
            {
                System.IO.File.Delete(fullPath);
                _logger.LogInformation("Storage Manager: deleted file {Path}", fullPath);
            }
            else
            {
                return BadRequest("Path not found");
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Storage Manager: delete failed {Path}", fullPath);
            return StatusCode(StatusCodes.Status500InternalServerError, "Delete operation failed");
        }
    }

    // -------------------------------------------------------------------------
    // GET /StorageManager/config
    // Returns current plugin configuration plus auto-detected paths.
    // -------------------------------------------------------------------------
    [HttpGet("config")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<object>> GetConfig()
    {
        if (!await IsAdminAsync().ConfigureAwait(false))
            return Forbid();

        var config = Plugin.Instance!.Configuration;
        var detected = GetAutoDetectedPaths().ToArray();

        return Ok(new
        {
            config.MediaDrivePaths,
            config.AutoDetectMediaDrive,
            config.RequirePasswordForDelete,
            config.ShowNonMediaDrives,
            DetectedPaths = detected,
        });
    }

    // -------------------------------------------------------------------------
    // POST /StorageManager/config
    // Saves plugin configuration.
    // -------------------------------------------------------------------------
    [HttpPost("config")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> SaveConfig([FromBody] SaveConfigRequest request)
    {
        if (!await IsAdminAsync().ConfigureAwait(false))
            return Forbid();

        var config = Plugin.Instance!.Configuration;
        config.MediaDrivePaths = request.MediaDrivePaths ?? Array.Empty<string>();
        config.AutoDetectMediaDrive = request.AutoDetectMediaDrive;
        config.RequirePasswordForDelete = request.RequirePasswordForDelete;
        config.ShowNonMediaDrives = request.ShowNonMediaDrives;
        Plugin.Instance.SaveConfiguration();

        return NoContent();
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    private async Task<bool> IsAdminAsync()
    {
        try
        {
            var authInfo = await _authContext.GetAuthorizationInfo(Request).ConfigureAwait(false);
            var user = _userManager.GetUserById(authInfo.UserId);
            return user is not null && user.HasPermission(PermissionKind.IsAdministrator);
        }
        catch
        {
            return false;
        }
    }

    private HashSet<string> GetMediaDrivePaths()
    {
        var config = Plugin.Instance?.Configuration;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (config?.AutoDetectMediaDrive != false)
        {
            foreach (var p in GetAutoDetectedPaths())
                paths.Add(p);
        }

        if (config?.MediaDrivePaths is { Length: > 0 })
        {
            foreach (var raw in config.MediaDrivePaths)
            {
                try { paths.Add(Path.GetFullPath(raw)); }
                catch { /* ignore malformed config paths */ }
            }
        }

        return paths;
    }

    private List<string> GetAutoDetectedPaths()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<string>();

        try
        {
            foreach (var folder in _libraryManager.RootFolder.Children)
            {
                foreach (var location in folder.PhysicalLocations)
                {
                    var root = Path.GetPathRoot(location);
                    if (root is not null && seen.Add(root))
                        results.Add(Path.GetFullPath(root));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Storage Manager: failed to auto-detect media paths");
        }

        return results;
    }

    private bool IsPathOnMediaDrive(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return false;

        string fullPath;
        try { fullPath = Path.GetFullPath(rawPath); }
        catch { return false; }

        return GetMediaDrivePaths()
            .Any(mp => fullPath.StartsWith(mp, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDriveMedia(DriveInfo drive, HashSet<string> mediaPaths)
    {
        var root = drive.RootDirectory.FullName;
        return mediaPaths.Any(mp =>
            root.StartsWith(mp, StringComparison.OrdinalIgnoreCase) ||
            mp.StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<bool> VerifyAdminPasswordAsync(string username, string password)
    {
        AuthenticationResult? result = null;
        try
        {
            result = await _sessionManager.AuthenticateNewSession(new AuthenticationRequest
            {
                Username = username,
                Password = password,
                App = "StorageManager",
                AppVersion = Plugin.Instance!.Version.ToString(),
                DeviceId = Guid.NewGuid().ToString(),
                DeviceName = "Storage Manager Plugin",
                RemoteEndPoint = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            }).ConfigureAwait(false);

            if (result?.User?.Policy?.IsAdministrator != true)
                return false;

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Storage Manager: password verification failed for {Username}", username);
            return false;
        }
        finally
        {
            // Discard the temporary session created for verification
            if (result?.AccessToken is not null)
            {
                try { await _sessionManager.Logout(result.AccessToken).ConfigureAwait(false); }
                catch { /* best effort */ }
            }
        }
    }

    private static DriveInfoModel MapDrive(DriveInfo drive, HashSet<string> mediaPaths)
    {
        var root = drive.RootDirectory.FullName;
        var isMedia = IsDriveMedia(drive, mediaPaths);
        var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
            ? drive.Name
            : $"{drive.VolumeLabel} ({drive.Name})";

        return new DriveInfoModel
        {
            Name = label,
            RootPath = root,
            TotalSize = drive.TotalSize,
            FreeSpace = drive.AvailableFreeSpace,
            UsedSpace = drive.TotalSize - drive.AvailableFreeSpace,
            UsedPercentage = drive.TotalSize > 0
                ? Math.Round((double)(drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize * 100, 1)
                : 0,
            IsMediaDrive = isMedia,
            IsManageable = isMedia,
            DriveType = drive.DriveType.ToString(),
            DriveFormat = drive.DriveFormat,
            VolumeLabel = drive.VolumeLabel,
        };
    }
}

// Inline request model — only used by this controller.
public class SaveConfigRequest
{
    public string[]? MediaDrivePaths { get; set; }
    public bool AutoDetectMediaDrive { get; set; } = true;
    public bool RequirePasswordForDelete { get; set; } = true;
    public bool ShowNonMediaDrives { get; set; } = true;
}
