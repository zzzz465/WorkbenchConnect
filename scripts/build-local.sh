#!/usr/bin/env bash

set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

CONFIGURATION="${CONFIGURATION:-Release}"
PROJECT_PATH="$REPO_ROOT/Source/WorkbenchConnect/WorkbenchConnect.csproj"

DOTNET_BIN="$(find_dotnet || true)"
[[ -n "$DOTNET_BIN" ]] || die "dotnet was not found. Install the .NET SDK or set DOTNET=/absolute/path/to/dotnet."

info "Building WorkbenchConnect ($CONFIGURATION)"

"$DOTNET_BIN" build "$PROJECT_PATH" --configuration "$CONFIGURATION"

info "Build output: $REPO_ROOT/Assemblies/WorkbenchConnect.dll"
