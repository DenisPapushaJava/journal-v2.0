import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('ci_scope', ROOT / 'scripts/ci_scope.py')
ci = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ci)


class ScopeTests(unittest.TestCase):
    def test_docs_and_editor_only_are_quick_but_binary_assets_are_not(self):
        paths = ['README.md', 'AGENTS.md', 'LICENSE', 'docs/pricing.md',
                 'docs/screenshots/main.png', 'docs/screenshots/dark.webp',
                 '.agents/skills/jornal-modernization/SKILL.md', '.github/pull_request_template.md',
                 '.vscode/launch.json', '.vscode/tasks.json', '.vscode/settings.json', '.vscode/extensions.json']
        self.assertEqual(ci.classify(paths), {'code': False, 'packaging': False})
        for path in ['docs/design/assets/journal-icon.png', 'logo.png', '.vscode/not-a-known-setting.json',
                     'docs/screenshots/tool.exe', 'docs/screenshots-other/main.png',
                     'unrecognized-build-file', 'services/new-service/run.ts', 'src/NewHost/Program.cs']:
            with self.subTest(path=path): self.assertEqual(ci.classify([path]), {'code': True, 'packaging': True})

    def test_regular_app_changes_need_native_tests_without_packaging(self):
        for path in ['src/Jornal.Desktop/Views/MainWindow.axaml', 'src/Jornal.Desktop/ViewModels/NewOrderViewModel.cs',
                     'src/Jornal.Desktop/Clipboard/ResultClipboard.cs', 'src/Jornal.Parsing/Documents/DocumentReader.cs',
                     'src/Jornal.Core/Results/OrderResultFormatter.cs', 'src/Jornal.Infrastructure/Local/LocalJournalStore.cs',
                     'tests/Jornal.Tests/WaterjetPdfParserTests.cs']:
            with self.subTest(path=path): self.assertEqual(ci.classify(['docs/pricing.md', path]), {'code': True, 'packaging': False})

    def test_packaging_and_shared_host_contracts_require_full_cycle(self):
        for path in ['.github/workflows/modern.yml', '.github/workflows/release.yml', 'scripts/publish.sh',
                     '.config/dotnet-tools.json', 'global.json', 'Directory.Build.props', 'Directory.Packages.props',
                     'src/Jornal.Desktop/packages.lock.json', 'src/Jornal.Core/Jornal.Core.csproj',
                     'src/Jornal.Desktop/Updates/JournalGithubSource.cs', 'src/Jornal.Desktop/ViewModels/MainWindowViewModel.Updates.cs',
                     'src/Jornal.Desktop/Views/AppUpdatesWindow.axaml', 'src/Jornal.Desktop/Startup/StartupCoordinator.cs',
                     'src/Jornal.Desktop/Program.cs', 'src/Jornal.Desktop/DataSources/OrderSourceSettings.cs',
                     'src/Jornal.AccessHost/Program.cs', 'src/Jornal.Infrastructure/Access/AccessHostProtocol.cs',
                     'src/Jornal.Core/Orders/ManualOrderDraft.cs', 'src/Jornal.Core/Pricing/PriceTable.cs',
                     'tests/Fixtures/New-AccessFixture.ps1', 'tests/Jornal.Access.Tests/NativeAccessTests.cs',
                     'tests/Jornal.Tests/AccessHostTests.cs', 'tests/Jornal.Tests/AppUpdateTests.cs', 'tests/release/test_ci_scope.py']:
            with self.subTest(path=path): self.assertEqual(ci.classify([path]), {'code': True, 'packaging': True})

    def test_gate_never_treats_failure_cancel_skip_or_missing_output_as_success(self):
        for state in ['failure', 'cancelled', 'skipped', '', None]:
            with self.subTest(state=state), self.assertRaises(ValueError): ci.gate(state, 'false', 'false')
        for code, packaging in [('false', 'true'), ('', 'false'), ('true', None), ('False', 'false')]:
            with self.subTest(code=code, packaging=packaging), self.assertRaises(ValueError): ci.gate('success', code, packaging)
        for code, packaging in [('false', 'false'), ('true', 'false'), ('true', 'true')]: ci.gate('success', code, packaging)

    def test_release_stays_unconditional_and_protected_checks_have_an_explicit_gate(self):
        workflow = (ROOT / '.github/workflows/modern.yml').read_text(encoding='utf-8')
        self.assertNotIn('paths-ignore:', workflow); self.assertNotIn('\n  paths:', workflow)
        build = workflow.split('\n  build:', 1)[1].split('\n  native:', 1)[0]
        self.assertIn('needs: scope', build)
        self.assertIn('if: ${{ !cancelled() }}', build)
        self.assertNotIn('if: ${{ always() }}\n    timeout-minutes:', build)
        self.assertNotIn('needs: [scope, native]', build)
        native = workflow.split('\n  native:', 1)[1].split('\n  checks:', 1)[0]
        self.assertIn("needs.scope.outputs.code == 'true'", native)
        self.assertNotIn('outputs.packaging', native)
        self.assertIn('uses: ./.github/workflows/native-access.yml', native)
        checks = workflow.split('\n  checks:', 1)[1]
        self.assertIn('name: Check (${{ matrix.os }})', checks)
        self.assertIn('needs: [scope, build, native]', checks)
        self.assertIn('if: ${{ always() }}', checks)
        self.assertIn('scripts/ci_scope.py jobs-gate', checks)
        for variable in ['SCOPE_RESULT', 'CHECK_CODE', 'CHECK_PACKAGING', 'BUILD_RESULT', 'NATIVE_RESULT']:
            self.assertIn(variable + ': ${{ needs.', checks)
        jet = (ROOT / '.github/workflows/native-access.yml').read_text(encoding='utf-8')
        self.assertIn('architecture: x86', jet)
        self.assertIn("JOURNAL_REQUIRE_NATIVE_TESTS: '1'", jet)
        self.assertIn('-RequireProvider', jet)
        self.assertIn('--locked-mode', jet)
        release = (ROOT / '.github/workflows/release.yml').read_text(encoding='utf-8')
        self.assertNotIn('needs.scope', release)
        self.assertIn('uses: ./.github/workflows/native-access.yml', release)
        self.assertIn('needs: [package, native]', release.split('\n  draft:', 1)[1])
        for step in ['Test Windows offline installer and Access host', 'Test Windows install and restart']:
            self.assertIn(step, release)

    def test_only_windows_is_packaged_but_linux_tests_and_release_gates_remain(self):
        for name, step in [('modern.yml', 'Package update artifacts'), ('release.yml', 'Package')]:
            text = (ROOT / '.github/workflows' / name).read_text(encoding='utf-8')
            self.assertIn('os: ubuntu-latest', text)
            self.assertIn('os: windows-latest', text)
            self.assertIn('dotnet test tests/Jornal.Tests/Jornal.Tests.csproj', text)
            packaging = text.split('- name: ' + step + '\n', 1)[1].split('      - name:', 1)[0]
            self.assertIn("runner.os == 'Windows'", packaging)
            self.assertIn('--runtime win-x64 --updates', packaging)
            self.assertNotIn('test-update-install-linux.py', text)
        text = (ROOT / '.github/workflows/release.yml').read_text(encoding='utf-8')
        draft = text.split('\n  draft:', 1)[1]
        self.assertIn('needs: [package, native]', draft)
        self.assertIn('name: package-win-x64', draft)
        self.assertNotIn('pattern: package-*', draft)
        probe = (ROOT / 'scripts/test-update-install.ps1').read_text(encoding='utf-8')
        self.assertIn('--noPortable', probe)
        self.assertIn("$_.Name -ne 'releases.win-x64.json'", probe)
        self.assertIn("$_.Name -notlike '*-full.nupkg'", probe)

    def test_job_gate_rejects_missing_failed_cancelled_or_unexpectedly_skipped_native_checks(self):
        for state in ['failure', 'cancelled', 'skipped', '', None]:
            with self.subTest(build=state), self.assertRaises(ValueError):
                ci.jobs_gate('success', 'true', 'false', state, 'success')
            with self.subTest(native=state), self.assertRaises(ValueError):
                ci.jobs_gate('success', 'true', 'false', 'success', state)
        ci.jobs_gate('success', 'true', 'true', 'success', 'success')
        ci.jobs_gate('success', 'false', 'false', 'success', 'skipped')
        with self.assertRaises(ValueError): ci.jobs_gate('success', 'false', 'false', 'success', 'cancelled')
        with self.assertRaises(ValueError): ci.jobs_gate('failure', 'false', 'false', 'success', 'skipped')

    def test_unverified_push_gets_a_full_run_even_for_documentation(self):
        self.assertEqual(ci.push_scope(False, 'нет подтверждения'), {'code': True, 'packaging': True, 'reason': 'нет подтверждения'})
        self.assertEqual(ci.push_scope(True, 'проверен'), {'code': False, 'packaging': False, 'reason': 'проверен'})

    def test_caches_cover_locked_packages_and_raw_verified_installer_downloads(self):
        for name in ['modern.yml', 'release.yml', 'native-access.yml']:
            text = (ROOT / '.github/workflows' / name).read_text(encoding='utf-8')
            self.assertIn('path: .nuget/packages', text)
            self.assertIn("key: nuget-${{ runner.os }}-${{ hashFiles('**/packages.lock.json', '.config/dotnet-tools.json') }}", text)
        self.assertIn('uses: actions/cache/restore@', (ROOT / '.github/workflows/native-access.yml').read_text())
        for name in ['modern.yml', 'release.yml']:
            text = (ROOT / '.github/workflows' / name).read_text(encoding='utf-8')
            self.assertIn('path: artifacts/installer-dependencies/*.exe', text)
            self.assertIn("hashFiles('installer/windows/dependencies.json')", text)
            self.assertNotIn('path: artifacts/installer-dependencies\n', text)

    def test_verified_main_warms_missing_caches_without_rebuilding_the_application(self):
        text = (ROOT / '.github/workflows/modern.yml').read_text(encoding='utf-8')
        self.assertIn("lookup-only: ${{ needs.scope.outputs.code != 'true' }}", text)
        self.assertIn("github.event_name == 'push' && steps.nuget.outputs.cache-hit != 'true'", text)
        self.assertIn('python scripts/windows_installer.py --download-only', text)
        smoke = text.split('- name: Verify installer downloads from PowerShell', 1)[1].split('- name:', 1)[0]
        self.assertIn('shell: pwsh', smoke)
        self.assertLess(text.index('- name: Verify installer downloads from PowerShell'), text.index('run: dotnet build'))
        self.assertIn("needs.scope.outputs.packaging == 'true'", smoke)
        legacy = text.split('- name: Verify legacy signature checks from pwsh', 1)[1].split('- name:', 1)[0]
        self.assertIn('shell: pwsh', legacy)
        self.assertIn('shutil.which=lambda _: None', legacy)
        self.assertIn("lookup-only: ${{ needs.scope.outputs.packaging != 'true' }}", text)
        build = text.split('run: dotnet build', 1)[0].rsplit('      - if:', 1)[1]
        self.assertIn("needs.scope.outputs.code == 'true'", build)
        self.assertNotIn('cache-hit', build)

    def test_editor_comments_and_trailing_commas_do_not_hide_invalid_json(self):
        value = ci.parse_editor_json('{"url":"https://example.invalid/*path*/",/*заметка*/ "items":[1,2,],// строка\n}')
        self.assertEqual(value, {'url': 'https://example.invalid/*path*/', 'items': [1, 2]})
        for text in ['{"a":1,,}', '[,,]', '{"a": 1 /*no end', '{"a": }']:
            with self.subTest(text=text), self.assertRaises(ValueError): ci.parse_editor_json(text)

    def test_repository_json_settings_are_valid(self): ci.check_repository()

    def test_dispatch_and_unknown_range_always_request_full_checks(self):
        for event, payload in [('workflow_dispatch', {}), ('push', {'before': '0'*40, 'after': 'a'*40}),
                               ('pull_request', {}), ('unexpected', {})]:
            with self.subTest(event=event):
                result = ci.determine(event, payload); self.assertTrue(result['code']); self.assertTrue(result['packaging'])

    def test_real_git_diff_includes_all_push_commits_and_both_sides_of_a_rename(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            def git(*args): return ci.git(*args, root=root).decode().strip()
            def commit(path, text):
                target = root / path; target.parent.mkdir(parents=True, exist_ok=True); target.write_text(text, encoding='utf-8')
                git('add', '--all'); git('commit', '-m', 'Проверка'); return git('rev-parse', 'HEAD')
            git('init', '-b', 'main'); git('config', 'user.name', 'Тест'); git('config', 'user.email', 'test@example.invalid')
            base = commit('README.md', 'Описание\n')
            code = commit('src/Jornal.Desktop/Views/Example.axaml', '<View />\n')
            head = commit('docs/help.md', 'Справка\n')
            result = ci.determine('push', {'before': base, 'after': head}, root)
            self.assertTrue(result['code']); self.assertFalse(result['packaging'])
            # Если учитывать только последний коммит, здесь ошибочно получатся docs-only.
            self.assertFalse(ci.determine('push', {'before': code, 'after': head}, root)['code'])
            git('mv', 'src/Jornal.Desktop/Views/Example.axaml', 'docs/renamed.md'); git('commit', '-m', 'Переименование')
            result = ci.determine('push', {'before': head, 'after': git('rev-parse', 'HEAD')}, root)
            self.assertTrue(result['code']); self.assertFalse(result['packaging'])
            asset = commit('docs/design/assets/logo.png', 'synthetic image')
            git('switch', '-c', 'screenshot', asset)
            (root / 'docs/screenshots').mkdir(parents=True)
            git('mv', 'docs/design/assets/logo.png', 'docs/screenshots/moved.png')
            git('commit', '-m', 'Перенос изображения')
            result = ci.determine('pull_request', {'pull_request': {'base': {'sha': asset},
                'head': {'sha': git('rev-parse', 'HEAD')}}}, root)
            self.assertTrue(result['code']); self.assertTrue(result['packaging'])
            # PR с отставшей базой не должен включать чужие изменения main.
            git('switch', '-c', 'feature', base); feature = commit('docs/feature.md', 'Описание функции\n')
            result = ci.determine('pull_request', {'pull_request': {'base': {'sha': head}, 'head': {'sha': feature}}}, root)
            self.assertFalse(result['code']); self.assertFalse(result['packaging'])
            dirty = commit('docs/feature.md', 'Пробелы   \n')
            with self.assertRaises(subprocess.CalledProcessError): ci.determine('push', {'before': feature, 'after': dirty}, root)


if __name__ == '__main__': unittest.main()
