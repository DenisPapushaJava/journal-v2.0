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
                 '.agents/skills/jornal-modernization/SKILL.md', '.github/pull_request_template.md',
                 '.vscode/launch.json', '.vscode/tasks.json', '.vscode/settings.json', '.vscode/extensions.json']
        self.assertEqual(ci.classify(paths), {'code': False, 'packaging': False})
        for path in ['docs/design/assets/journal-icon.png', 'logo.png', '.vscode/not-a-known-setting.json',
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
        build = workflow.split('\n  build:', 1)[1]
        self.assertIn('name: Check (${{ matrix.os }})', build)
        self.assertIn('needs: scope', build); self.assertIn('if: ${{ always() }}', build)
        self.assertIn('SCOPE_RESULT: ${{ needs.scope.result }}', build)
        self.assertIn('scripts/ci_scope.py gate', build)
        jet = build.split('- name: Test native Access transactions', 1)[1].split('- name:', 1)[0]
        self.assertIn("if: ${{ !cancelled() && needs.scope.outputs.code == 'true' && runner.os == 'Windows' }}", jet)
        self.assertNotIn("outputs.packaging", jet)
        self.assertIn("JOURNAL_REQUIRE_NATIVE_TESTS: '1'", jet)
        release = (ROOT / '.github/workflows/release.yml').read_text(encoding='utf-8')
        self.assertNotIn('needs.scope', release)
        native_release = release.split('- name: Test native Access transactions', 1)[1].split('- name:', 1)[0]
        self.assertIn("if: ${{ !cancelled() && runner.os == 'Windows' }}", native_release)
        for step in ['Test Windows offline installer and Access host', 'Test native Access transactions',
                     'Test Linux update and restart', 'Test Windows install and restart']:
            self.assertIn(step, release)

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
            # PR с отставшей базой не должен включать чужие изменения main.
            git('switch', '-c', 'feature', base); feature = commit('docs/feature.md', 'Описание функции\n')
            result = ci.determine('pull_request', {'pull_request': {'base': {'sha': head}, 'head': {'sha': feature}}}, root)
            self.assertFalse(result['code']); self.assertFalse(result['packaging'])
            dirty = commit('docs/feature.md', 'Пробелы   \n')
            with self.assertRaises(subprocess.CalledProcessError): ci.determine('push', {'before': feature, 'after': dirty}, root)


if __name__ == '__main__': unittest.main()
