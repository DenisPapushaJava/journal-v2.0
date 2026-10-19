#!/usr/bin/env python3
"""Validate Calendar Versions and package framework-dependent test builds."""

import argparse
from dataclasses import dataclass
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parent.parent
VERSION_PATTERN = re.compile(
    r"v?(?P<year>20\d{2})\.(?P<release>[1-9]\d*)\.(?P<patch>0|[1-9]\d*)"
    r"(?P<suffix>-(?:alpha|beta|rc)\.[1-9]\d*)?"
)
PRIVATE_EXTENSIONS = {".mdb", ".accdb", ".ldb", ".laccdb", ".pdb"}


@dataclass(frozen=True)
class ReleaseVersion:
    version: str
    assembly_version: str
    prerelease: bool

    @property
    def tag(self):
        return "v" + self.version


def parse_version(value):
    match = VERSION_PATTERN.fullmatch(value)
    if not match:
        raise ValueError("Expected YEAR.RELEASE.PATCH, optionally -alpha.N, -beta.N or -rc.N")
    year, release, patch = (int(match[k]) for k in ("year", "release", "patch"))
    if release > 65534 or patch > 65534:
        raise ValueError("Release and patch must fit .NET assembly version components (0..65534)")
    suffix = match["suffix"] or ""
    return ReleaseVersion(f"{year}.{release}.{patch}{suffix}", f"{year}.{release}.{patch}.0", bool(suffix))


def validate_publish(publish_dir, runtime):
    publish_dir = Path(publish_dir)
    files = sorted(p for p in publish_dir.rglob("*") if p.is_file())
    forbidden = [p.name for p in files if p.suffix.lower() in PRIVATE_EXTENSIONS]
    if forbidden:
        raise ValueError("Unexpected database/debug files in publish output: " + ", ".join(forbidden))
    launcher = "Jornal.Desktop.exe" if runtime == "win-x64" else "Jornal.Desktop"
    if not (publish_dir / launcher).is_file():
        raise ValueError(f"Missing launcher: {launcher}")
    return files


def create_archive(publish_dir, output_dir, version, runtime, commit):
    publish_dir, output_dir = Path(publish_dir), Path(output_dir)
    files = validate_publish(publish_dir, runtime)
    launcher = "Jornal.Desktop.exe" if runtime == "win-x64" else "Jornal.Desktop"
    output_dir.mkdir(parents=True, exist_ok=True)
    archive = output_dir / f"Journal-{version.version}-{runtime}.zip"
    with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED) as bundle:
        for path in files:
            bundle.write(path, path.relative_to(publish_dir).as_posix())
        bundle.writestr("BUILD-INFO.json", json.dumps({
            "version": version.version, "runtime": runtime, "commit": commit,
            "deployment": "framework-dependent", "stage": "specification-import",
        }, indent=2) + "\n")
        bundle.writestr("README-RUN.txt", (
            f"Journal {version.version} ({runtime})\n\n"
            "ПРЕДВАРИТЕЛЬНАЯ СБОРКА. ZIP предназначен для проверки и требует .NET.\n"
            "Для Windows используйте OfflineSetup.exe из выпуска: он содержит необходимые среды .NET.\n"
            "Нужна .NET 10 Runtime. В «Настройки → База данных» можно выбрать копию существующей MDB.\n"
            "На Windows статусы и БЛЦБ записываются в MDB транзакцией; на Linux MDB доступна только для чтения.\n"
            "На Windows доступны создание/редактирование с расчётом, статусы, БЛЦБ и локальная корзина.\n"
            "Импорт HTML БЛОК/Wintech и PDF HansLaser открывает одну или несколько заявок для проверки перед сохранением.\n"
            "Для первой проверки выбирайте отдельную копию MDB.\n"
            "Для времени резки на Windows подготавливается отдельная JournalOrderExtras, прежние поля Orders не меняются.\n"
            "Перед первой подготовкой закройте остальные подключения; рядом с MDB автоматически создаётся резервная копия.\n"
            "На Linux доступна отдельная редактируемая .journal-test.json с данными и прайсами, без обратной записи в MDB.\n"
            "MDB на Windows обслуживает вложенный модуль AccessHost x86; нужен также .NET Runtime x86.\n"
            "На Linux нужен полный mdbtools (mdb-export и mdb-tables).\n"
            "Распакуйте архив целиком, затем запустите " + launcher + ".\n"
            "На Linux при необходимости разрешите запуск: chmod +x Jornal.Desktop\n"
            "Для проверки этой сборки не заменяйте и не перемещайте существующий OrderDB.mdb.\n"
        ))
    checksum = archive.with_suffix(archive.suffix + ".sha256")
    checksum.write_text(hashlib.sha256(archive.read_bytes()).hexdigest() + "  " + archive.name + "\n", encoding="utf-8")
    return archive, checksum


def collect_windows_release(updates_dir, offline_setup, output_dir, version):
    updates_dir, offline_setup, output_dir = Path(updates_dir), Path(offline_setup), Path(output_dir)
    package_name = f"DenisPapusha.Journal-{version.version}-win-x64-full.nupkg"
    package = updates_dir / package_name
    feed = updates_dir / "releases.win-x64.json"
    if offline_setup.name != f"Journal-{version.version}-win-x64-OfflineSetup.exe":
        raise ValueError("Offline installer version does not match the release")
    files = [offline_setup, package, feed]
    if any(not path.is_file() or path.stat().st_size == 0 for path in files):
        raise ValueError("Missing Windows installer, full update package or feed")
    hashes = {}
    for path in files:
        with path.open("rb") as source:
            hashes[path.name] = hashlib.file_digest(source, "sha256").hexdigest()
    metadata = json.loads(feed.read_text(encoding="utf-8"))
    assets = metadata.get("Assets") if isinstance(metadata, dict) else None
    if not isinstance(assets, list) or len(assets) != 1 or not isinstance(assets[0], dict):
        raise ValueError("Expected one full Windows update in the release feed")
    asset = assets[0]
    if (asset.get("FileName") != package_name or asset.get("Version") != version.version or
            asset.get("PackageId") != "DenisPapusha.Journal" or asset.get("Type") != "Full" or
            asset.get("Size") != package.stat().st_size or
            str(asset.get("SHA256", "")).lower() != hashes[package_name]):
        raise ValueError("Windows update feed does not match the release package")
    output_dir.mkdir(parents=True, exist_ok=True)
    if any(output_dir.iterdir()):
        raise ValueError("Release output must be empty; use a clean artifacts/release directory")
    for path in files:
        shutil.copy2(path, output_dir / path.name)
    checksum = output_dir / "SHA256SUMS"
    checksum.write_text("".join(f"{hashes[name]}  {name}\n" for name in sorted(hashes)), encoding="utf-8", newline="\n")
    return [output_dir / path.name for path in files] + [checksum]


def update_channel(version, runtime):
    # Канал в feed разделён по ОС/архитектуре; stable/preview фильтруется признаком GitHub prerelease.
    return runtime


def package_updates(dotnet, staging, output_dir, version, runtime):
    if (runtime.startswith("win") and os.name != "nt") or (runtime.startswith("linux") and sys.platform != "linux"):
        raise ValueError("Velopack packaging must run on the target OS")
    subprocess.run([dotnet, "tool", "restore"], cwd=ROOT, check=True)
    command = [dotnet, "tool", "run", "vpk", "pack", "--packId", "DenisPapusha.Journal",
        "--packTitle", "Journal", "--packAuthors", "DenisPapusha",
        "--packVersion", version.version, "--packDir", str(staging),
        "--mainExe", "Jornal.Desktop.exe" if runtime.startswith("win") else "Jornal.Desktop",
        "--runtime", runtime, "--channel", update_channel(version, runtime),
        "--outputDir", str(output_dir), "--releaseNotes", str(ROOT / "docs/release-notes.md")]
    if runtime.startswith("win"):
        command += ["--noPortable", "--framework", "net10.0-x64-runtime,net10.0-x86-runtime", "--icon", str(ROOT / "docs/design/assets/journal-icon.ico")]
    else:
        command += ["--icon", str(ROOT / "docs/design/assets/journal-icon.png"), "--categories", "Office"]
    subprocess.run(command, cwd=ROOT, check=True)
    if runtime != "win-x64":
        for path in output_dir.iterdir():
            if path.is_file() and path.suffix != ".sha256":
                path.with_name(path.name + ".sha256").write_text(hashlib.sha256(path.read_bytes()).hexdigest() + "  " + path.name + "\n", encoding="utf-8")


def package(version, runtime, with_updates=False):
    dotnet = shutil.which("dotnet")
    if not dotnet:
        candidate = Path.home() / ".dotnet" / ("dotnet.exe" if os.name == "nt" else "dotnet")
        if candidate.is_file():
            dotnet = str(candidate)
    if not dotnet:
        raise ValueError(".NET SDK not found; run scripts/setup.sh first")
    work_dir = ROOT / "artifacts"
    work_dir.mkdir(exist_ok=True)
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    with tempfile.TemporaryDirectory(prefix="journal-package-", dir=work_dir) as workspace:
        staging = Path(workspace) / "publish"
        subprocess.run([
            dotnet, "publish", "src/Jornal.Desktop/Jornal.Desktop.csproj",
            "--configuration", "Release", "--runtime", runtime, "--self-contained", "false",
            f"-p:Version={version.version}", f"-p:AssemblyVersion={version.assembly_version}",
            f"-p:FileVersion={version.assembly_version}", f"-p:SourceRevisionId={commit}",
            "-p:ContinuousIntegrationBuild=true", "-p:RestoreLockedMode=true",
            "-p:DebugType=None", "-p:DebugSymbols=false", "--output", str(staging),
        ], cwd=ROOT, check=True)
        if runtime == "win-x64":
            subprocess.run([dotnet, "publish", "src/Jornal.AccessHost/Jornal.AccessHost.csproj", "-c", "Release",
                "-r", "win-x86", "--self-contained", "false", f"-p:Version={version.version}",
                f"-p:SourceRevisionId={commit}", "-p:ContinuousIntegrationBuild=true", "-p:RestoreLockedMode=true",
                "-p:DebugType=None", "-p:DebugSymbols=false", "-o", str(Path(staging) / "AccessHost")], cwd=ROOT, check=True)
        if with_updates and runtime == "win-x64":
            validate_publish(staging, runtime)
        else:
            for path in create_archive(staging, work_dir / "release", version, runtime, commit):
                print(path.relative_to(ROOT))
        if with_updates:
            updates_dir = Path(workspace) / "updates"
            package_updates(dotnet, staging, updates_dir, version, runtime)
            if runtime == "win-x64":
                from windows_installer import build_offline_installer
                app_setup, = updates_dir.glob("*Setup.exe")
                installer_dir = Path(workspace) / "installer"
                installer_dir.mkdir()
                offline_setup = build_offline_installer(app_setup, staging / "AccessHost", installer_dir, version.version)
                for path in collect_windows_release(updates_dir, offline_setup, work_dir / "release", version):
                    print(path.relative_to(ROOT))
            else:
                for asset in updates_dir.iterdir():
                    if asset.is_file(): shutil.copy2(asset, work_dir / "release" / asset.name)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    validate = commands.add_parser("validate")
    validate.add_argument("--version", required=True)
    validate.add_argument("--github-output", type=Path)
    build = commands.add_parser("package")
    build.add_argument("--version", required=True)
    build.add_argument("--runtime", choices=["win-x64", "linux-x64"], required=True)
    build.add_argument("--updates", action="store_true", help="Also build installer/AppImage and Velopack update feed")
    args = parser.parse_args()
    try:
        version = parse_version(args.version)
        if args.command == "validate":
            print(version.version)
            if args.github_output:
                with args.github_output.open("a", encoding="utf-8") as output:
                    output.write(f"version={version.version}\ntag={version.tag}\n")
                    output.write(f"prerelease={str(version.prerelease).lower()}\n")
        else:
            package(version, args.runtime, args.updates)
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        parser.exit(1, f"Release error: {error}\n")


if __name__ == "__main__":
    main()
