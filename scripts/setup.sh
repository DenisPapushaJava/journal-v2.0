#!/usr/bin/env bash
set -euo pipefail
repo_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_dir"
sdk_version="$(sed -n 's/.*"version": "\([^"]*\)".*/\1/p' global.json)"
if ! ./scripts/dotnet.sh --version >/dev/null 2>&1; then
    command -v curl >/dev/null 2>&1 || { echo "Для установки SDK нужен curl." >&2; exit 1; }
    installer_file="$(mktemp)"
    trap 'rm -f "$installer_file"' EXIT
    curl --fail --location --silent --show-error https://dot.net/v1/dotnet-install.sh --output "$installer_file"
    bash "$installer_file" --version "$sdk_version" --install-dir "$HOME/.dotnet" --no-path
fi
./scripts/dotnet.sh --version
./scripts/dotnet.sh restore Jornal.Modern.slnx --locked-mode
echo "Готово. Запуск: ./scripts/dev.sh | Проверки: ./scripts/check.sh"
