#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────
# deploy.sh — build & push Storage Manager plugin to a remote
#             Jellyfin server over SSH.
#
# Usage:
#   ./deploy.sh <user@host>
#   ./deploy.sh <user@host> [jellyfin-plugin-dir]
#
# Examples:
#   ./deploy.sh bean@192.168.1.50
#   ./deploy.sh bean@myserver.local /opt/jellyfin/plugins
#   ./deploy.sh bean@myserver.local          # uses default path
#
# The script will:
#   1. Build the plugin locally (Release)
#   2. scp the DLL to the remote server
#   3. ssh to create the plugin directory and restart Jellyfin
# ─────────────────────────────────────────────────────────────

set -euo pipefail

# ── Args ──────────────────────────────────────────────────────
TARGET="${1:-}"
REMOTE_PLUGIN_DIR="${2:-/var/lib/jellyfin/plugins/StorageManager}"

if [[ -z "$TARGET" ]]; then
    echo "Usage: $0 <user@host> [jellyfin-plugin-dir]"
    echo ""
    echo "  <user@host>           SSH target, e.g. bean@192.168.1.50"
    echo "  [jellyfin-plugin-dir] Plugin directory on remote server"
    echo "                        Default: /var/lib/jellyfin/plugins/StorageManager"
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
DLL_NAME="Jellyfin.Plugin.StorageManager.dll"
LOCAL_DLL="$SCRIPT_DIR/artifacts/$DLL_NAME"

# ── Step 1: build locally ──────────────────────────────────────
echo ""
echo "▶  Building plugin (Release)..."
dotnet publish "$SCRIPT_DIR" \
    --configuration Release \
    --output "$SCRIPT_DIR/artifacts" \
    --nologo \
    -v quiet

if [[ ! -f "$LOCAL_DLL" ]]; then
    echo "✗  Build failed — $LOCAL_DLL not found."
    exit 1
fi
echo "✓  Build complete: $LOCAL_DLL"

# ── Step 2: copy DLL to remote ─────────────────────────────────
echo ""
echo "▶  Copying $DLL_NAME → $TARGET:$REMOTE_PLUGIN_DIR ..."

# Create the remote directory first, then scp
ssh "$TARGET" "mkdir -p '$REMOTE_PLUGIN_DIR'"
scp "$LOCAL_DLL" "$TARGET:$REMOTE_PLUGIN_DIR/$DLL_NAME"
echo "✓  File copied."

# ── Step 3: restart Jellyfin on remote ─────────────────────────
echo ""
echo "▶  Restarting Jellyfin on $TARGET ..."

# Try systemctl first; fall back to service command; skip if neither works.
ssh "$TARGET" bash <<'REMOTE'
if command -v systemctl &>/dev/null && systemctl is-active --quiet jellyfin 2>/dev/null; then
    sudo systemctl restart jellyfin
    echo "✓  Jellyfin restarted via systemctl."
elif command -v service &>/dev/null; then
    sudo service jellyfin restart
    echo "✓  Jellyfin restarted via service."
else
    echo "⚠  Could not detect how to restart Jellyfin."
    echo "   Please restart it manually on the remote server."
fi
REMOTE

echo ""
echo "✓  Done! Storage Manager plugin deployed to $TARGET"
echo "   Jellyfin admin → left sidebar → Storage Manager"
