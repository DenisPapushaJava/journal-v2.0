#!/usr/bin/env bash
set -euo pipefail
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
python3 -m unittest discover -s "$script_dir/../tests/release" -v
"$script_dir/dotnet.sh" restore Jornal.Modern.slnx --locked-mode
"$script_dir/dotnet.sh" build Jornal.Modern.slnx --configuration Release --no-restore
"$script_dir/dotnet.sh" test tests/Jornal.Tests/Jornal.Tests.csproj --configuration Release --no-build --no-restore
"$script_dir/dotnet.sh" test tests/Jornal.Access.Tests/Jornal.Access.Tests.csproj --configuration Release --no-build --no-restore
