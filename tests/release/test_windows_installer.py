import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("windows_installer", Path(__file__).resolve().parents[2] / "scripts/windows_installer.py")
installer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(installer)


class InstallerDependenciesTests(unittest.TestCase):
    def asset(self):
        return dict(name="runtime.exe", url="https://example.test/runtime.exe", algorithm="sha512", hash=hashlib.sha512(b"trusted").hexdigest())

    def test_verified_cached_dependency_can_be_used_without_network(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "runtime.exe"
            path.write_bytes(b"trusted")
            with patch.object(installer.urllib.request, "urlopen", side_effect=AssertionError("Network not expected")):
                self.assertEqual(installer.fetch(self.asset(), directory), path)

    def test_corrupted_cache_is_replaced_only_with_a_verified_download(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "runtime.exe"
            path.write_bytes(b"corrupt")
            with patch.object(installer.urllib.request, "urlopen", return_value=io.BytesIO(b"trusted")):
                installer.fetch(self.asset(), directory)
            self.assertEqual(path.read_bytes(), b"trusted")

    def test_bad_download_is_rejected_and_not_cached(self):
        with tempfile.TemporaryDirectory() as directory:
            with patch.object(installer.urllib.request, "urlopen", return_value=io.BytesIO(b"tampered")):
                with self.assertRaisesRegex(ValueError, "checksum"):
                    installer.fetch(self.asset(), directory)
            self.assertEqual(list(Path(directory).iterdir()), [])

    def test_manifest_pins_both_runtime_architectures_to_the_same_patch(self):
        metadata = json.loads(installer.MANIFEST.read_text())
        self.assertEqual(metadata["runtime_version"], "10.0." + str(metadata["runtime_patch"]))
        for architecture in ("x64", "x86"):
            asset = metadata["files"]["runtime_" + architecture]
            self.assertIn("dotnet-runtime-" + metadata["runtime_version"] + "-win-" + architecture, asset["name"])
            self.assertEqual(len(asset["hash"]), 128)

    def test_dependencies_cannot_escape_the_cache(self):
        with tempfile.TemporaryDirectory() as directory:
            asset = self.asset(); asset["name"] = "../other.exe"
            with self.assertRaises(ValueError):
                installer.fetch(asset, directory)
