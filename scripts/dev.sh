#!/usr/bin/env bash
set -euo pipefail
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
if [[ -z "${JOURNAL_MDB_EXPORT:-}" && -x "$script_dir/../.local/tools/mdb-export" ]]; then
  export JOURNAL_MDB_EXPORT="$script_dir/../.local/tools/mdb-export"
fi
exec "$script_dir/dotnet.sh" run --project src/Jornal.Desktop/Jornal.Desktop.csproj "$@"
