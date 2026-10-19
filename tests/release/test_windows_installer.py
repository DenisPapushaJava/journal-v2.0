import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("windows_installer", Path(__file__).resolve().parents[2] / "scripts/windows_installer.py")
installer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(installer)


class InstallerDependenciesTests(unittest.TestCase):
    def test_windows_powershell_does_not_inherit_incompatible_pwsh_modules(self):
        with patch.dict(installer.os.environ, {'PSModulePath': 'PowerShell7-only'}), \
                patch.object(installer.subprocess, 'run') as run:
            installer.verify_signature(Path('runtime.exe'), 'Microsoft Corporation')
            environment = run.call_args.kwargs['env']
            self.assertNotIn('PSModulePath', environment)
            self.assertEqual('runtime.exe', environment['JOURNAL_VERIFY_FILE'])
            self.assertEqual('Microsoft Corporation', environment['JOURNAL_VERIFY_PUBLISHER'])
            self.assertTrue(run.call_args.kwargs['check'])
            self.assertIn('Get-AuthenticodeSignature', run.call_args.args[0][-1])

    def test_warming_downloads_rechecks_each_signature_without_running_the_compiler(self):
        with tempfile.TemporaryDirectory() as directory:
            with patch.object(installer, 'ROOT', Path(directory)), patch.object(installer, 'os', SimpleNamespace(name='nt')), \
                    patch.object(installer, 'fetch', side_effect=lambda asset, cache: cache / asset['name']) as fetch, \
                    patch.object(installer, 'verify_signature') as signature, patch.object(installer.subprocess, 'run') as run:
                metadata, assets, cache = installer.prepare_dependencies()
                self.assertEqual(len(metadata['files']), fetch.call_count)
                self.assertEqual(len(assets), signature.call_count)
                for key, asset in metadata['files'].items(): signature.assert_any_call(cache / asset['name'], asset['publisher'])
                run.assert_not_called()

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
