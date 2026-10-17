#!/usr/bin/env python3
"""Определяет проверки по полному diff; неизвестные пути требуют полного цикла."""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[1]
DOC_ROOTS = ('docs/', '.agents/skills/')
DOC_FILES = {'AGENTS.md', 'README.md', 'LICENSE', '.github/pull_request_template.md'}
EDITOR_FILES = {f'.vscode/{name}.json' for name in ('extensions', 'launch', 'settings', 'tasks')}
FULL_PREFIXES = ('.github/workflows/', '.github/actions/', '.config/', 'scripts/', 'installer/',
                 'src/Jornal.AccessHost/', 'src/Jornal.Infrastructure/Access/',
                 'src/Jornal.Desktop/Updates/', 'src/Jornal.Desktop/Startup/', 'src/Jornal.Desktop/DataSources/',
                 'src/Jornal.Core/Orders/', 'src/Jornal.Core/Pricing/',
                 'tests/Fixtures/', 'tests/Jornal.Access.Tests/', 'tests/release/')
FULL_DESKTOP = {'src/Jornal.Desktop/Program.cs', 'src/Jornal.Desktop/App.axaml.cs',
                'src/Jornal.Desktop/ViewModels/MainWindowViewModel.Updates.cs',
                'src/Jornal.Desktop/ViewModels/AppUpdatesViewModel.cs',
                'src/Jornal.Desktop/Views/AppUpdatesWindow.axaml',
                'src/Jornal.Desktop/Views/AppUpdatesWindow.axaml.cs',
                'src/Jornal.Desktop/Controls/Settings/AppUpdatesPanel.axaml',
                'src/Jornal.Desktop/Controls/Settings/AppUpdatesPanel.axaml.cs'}
APP_PREFIXES = ('src/Jornal.Desktop/', 'src/Jornal.Core/Results/', 'src/Jornal.Parsing/',
                'src/Jornal.Infrastructure/Local/', 'src/Jornal.Infrastructure/Demo/', 'tests/Jornal.Tests/')
FULL_TESTS = ('AccessHost', 'AppUpdate', 'Update', 'WindowsInstaller', 'Installer')


def classify(paths):
    code = full = False
    for path in paths:
        if path in DOC_FILES or path in EDITOR_FILES or path.endswith('.md') and path.startswith(DOC_ROOTS):
            continue
        code = True
        if (path.startswith(FULL_PREFIXES) or path in FULL_DESKTOP or
                path.endswith(('.csproj', '.slnx', '.props', '.targets', 'packages.lock.json')) or
                path.startswith('tests/Jornal.Tests/') and Path(path).name.startswith(FULL_TESTS)):
            full = True
        elif path.startswith(APP_PREFIXES) and path.endswith(('.cs', '.axaml')):
            pass
        else:
            full = True
    return {'code': code, 'packaging': full}


def git(*args, root=ROOT):
    return subprocess.run(['git', *args], cwd=root, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE).stdout


def revision(value):
    if not isinstance(value, str) or re.fullmatch(r'[0-9a-fA-F]{40}', value) is None or set(value) == {'0'}:
        raise ValueError('Нужна непустая SHA-1 ревизия Git.')
    return value


def determine(event_name, event, root=ROOT):
    if event_name == 'workflow_dispatch':
        return {'code': True, 'packaging': True, 'reason': 'Ручной полный прогон.'}
    try:
        if event_name == 'pull_request':
            request = event['pull_request']
            base, head = revision(request['base']['sha']), revision(request['head']['sha'])
            # PR: merge-base, а не изменения соседних коммитов основной ветки.
            base = git('merge-base', base, head, root=root).decode().strip()
        elif event_name == 'push':
            base, head = revision(event['before']), revision(event['after'])
        else:
            raise ValueError('Неизвестное событие.')
        names = git('diff', '--name-only', '--no-renames', '-z', base, head, root=root)
    except (KeyError, TypeError, ValueError, subprocess.CalledProcessError):
        return {'code': True, 'packaging': True, 'reason': 'Диапазон недоступен: полный прогон.'}
    # Ошибка diff/check не может превратить изменения в документацию.
    git('diff', '--check', base, head, root=root)
    paths = [os.fsdecode(path) for path in names.split(b'\0') if path]
    # Пустой/неожиданный diff тоже не отменяет проверки.
    scope = classify(paths) if paths else {'code': True, 'packaging': True}
    return {**scope, 'reason': f'Изменено файлов: {len(paths)}.'}


def gate(result, code, packaging):
    if result != 'success' or code not in ('true', 'false') or packaging not in ('true', 'false') or packaging == 'true' and code != 'true':
        raise ValueError('Состав проверок не подтверждён: обязательная проверка остановлена.')


def parse_editor_json(text):
    # VS Code допускает JSONC. Строки с URL/экранированием не являются комментариями.
    clean = []; index = 0; quoted = False
    while index < len(text):
        char = text[index]
        if quoted:
            clean.append(char)
            if char == "\\" and index + 1 < len(text):
                index += 1; clean.append(text[index])
            elif char == '"': quoted = False
        elif char == '"': quoted = True; clean.append(char)
        elif text.startswith('//', index):
            end = text.find('\n', index + 2); index = len(text) if end < 0 else end
            clean.append('\n'); continue
        elif text.startswith('/*', index):
            end = text.find('*/', index + 2)
            if end < 0: raise ValueError('Незакрытый комментарий JSONC.')
            clean.append(' '); index = end + 2; continue
        else: clean.append(char)
        index += 1
    text = ''.join(clean); clean = []; index = 0; quoted = False
    while index < len(text):
        char = text[index]
        if quoted:
            clean.append(char)
            if char == "\\" and index + 1 < len(text):
                index += 1; clean.append(text[index])
            elif char == '"': quoted = False
        elif char == '"': quoted = True; clean.append(char)
        elif char == ',' and text[index + 1:].lstrip().startswith((']', '}')): pass
        else: clean.append(char)
        index += 1
    return json.loads(''.join(clean))


def check_repository(root=ROOT):
    for name in sorted(EDITOR_FILES | {'global.json', '.config/dotnet-tools.json'}):
        text = (root / name).read_text(encoding='utf-8')
        (parse_editor_json if name in EDITOR_FILES else json.loads)(text)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('scope', 'gate', 'check'))
    args = parser.parse_args()
    if args.command == 'gate':
        gate(os.environ.get('SCOPE_RESULT'), os.environ.get('CHECK_CODE'), os.environ.get('CHECK_PACKAGING'))
    elif args.command == 'check':
        check_repository()
    else:
        event = json.loads(Path(os.environ['GITHUB_EVENT_PATH']).read_text(encoding='utf-8'))
        scope = determine(os.environ['GITHUB_EVENT_NAME'], event)
        with Path(os.environ['GITHUB_OUTPUT']).open('a', encoding='utf-8') as output:
            for name in ('code', 'packaging'):
                output.write(f'{name}={str(scope[name]).lower()}\n')
        print(json.dumps(scope, ensure_ascii=False))
        if summary := os.environ.get('GITHUB_STEP_SUMMARY'):
            mode = 'Полный цикл' if scope['packaging'] else 'Сборка + тесты + Jet' if scope['code'] else 'Быстрые проверки документации/редактора'
            with Path(summary).open('a', encoding='utf-8') as output:
                output.write(f'### Состав CI\n\n{mode}. {scope["reason"]}\n')


if __name__ == '__main__':
    main()
