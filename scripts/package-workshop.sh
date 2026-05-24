#!/usr/bin/env bash

set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

SKIP_BUILD=0
STAGING_DIR="${WORKSHOP_STAGING_DIR:-$REPO_ROOT/dist/WorkbenchConnect}"

usage() {
  cat <<'USAGE'
Usage: scripts/package-workshop.sh [--skip-build] [--output DIR]

Builds the mod and creates a clean Steam Workshop content folder.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --skip-build)
      SKIP_BUILD=1
      shift
      ;;
    --output)
      [[ $# -ge 2 ]] || die "--output requires a directory"
      STAGING_DIR="$2"
      shift 2
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

if [[ "$SKIP_BUILD" -eq 0 ]]; then
  "$SCRIPT_DIR/build-local.sh"
fi

[[ -f "$REPO_ROOT/About/About.xml" ]] || die "Missing About/About.xml"
[[ -f "$REPO_ROOT/Assemblies/WorkbenchConnect.dll" ]] || die "Missing Assemblies/WorkbenchConnect.dll"

rm -rf "$STAGING_DIR"
mkdir -p "$STAGING_DIR"

copy_if_present() {
  local path="$1"
  if [[ -e "$REPO_ROOT/$path" ]]; then
    mkdir -p "$(dirname "$STAGING_DIR/$path")"
    cp -R "$REPO_ROOT/$path" "$STAGING_DIR/$path"
  fi
}

for path in \
  About \
  Assemblies \
  Languages \
  Defs \
  Patches \
  Sounds \
  Textures \
  Common \
  1.6 \
  1.5 \
  1.4 \
  LoadFolders.xml; do
  copy_if_present "$path"
done

find "$STAGING_DIR" -name '.DS_Store' -delete
find "$STAGING_DIR" -name '.gitkeep' -delete
find "$STAGING_DIR" \( -name '*.pdb' -o -name '*.mdb' \) -delete

[[ -f "$STAGING_DIR/About/About.xml" ]] || die "Package is missing About/About.xml"
[[ -f "$STAGING_DIR/Assemblies/WorkbenchConnect.dll" ]] || die "Package is missing Assemblies/WorkbenchConnect.dll"

if [[ ! -f "$STAGING_DIR/About/Preview.png" ]]; then
  info "warning: About/Preview.png is missing; Steam Workshop preview will not be updated."
fi

info "Workshop content folder: $STAGING_DIR"
