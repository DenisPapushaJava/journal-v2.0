#!/usr/bin/env bash
set -euo pipefail
repo_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_dir"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
if command -v dotnet >/dev/null 2>&1 && dotnet --version >/dev/null 2>&1; then
    exec dotnet "$@"
elif [[ -x "$HOME/.dotnet/dotnet" ]]; then
    export DOTNET_ROOT="$HOME/.dotnet"
    export PATH="$DOTNET_ROOT:$PATH"
    exec "$DOTNET_ROOT/dotnet" "$@"
else
    echo "Не найден .NET SDK. Выполните ./scripts/setup.sh" >&2
    exit 1
fi
