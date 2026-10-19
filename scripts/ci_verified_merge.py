"""Разрешает короткий main CI только для состава успешно проверенного squash PR."""
import json
from pathlib import Path
import re
import subprocess
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
CHECKS = {'Состав проверок', 'Check (ubuntu-latest)', 'Check (windows-latest)'}
FALLBACK = 'Проверенный squash PR не подтверждён: полный прогон.'


def git(*args, root=ROOT):
    return subprocess.check_output(['git', *args], cwd=root, stderr=subprocess.DEVNULL).decode().strip()


def sha(value):
    if not isinstance(value, str) or re.fullmatch(r'[0-9a-f]{40}', value) is None or set(value) == {'0'}:
        raise ValueError('Некорректная ревизия.')
    return value


def verify(event, repository, token, *, root=ROOT, get=None):
    try:
        if (event.get('ref') != 'refs/heads/main' or event.get('forced') or event.get('deleted') or
                re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', repository) is None):
            return False, FALLBACK
        before, after = sha(event['before']), sha(event['after'])
        if git('rev-list', '--parents', '-n', '1', after, root=root).split() != [after, before]:
            return False, FALLBACK
        if get is None:
            if not token:
                return False, FALLBACK

            def get(path):
                request = urllib.request.Request('https://api.github.com/repos/' + repository + path,
                    headers={'Authorization': 'Bearer ' + token, 'Accept': 'application/vnd.github+json',
                             'X-GitHub-Api-Version': '2022-11-28', 'User-Agent': 'Journal-CI'})
                with urllib.request.urlopen(request, timeout=10) as response:
                    return json.load(response)

        for associated in get('/commits/' + after + '/pulls?per_page=100'):
            number = associated['number']
            if not isinstance(number, int) or number < 1:
                continue
            pr = get('/pulls/' + str(number))
            if (not pr.get('merged') or pr.get('merge_commit_sha') != after or pr['base']['ref'] != 'main' or
                    pr['base']['sha'] != before or pr['base']['repo']['full_name'] != repository or
                    pr['head']['repo']['full_name'] != repository):
                continue
            head = sha(pr['head']['sha'])
            try:
                git('cat-file', '-e', head + '^{commit}', root=root)
            except subprocess.CalledProcessError:
                git('fetch', '--no-tags', 'origin', head, root=root)
            if git('rev-parse', head + '^{tree}', root=root) != git('rev-parse', after + '^{tree}', root=root):
                continue
            git('merge-base', '--is-ancestor', before, head, root=root)
            runs = get('/actions/workflows/modern.yml/runs?event=pull_request&head_sha=' + head + '&per_page=20')['workflow_runs']
            # Учитываем и повтор старого run: дата обновления важнее порядка ответа API.
            candidates = [run for run in runs if run.get('head_sha') == head and run.get('event') == 'pull_request']
            run = max(candidates, key=lambda run: (run.get('updated_at', ''), run.get('id', 0)), default=None)
            if (run is None or run.get('conclusion') != 'success' or run.get('status') != 'completed' or
                    run.get('path') != '.github/workflows/modern.yml'):
                continue
            run_id = run['id']
            if not isinstance(run_id, int) or run_id < 1:
                continue
            jobs = get('/actions/runs/' + str(run_id) + '/jobs?filter=latest&per_page=100')['jobs']
            if all(any(job.get('name') == name and job.get('status') == 'completed' and job.get('conclusion') == 'success'
                       for job in jobs) for name in CHECKS):
                return True, f'Подтверждён squash PR #{number}: состав совпадает, обязательные проверки успешны.'
    except (OSError, ValueError, KeyError, TypeError, subprocess.SubprocessError):
        pass
    return False, FALLBACK
