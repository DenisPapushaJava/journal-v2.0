#!/usr/bin/env python3
"""Test a real AppImage update/restart using an isolated console probe."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time

ROOT = Path(__file__).resolve().parent.parent
root = Path(tempfile.mkdtemp(prefix="journal-update-test-"))
releases = root / "releases"
dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
env = dict(os.environ, JOURNAL_UPDATE_PROBE_ROOT=str(root), APPIMAGE_EXTRACT_AND_RUN="1")
env.setdefault("DOTNET_ROOT", str(Path(dotnet).resolve().parent))
# Only artificial sentinels outside the AppImage; no user profile or real Access database.
sentinels = {"OrderDB.mdb": b"ARTIFICIAL SENTINEL, NOT A DATABASE", "data-source.json": b'{"DatabasePath":"existing-OrderDB.mdb"}'}
for name, data in sentinels.items():
    (root / name).write_bytes(data)


def run(*args):
    subprocess.run([dotnet, *args], cwd=ROOT, env=env, check=True)


run("tool", "restore")
for version in ["2026.1.0", "2026.2.0"]:
    publish = root / ("publish-" + version)
    run("publish", "tests/Fixtures/UpdateProbe/Jornal.UpdateProbe.csproj", "-c", "Release", "-r", "linux-x64", "--self-contained", "false",
        "-p:Version=" + version, "-p:RestoreLockedMode=true", "-p:DebugType=None", "-p:DebugSymbols=false", "-o", str(publish))
    run("tool", "run", "vpk", "pack", "--packId", "Journal.UpdateProbe", "--packVersion", version,
        "--packDir", str(publish), "--mainExe", "Jornal.UpdateProbe", "--runtime", "linux-x64", "--channel", "linux-x64",
        "--outputDir", str(releases), "--icon", str(ROOT / "docs/design/assets/journal-icon.png"))
    if version == "2026.1.0":
        shutil.copy2(next(releases.glob("*.AppImage")), root / "probe-v1.AppImage")

with (root / "app.log").open("w") as log:
    process = subprocess.Popen([str(root / "probe-v1.AppImage")], env=env, stdout=log, stderr=subprocess.STDOUT)
    for _ in range(90):
        if (root / "failed.txt").exists():
            raise RuntimeError((root / "failed.txt").read_text())
        if (root / "result.json").exists():
            result = json.loads((root / "result.json").read_text())
            assert result == {"version": "2026.2.0", "restored": True}, result
            assert all((root / name).read_bytes() == data for name, data in sentinels.items()), "External data changed"
            print("AppImage 2026.1.0 → 2026.2.0: обновление, перезапуск и восстановление результата прошли. Внешние данные не изменились.", flush=True)
            break
        time.sleep(1)
    else:
        raise RuntimeError("Update timed out; diagnostic files: " + str(root))
# Test files remain in the temporary directory for diagnostics on the disposable runner.
