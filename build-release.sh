#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────
# build-release.sh — bump version, build, update manifest.json,
#                    and print the git commands to publish.
#
# Usage:
#   ./build-release.sh <version>
#
# Example:
#   ./build-release.sh 1.2.0
# ─────────────────────────────────────────────────────────────

set -euo pipefail

VERSION="${1:-}"

if [[ -z "$VERSION" ]]; then
    echo "Usage: $0 <version>   e.g. $0 1.2.0"
    exit 1
fi

# Basic semver check (x.y.z)
if ! [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "Error: version must be in x.y.z format (e.g. 1.2.0)"
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
CSPROJ="$SCRIPT_DIR/Jellyfin.Plugin.StorageManager.csproj"
MANIFEST="$SCRIPT_DIR/manifest.json"
DLL="$SCRIPT_DIR/artifacts/Jellyfin.Plugin.StorageManager.dll"
ZIP="$SCRIPT_DIR/artifacts/Jellyfin.Plugin.StorageManager.zip"

# ── 1. Stamp version into csproj ──────────────────────────────
echo "▶  Setting version to $VERSION …"
sed -i "s|<Version>.*</Version>|<Version>${VERSION}</Version>|" "$CSPROJ"
sed -i "s|<AssemblyVersion>.*</AssemblyVersion>|<AssemblyVersion>${VERSION}.0</AssemblyVersion>|" "$CSPROJ"
echo "✓  csproj updated."

# ── 2. Build ───────────────────────────────────────────────────
echo ""
echo "▶  Building …"
export PATH="$HOME/.dotnet:$PATH"
dotnet publish "$SCRIPT_DIR" --configuration Release --output "$SCRIPT_DIR/artifacts" --nologo -v quiet
echo "✓  Build complete."

# ── 3. Zip the DLL (Jellyfin 10.11+ requires a zip package) ───
(cd "$SCRIPT_DIR/artifacts" && zip -q Jellyfin.Plugin.StorageManager.zip Jellyfin.Plugin.StorageManager.dll)
echo "✓  Packaged: $ZIP"

# ── 4. Compute MD5 of the zip ─────────────────────────────────
MD5=$(md5sum "$ZIP" | awk '{print $1}')
echo "✓  MD5: $MD5"

# ── 5. Resolve GitHub repo for sourceUrl ──────────────────────
REMOTE=$(git -C "$SCRIPT_DIR" remote get-url origin 2>/dev/null || echo "")
REPO=""
if [[ "$REMOTE" =~ github\.com[:/](.+/.+?)(\.git)?$ ]]; then
    REPO="${BASH_REMATCH[1]}"
fi

if [[ -z "$REPO" ]]; then
    echo ""
    echo "⚠  Could not detect GitHub repo from git remote."
    echo "   Edit manifest.json manually and set the correct sourceUrl."
    SOURCE_URL="https://github.com/YOUR_GITHUB_USERNAME/jellyfin-storage-manager/releases/download/v${VERSION}/Jellyfin.Plugin.StorageManager.zip"
else
    SOURCE_URL="https://github.com/${REPO}/releases/download/v${VERSION}/Jellyfin.Plugin.StorageManager.zip"
    echo "✓  Repo: $REPO"
fi

# ── 6. Update manifest.json ────────────────────────────────────
echo ""
echo "▶  Updating manifest.json …"
TIMESTAMP=$(date -u +"%Y-%m-%dT%H:%M:%S.0000000Z")

python3 - <<PYEOF
import json, sys

with open("$MANIFEST") as f:
    data = json.load(f)

new_version = {
    "version": "${VERSION}.0",
    "changelog": "See the GitHub release notes for details.",
    "targetAbi": "10.11.0.0",
    "sourceUrl": "$SOURCE_URL",
    "checksum": "$MD5",
    "timestamp": "$TIMESTAMP",
}

data[0]["versions"].insert(0, new_version)

with open("$MANIFEST", "w") as f:
    json.dump(data, f, indent=2)
    f.write("\n")

print("✓  manifest.json updated with version ${VERSION}.0")
PYEOF

# ── 6. Print next steps ────────────────────────────────────────
echo ""
echo "─────────────────────────────────────────────────────────"
echo "  Done. Run these commands to publish the release:"
echo ""
echo "    git add manifest.json Jellyfin.Plugin.StorageManager.csproj"
echo "    git commit -m \"chore: release v${VERSION}\""
echo "    git tag v${VERSION}"
echo "    git push origin main v${VERSION}"
echo ""
echo "  Pushing the tag triggers the GitHub Actions release workflow,"
echo "  which will attach the DLL to the GitHub Release automatically."
echo "─────────────────────────────────────────────────────────"
