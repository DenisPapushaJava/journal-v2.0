#!/usr/bin/env bash
set -euo pipefail
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
runtime="${1:-linux-x64}"
case "$runtime" in
    linux-x64|win-x64) ;;
    *) echo "Поддерживаемые цели публикации: linux-x64, win-x64." >&2; exit 1 ;;
esac
exec "$script_dir/dotnet.sh" publish src/Jornal.Desktop/Jornal.Desktop.csproj \
    --configuration Release --runtime "$runtime" --self-contained false \
    -p:RestoreLockedMode=true -p:DebugType=None -p:DebugSymbols=false \
    --output "artifacts/$runtime"
