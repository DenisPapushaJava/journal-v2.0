import hashlib
import importlib.util
import json
from pathlib import Path
import stat
import sys
import tempfile
import unittest
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


if __name__ == "__main__":
    unittest.main()
