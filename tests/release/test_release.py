import hashlib
import importlib.util
import json
from pathlib import Path
import stat
import sys
import tempfile
import unittest
from unittest.mock import patch
from types import SimpleNamespace
import zipfile

spec = importlib.util.spec_from_file_location("journal_release", Path(__file__).resolve().parents[2] / "scripts/release.py")
release = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = release
spec.loader.exec_module(release)


class ReleaseTests(unittest.TestCase):
    def test_calendar_release_and_dotnet_version(self):
        stable = release.parse_version("v2026.41.2")
        self.assertEqual(stable.version, "2026.41.2")
        self.assertEqual(stable.assembly_version, "2026.41.2.0")
        self.assertFalse(stable.prerelease)
        # Release count is independent of the number of calendar weeks.
        self.assertEqual(release.parse_version("2026.100.0").tag, "v2026.100.0")

    def test_prerelease(self):
        for label in ["alpha", "beta", "rc"]:
            self.assertTrue(release.parse_version(f"2026.1.0-{label}.1").prerelease)

    def test_update_feeds_are_separate_per_platform_and_filter_preview_on_github(self):
        stable = release.parse_version("2026.1.0")
        preview = release.parse_version("2026.2.0-beta.1")
        self.assertEqual(release.update_channel(stable, "win-x64"), "win-x64")
        self.assertEqual(release.update_channel(preview, "win-x64"), "win-x64")
        self.assertEqual(release.update_channel(stable, "linux-x64"), "linux-x64")

    def test_invalid_versions_cannot_reach_packaging(self):
        for value in ["2026.41", "2026.0.0", "2026.01.0", "2026.1.01", "2026.1.0-alpha.0", "2026.65535.0", "2026.1.65535", "2026.1.0; echo unsafe", "2026.1.0\nother=value"]:
            with self.subTest(value=value), self.assertRaises(ValueError):
                release.parse_version(value)

    def test_archive_checksum_metadata_and_executable(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            app = root / "publish"
            app.mkdir()
            binary = app / "Jornal.Desktop"
            binary.write_bytes(b"test launcher")
            binary.chmod(0o755)
            archive, checksum = release.create_archive(app, root / "release", release.parse_version("2026.1.0-alpha.1"), "linux-x64", "example-commit")
            self.assertEqual(checksum.read_text().split()[0], hashlib.sha256(archive.read_bytes()).hexdigest())
            with zipfile.ZipFile(archive) as bundle:
                info = json.loads(bundle.read("BUILD-INFO.json"))
                self.assertEqual(info["version"], "2026.1.0-alpha.1")
                self.assertEqual(info["commit"], "example-commit")
                self.assertIn(".NET 10", bundle.read("README-RUN.txt").decode())
                if sys.platform != "win32":
                    self.assertTrue(bundle.getinfo("Jornal.Desktop").external_attr >> 16 & stat.S_IXUSR)

    def test_database_is_not_packaged(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / "Jornal.Desktop.exe").write_bytes(b"test launcher")
            (root / "OrderDB.mdb").write_bytes(b"not a real database")
            with self.assertRaisesRegex(ValueError, "database"):
                release.create_archive(root, root / "out", release.parse_version("2026.1.0"), "win-x64", "commit")

    def windows_assets(self, root):
        version = release.parse_version("2026.1.0-alpha.11")
        updates = root / "updates"
        updates.mkdir()
        package = updates / f"DenisPapusha.Journal-{version.version}-win-x64-full.nupkg"
        package.write_bytes(b"ARTIFICIAL UPDATE PACKAGE")
        installer = root / f"Journal-{version.version}-win-x64-OfflineSetup.exe"
        installer.write_bytes(b"ARTIFICIAL OFFLINE INSTALLER")
        asset = dict(PackageId="DenisPapusha.Journal", Version=version.version, Type="Full",
                     FileName=package.name, Size=package.stat().st_size,
                     SHA256=hashlib.sha256(package.read_bytes()).hexdigest().upper())
        feed = updates / "releases.win-x64.json"
        feed.write_text(json.dumps(dict(Assets=[asset])), encoding="utf-8")
        return version, updates, installer, package, feed, asset

    def test_windows_release_contains_only_offline_installer_update_feed_and_one_checksum_file(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            version, updates, installer, package, feed, _ = self.windows_assets(root)
            for name in ["OtherVersion-full.nupkg", "Portable.zip", "Setup.exe", "RELEASES-win-x64",
                         "assets.win-x64.json", "package.sha256", "OrderDB.mdb", "debug.pdb"]:
                (updates / name).write_bytes(b"NOT FOR PUBLICATION")
            output = root / "release"
            actual = release.collect_windows_release(updates, installer, output, version)
            expected = {installer.name, package.name, feed.name, "SHA256SUMS"}
            self.assertEqual({path.name for path in actual}, expected)
            self.assertEqual({path.name for path in output.iterdir()}, expected)
            self.assertEqual((output / feed.name).read_bytes(), feed.read_bytes())
            lines = (output / "SHA256SUMS").read_text().splitlines()
            self.assertEqual(len(lines), 3)
            for line in lines:
                digest, name = line.split("  ")
                self.assertEqual(digest, hashlib.sha256((output / name).read_bytes()).hexdigest())

    def test_missing_or_inconsistent_windows_assets_are_rejected_before_copying(self):
        changes = dict(Version="2026.1.0-alpha.10", PackageId="OtherApp", Type="Delta",
                       FileName="../other.nupkg", Size=1, SHA256="0" * 64)
        for field, value in changes.items():
            with self.subTest(field=field), tempfile.TemporaryDirectory() as folder:
                root = Path(folder)
                version, updates, installer, _, feed, asset = self.windows_assets(root)
                asset[field] = value
                feed.write_text(json.dumps(dict(Assets=[asset])), encoding="utf-8")
                with self.assertRaises(ValueError):
                    release.collect_windows_release(updates, installer, root / "release", version)
                self.assertFalse((root / "release").exists())
        for missing in ["installer", "package", "feed"]:
            with self.subTest(missing=missing), tempfile.TemporaryDirectory() as folder:
                root = Path(folder)
                version, updates, installer, package, feed, _ = self.windows_assets(root)
                dict(installer=installer, package=package, feed=feed)[missing].unlink()
                with self.assertRaises(ValueError):
                    release.collect_windows_release(updates, installer, root / "release", version)
                self.assertFalse((root / "release").exists())

    def test_stale_release_output_is_not_mixed_with_a_new_release(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            version, updates, installer, _, _, _ = self.windows_assets(root)
            output = root / "release"
            output.mkdir()
            (output / "old-release.zip").write_bytes(b"PRESERVE")
            with self.assertRaisesRegex(ValueError, "empty"):
                release.collect_windows_release(updates, installer, output, version)
            self.assertEqual([path.name for path in output.iterdir()], ["old-release.zip"])
            self.assertEqual((output / "old-release.zip").read_bytes(), b"PRESERVE")

    def test_windows_pack_skips_portable_but_keeps_the_inner_setup_and_same_channel(self):
        with patch.object(release, "os", SimpleNamespace(name="nt")), patch.object(release.subprocess, "run") as run:
            release.package_updates("dotnet", Path("publish"), Path("updates"),
                                    release.parse_version("2026.1.0-alpha.11"), "win-x64")
        command = run.call_args_list[-1].args[0]
        self.assertIn("--noPortable", command)
        self.assertNotIn("--noInst", command)
        self.assertEqual(command[command.index("--channel") + 1], "win-x64")
        self.assertIn("net10.0-x64-runtime,net10.0-x86-runtime", command)

    def test_publish_validation_still_rejects_database_and_debug_files_without_creating_a_zip(self):
        for name in ["OrderDB.mdb", "Jornal.Desktop.pdb"]:
            with self.subTest(name=name), tempfile.TemporaryDirectory() as folder:
                root = Path(folder)
                (root / "Jornal.Desktop.exe").write_bytes(b"APP")
                (root / name).write_bytes(b"FORBIDDEN")
                with self.assertRaisesRegex(ValueError, "database/debug"):
                    release.validate_publish(root, "win-x64")

    def test_windows_package_wiring_skips_zip_and_rejects_private_files_before_velopack(self):
        for forbidden in [False, True]:
            with self.subTest(forbidden=forbidden), tempfile.TemporaryDirectory() as folder:
                root = Path(folder)
                version = release.parse_version("2026.1.0-alpha.11")
                def publish(command, **kwargs):
                    target = Path(command[-1])
                    target.mkdir(parents=True, exist_ok=True)
                    (target / ("Jornal.AccessHost.exe" if "-r" in command else "Jornal.Desktop.exe")).write_bytes(b"APP")
                    if forbidden:
                        (target / "OrderDB.mdb").write_bytes(b"PRIVATE DATABASE")
                def pack(dotnet, staging, output, requested, runtime):
                    self.assertEqual(requested, version)
                    self.windows_assets(Path(output).parent)
                    (Path(output) / "InnerSetup.exe").write_bytes(b"INNER SETUP")
                def installer(setup, host, output, requested):
                    self.assertEqual(setup.read_bytes(), b"INNER SETUP")
                    self.assertTrue((host / "Jornal.AccessHost.exe").exists())
                    target = output / f"Journal-{requested}-win-x64-OfflineSetup.exe"
                    target.write_bytes(b"OFFLINE SETUP")
                    return target
                with patch.object(release, "ROOT", root), patch.object(release.shutil, "which", return_value="dotnet"), \
                     patch.object(release.subprocess, "check_output", return_value="test-commit"), \
                     patch.object(release.subprocess, "run", side_effect=publish), \
                     patch.object(release, "create_archive") as archive, \
                     patch.object(release, "package_updates", side_effect=pack) as updates, \
                     patch.dict(sys.modules, windows_installer=SimpleNamespace(build_offline_installer=installer)), \
                     patch("builtins.print"):
                    if forbidden:
                        with self.assertRaisesRegex(ValueError, "database/debug"):
                            release.package(version, "win-x64", with_updates=True)
                        updates.assert_not_called()
                        self.assertFalse((root / "artifacts/release").exists())
                    else:
                        release.package(version, "win-x64", with_updates=True)
                        updates.assert_called_once()
                        self.assertEqual(len(list((root / "artifacts/release").iterdir())), 4)
                    archive.assert_not_called()


if __name__ == "__main__":
    unittest.main()
