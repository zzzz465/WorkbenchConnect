#!/usr/bin/env bash

set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

SKIP_BUILD=0
SKIP_PACKAGE=0
CONTENT_FOLDER="${WORKSHOP_CONTENT_FOLDER:-${WORKSHOP_STAGING_DIR:-$REPO_ROOT/dist/WorkbenchConnect}}"
VDF_PATH="${WORKSHOP_VDF_PATH:-$REPO_ROOT/dist/workshop_item.vdf}"
CHANGE_NOTE="${CHANGE_NOTE:-}"
STEAM_USER_NAME="${STEAM_USER:-}"

usage() {
  cat <<'USAGE'
Usage: scripts/publish-workshop.sh --steam-user USER [--changenote TEXT] [--skip-build] [--skip-package]

Builds, packages, and uploads the existing Steam Workshop item.

Environment overrides:
  STEAMCMD=/absolute/path/to/steamcmd
  STEAM_USER=steam_login_name
  STEAM_PASSWORD=optional_password_for_noninteractive_login
  WORKSHOP_STAGING_DIR=/path/to/staging
  WORKSHOP_VDF_PATH=/path/to/workshop_item.vdf
  CHANGE_NOTE="Workshop change note"
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --steam-user)
      [[ $# -ge 2 ]] || die "--steam-user requires a value"
      STEAM_USER_NAME="$2"
      shift 2
      ;;
    --changenote)
      [[ $# -ge 2 ]] || die "--changenote requires a value"
      CHANGE_NOTE="$2"
      shift 2
      ;;
    --content-folder)
      [[ $# -ge 2 ]] || die "--content-folder requires a directory"
      CONTENT_FOLDER="$2"
      SKIP_PACKAGE=1
      shift 2
      ;;
    --skip-build)
      SKIP_BUILD=1
      shift
      ;;
    --skip-package)
      SKIP_PACKAGE=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      die "Unknown argument: $1"
      ;;
  esac
done

[[ -n "$STEAM_USER_NAME" ]] || die "Steam user is required. Pass --steam-user USER or set STEAM_USER."

if [[ -z "$CHANGE_NOTE" ]]; then
  CHANGE_NOTE="$(git -C "$REPO_ROOT" log -1 --pretty=%s 2>/dev/null || true)"
fi
[[ -n "$CHANGE_NOTE" ]] || CHANGE_NOTE="Local Workshop update"

if [[ "$SKIP_PACKAGE" -eq 0 ]]; then
  package_args=()
  if [[ "$SKIP_BUILD" -eq 1 ]]; then
    package_args+=(--skip-build)
  fi
  package_args+=(--output "$CONTENT_FOLDER")
  "$SCRIPT_DIR/package-workshop.sh" "${package_args[@]}"
fi

[[ -d "$CONTENT_FOLDER" ]] || die "Workshop content folder does not exist: $CONTENT_FOLDER"

STEAMCMD_BIN="$(find_steamcmd || true)"
[[ -n "$STEAMCMD_BIN" ]] || die "steamcmd was not found. Install SteamCMD or set STEAMCMD=/absolute/path/to/steamcmd."

PUBLISHED_FILE_ID="$(published_file_id)"
PREVIEW_FILE="$CONTENT_FOLDER/About/Preview.png"

mkdir -p "$(dirname "$VDF_PATH")"

{
  printf '"workshopitem"\n'
  printf '{\n'
  printf '  "appid" "%s"\n' "$RIMWORLD_APP_ID"
  printf '  "publishedfileid" "%s"\n' "$PUBLISHED_FILE_ID"
  printf '  "contentfolder" "%s"\n' "$(vdf_escape "$CONTENT_FOLDER")"
  if [[ -f "$PREVIEW_FILE" ]]; then
    printf '  "previewfile" "%s"\n' "$(vdf_escape "$PREVIEW_FILE")"
  fi
  printf '  "changenote" "%s"\n' "$(vdf_escape "$CHANGE_NOTE")"
  printf '}\n'
} > "$VDF_PATH"

info "SteamCMD: $STEAMCMD_BIN"
info "Workshop VDF: $VDF_PATH"
info "PublishedFileId: $PUBLISHED_FILE_ID"

login_args=(+login "$STEAM_USER_NAME")
if [[ -n "${STEAM_PASSWORD:-}" ]]; then
  login_args=(+login "$STEAM_USER_NAME" "$STEAM_PASSWORD")
fi

"$STEAMCMD_BIN" "${login_args[@]}" +workshop_build_item "$VDF_PATH" +quit
