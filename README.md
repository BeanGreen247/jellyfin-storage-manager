# Jellyfin Storage Manager Plugin

A Jellyfin server plugin that lets admins monitor disk usage and manage media drive contents directly from the Jellyfin dashboard — no SSH required.

---

## Features

- **Storage overview** — at-a-glance cards for every drive showing used, free, and total space with a colour-coded usage bar
- **Media drive detection** — automatically identifies which drive holds your Jellyfin libraries; other drives (OS, cache) are shown as read-only
- **File browser** — navigate folders on the media drive, with item counts and last-modified dates
- **Rename** — rename any file or folder on the media drive
- **Delete** — permanently delete files or folders; requires the admin to re-enter their Jellyfin password before anything is removed
- **Admin-only** — all features are locked behind Jellyfin's built-in admin authentication; regular users cannot access the plugin
- **Configurable** — choose manual drive paths, toggle password confirmation, show or hide non-media drives

---

## Screenshots

> Plugin page is accessible from the Jellyfin admin sidebar under **Storage Manager**.

| Drive overview | File browser | Delete confirmation |
|---|---|---|
| Drive cards with usage bars | Folder navigation with rename/delete | Password prompt before deletion |

---

## Requirements

| Component | Version |
|---|---|
| Jellyfin Server | 10.11.x |
| .NET SDK | 9.0 (build only) |
| OS | Linux or Windows |

---

## Installation

### Option A — Build and install locally (Jellyfin on this machine)

**1. Install the .NET 9 SDK** if you do not have it:

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 9.0
echo 'export PATH="$HOME/.dotnet:$PATH"' >> ~/.bashrc && source ~/.bashrc
```

**2. Build the plugin:**

```bash
cd ~/jellyfin-storage-manager
dotnet publish --configuration Release --output ./artifacts
```

**3. Copy the DLL to Jellyfin's plugin directory:**

```bash
sudo mkdir -p /var/lib/jellyfin/plugins/StorageManager
sudo cp artifacts/Jellyfin.Plugin.StorageManager.dll /var/lib/jellyfin/plugins/StorageManager/
sudo systemctl restart jellyfin
```

**4. Open Jellyfin** — the plugin appears in the admin sidebar as **Storage Manager**.

---

### Option B — Deploy to a remote server over SSH

Use the included `deploy.sh` script. It builds the plugin locally, copies the DLL over SSH, and restarts Jellyfin on the remote machine in one step.

```bash
cd ~/jellyfin-storage-manager
./deploy.sh <user@host>
```

**Examples:**

```bash
# Standard apt/deb install
./deploy.sh bean@192.168.1.50

# Jellyfin installed to a non-standard path
./deploy.sh bean@192.168.1.50 /opt/jellyfin/plugins/StorageManager

# Docker volume mount
./deploy.sh bean@myserver.local /mnt/docker/jellyfin/plugins/StorageManager
```

The script will:
1. Run `dotnet publish` locally
2. `scp` the DLL to the remote plugin directory (creating it if needed)
3. `ssh` in and run `sudo systemctl restart jellyfin`

If your remote user requires a password for `sudo`, the restart step will prompt for it. Everything else (build, copy) does not need sudo.

**Tip — SSH config shorthand:**

If you connect with a non-default port, key file, or username, add an entry to `~/.ssh/config` once and use the alias everywhere:

```
# ~/.ssh/config
Host myjellyfin
    HostName 192.168.1.50
    User bean
    Port 22
    IdentityFile ~/.ssh/id_ed25519
```

```bash
./deploy.sh myjellyfin
```

**Docker note:** If Jellyfin runs inside Docker, the automatic restart (`systemctl restart jellyfin`) will not apply. After the DLL is copied into the Docker volume, restart the container manually:

```bash
docker restart jellyfin
# or with compose:
docker compose restart jellyfin
```

---

### Option C — Install from the Jellyfin Plugin Catalog

This is the easiest method for end users. The plugin is distributed through a public repository manifest that Jellyfin downloads automatically.

**Step 1 — Add the repository to Jellyfin**

1. Open Jellyfin and go to **Admin Dashboard → Plugins → Repositories**
2. Click **+** to add a new repository
3. Enter these details:

| Field | Value |
|---|---|
| Repository Name | Storage Manager |
| Repository URL | `https://raw.githubusercontent.com/BeanGreen247/jellyfin-storage-manager/main/manifest.json` |

4. Click **Save**

**Step 2 — Install the plugin**

1. Go to **Admin Dashboard → Plugins → Catalog**
2. Find **Storage Manager** in the list
3. Click **Install**
4. Restart Jellyfin when prompted

**Step 3 — Done**

After the restart, **Storage Manager** appears in the admin sidebar.

---

## Verifying the installation

After restarting Jellyfin, use these checks to confirm the plugin loaded correctly.

### 1. Check the Jellyfin logs (most reliable)

```bash
# systemd install
sudo journalctl -u jellyfin -n 100 | grep -i "storage\|plugin\|error"

# or via log file
sudo tail -100 /var/log/jellyfin/jellyfin*.log | grep -i "storage\|plugin\|error"
```

A successful load shows:
```
[INF] Loaded plugin: Storage Manager 1.0.1
```

An error shows something like:
```
[ERR] Failed to load assembly ... MissingMethodException ...
```

### 2. Check Admin → Plugins → My Plugins

The plugin should appear in the installed list with no warning icon.

### 3. Check the sidebar

**Storage Manager** should appear in the left sidebar under the Plugins section alongside any other installed plugins. Click it to open the storage overview.

---

## Troubleshooting

**Plugin installs but does not appear in the sidebar**

This almost always means the DLL failed to load at startup, usually due to a Jellyfin version mismatch. Check the logs (see above) for the exact error. Make sure you are running Jellyfin **10.11.x** — this plugin is not compatible with 10.9.x or 10.10.x.

**"Only zip packages are supported" error in the logs**

Jellyfin 10.11+ requires plugins to be distributed as `.zip` archives rather than bare `.dll` files. If you see this error, you are running an older release of this plugin. Uninstall it, then reinstall the latest version from the catalog — releases from v1.0.2 onwards are packaged correctly as zip archives.

**"Storage Manager was installed" appears twice in Alerts**

This just means it was installed more than once from the catalog. It is harmless — only one copy is active.

**Delete confirmation returns "Invalid credentials"**

Double-check that you are entering the Jellyfin username and password of an admin account, not the server's OS password.

**Auto-detect finds no media paths**

This happens if no libraries have been set up in Jellyfin yet, or the library paths are on a network share that the plugin cannot resolve. Go to **Admin → Plugins → Storage Manager → Settings** and add the path manually.

---

## Publishing a release (for maintainers)

This section explains how to cut a new version so the catalog manifest stays up to date.

### Using the automated workflow (recommended)

The repository includes a GitHub Actions release workflow at `.github/workflows/release.yml` that handles everything automatically when you push a version tag.

**Releasing a new version:**

```bash
git tag v1.2.0
git push origin v1.2.0
```

Pushing the tag triggers the release workflow, which:
1. Builds the plugin with the new version stamped in
2. Computes the MD5 checksum Jellyfin uses for verification
3. Prepends the new version entry to `manifest.json` and commits it back to `main`
4. Creates a GitHub Release and attaches the DLL as a download

Within a few minutes, the updated manifest URL will serve the new version and any Jellyfin instance with the repository added will show the update in **Admin → Plugins → Catalog**.

### How the manifest works

`manifest.json` is a JSON file hosted at the raw GitHub URL. Jellyfin fetches it when it checks for updates. Each entry in the `versions` array tells Jellyfin:

- Which version this is (`version`)
- The minimum Jellyfin server version required (`targetAbi` — currently `10.11.0.0`)
- Where to download the DLL (`sourceUrl` — points to the GitHub Release asset)
- The MD5 of that DLL (`checksum`) so Jellyfin can verify the download before installing

The release workflow fills all of this in automatically. You never need to edit `manifest.json` by hand.

---

## Configuration

Go to **Admin → Plugins → Storage Manager → Settings** to adjust these options:

| Setting | Default | Description |
|---|---|---|
| Auto-detect media drive | On | Uses Jellyfin library paths to find which drive holds media |
| Additional manageable paths | *(empty)* | Manually add extra paths to treat as manageable (one per line) |
| Require password before delete | On | Admin must re-enter their Jellyfin password before any deletion |
| Show non-media drives | On | Displays OS and cache drives in the overview (read-only) |

If auto-detect is on and you also add manual paths, both sets are combined.

---

## How the media drive is detected

The plugin reads the physical locations of all Jellyfin libraries (`Admin → Libraries`) and determines which filesystem root they live on. That root is treated as the manageable media drive.

**Example:** if your Movies library points to `/mnt/media/Movies`, the plugin marks `/mnt/media/` as manageable.

All other drives visible to the OS are shown in the overview but cannot be browsed or modified.

---

## Security

- Every API endpoint requires a valid Jellyfin session (`Authorization: MediaBrowser Token="..."`)
- Admin status is verified server-side on every request using `IAuthorizationContext` + `IUserManager`
- Before deletion, the plugin calls Jellyfin's own authentication pipeline to verify the supplied credentials, then immediately discards the temporary session — no credentials are stored
- All file paths are resolved with `Path.GetFullPath` and checked against the allowed media roots before any operation, preventing directory traversal attacks

---

## Building from source

```bash
git clone https://github.com/BeanGreen247/jellyfin-storage-manager.git
cd jellyfin-storage-manager
dotnet publish --configuration Release --output ./artifacts
# DLL is at: artifacts/Jellyfin.Plugin.StorageManager.dll
```

### Targeting a different Jellyfin version

Edit `Jellyfin.Plugin.StorageManager.csproj` and change both the package version and target framework to match your server:

| Jellyfin version | `TargetFramework` | `Jellyfin.Controller` version |
|---|---|---|
| 10.11.x | `net9.0` | `10.11.10` |
| 10.10.x | `net8.0` | `10.10.7` |
| 10.9.x | `net8.0` | `10.9.11` |

Run `dotnet restore` then rebuild after changing either value.

---

## CI / CD

| Workflow | File | Trigger | What it does |
|---|---|---|---|
| Build | `.github/workflows/build.yml` | Push / PR to `main` | Builds the plugin and uploads the DLL as a workflow artifact |
| Release | `.github/workflows/release.yml` | Push of a `v*` tag | Builds, stamps version, computes MD5, updates `manifest.json`, commits it back to `main`, creates a GitHub Release with the DLL attached |

---

## Project structure

```
jellyfin-storage-manager/
├── Jellyfin.Plugin.StorageManager.csproj
├── Plugin.cs                        # entry point, page registration
├── manifest.json                    # Jellyfin plugin repository manifest
├── Api/
│   └── StorageController.cs         # REST endpoints
├── Models/
│   ├── DriveInfoModel.cs
│   ├── FileSystemEntry.cs
│   ├── BrowseResponse.cs
│   ├── RenameRequest.cs
│   └── DeleteRequest.cs
├── Configuration/
│   └── PluginConfiguration.cs       # persisted XML settings
├── Web/
│   ├── storageManager.html          # main UI (drives + file browser)
│   └── configPage.html              # plugin settings page
├── deploy.sh                        # SSH deploy script (remote installs)
├── build-release.sh                 # local release preparation script
└── .github/workflows/
    ├── build.yml                    # CI — build on every push
    └── release.yml                  # CD — publish on version tag
```

---

## API reference

All endpoints are under `/StorageManager` and require admin authentication.

| Method | Path | Description |
|---|---|---|
| `GET` | `/StorageManager/drives` | List all drives with usage stats |
| `GET` | `/StorageManager/browse?path=…` | List files and folders at a path |
| `POST` | `/StorageManager/rename` | Rename a file or folder |
| `POST` | `/StorageManager/delete` | Delete a file or folder (password required) |
| `GET` | `/StorageManager/config` | Get current plugin configuration |
| `POST` | `/StorageManager/config` | Save plugin configuration |

---

## License

MIT
