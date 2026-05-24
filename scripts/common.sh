#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
RIMWORLD_APP_ID="294100"

die() {
  printf 'error: %s\n' "$*" >&2
  exit 1
}

info() {
  printf '%s\n' "$*"
}

first_existing_executable() {
  local candidate
  for candidate in "$@"; do
    if [[ -n "$candidate" && -x "$candidate" ]]; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done
  return 1
}

find_dotnet() {
  if [[ -n "${DOTNET:-}" ]]; then
    first_existing_executable "$DOTNET" && return 0
  fi

  if command -v dotnet >/dev/null 2>&1; then
    command -v dotnet
    return 0
  fi

  first_existing_executable \
    "$HOME/.dotnet/dotnet" \
    "/usr/local/share/dotnet/dotnet" \
    "/opt/homebrew/share/dotnet/dotnet" \
    "/usr/share/dotnet/dotnet"
}

find_steamcmd() {
  if [[ -n "${STEAMCMD:-}" ]]; then
    first_existing_executable "$STEAMCMD" && return 0
  fi

  if command -v steamcmd >/dev/null 2>&1; then
    command -v steamcmd
    return 0
  fi

  if command -v steamcmd.sh >/dev/null 2>&1; then
    command -v steamcmd.sh
    return 0
  fi

  first_existing_executable \
    "$HOME/Steam/steamcmd/steamcmd.sh" \
    "$HOME/steamcmd/steamcmd.sh" \
    "$HOME/Library/Application Support/Steam/steamcmd/steamcmd.sh" \
    "/opt/homebrew/bin/steamcmd" \
    "/usr/local/bin/steamcmd"
}

published_file_id() {
  local id_file="$REPO_ROOT/About/PublishedFileId.txt"
  [[ -f "$id_file" ]] || die "Missing $id_file"
  tr -d '[:space:]' < "$id_file"
}

vdf_escape() {
  sed 's/\\/\\\\/g; s/"/\\"/g' <<<"$1"
}
