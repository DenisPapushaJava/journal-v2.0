import copy
import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import Mock, call, patch

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('ci_verified_merge', ROOT / 'scripts/ci_verified_merge.py')
verified = importlib.util.module_from_spec(spec)
spec.loader.exec_module(verified)


class VerifiedMergeTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.git('init', '-b', 'main')
        self.git('config', 'user.name', 'Тест')
        self.git('config', 'user.email', 'test@example.invalid')
        (self.root / 'file').write_text('before')
        self.git('add', '--all'); self.git('commit', '-m', 'Начало')
        self.before = self.git('rev-parse', 'HEAD')
        self.git('switch', '-c', 'feature')
        (self.root / 'file').write_text('after')
        self.git('commit', '-am', 'Изменение')
        self.head = self.git('rev-parse', 'HEAD')
        self.git('switch', 'main'); self.git('merge', '--squash', 'feature'); self.git('commit', '-m', 'Слияние')
        self.after = self.git('rev-parse', 'HEAD')
        self.event = dict(ref='refs/heads/main', before=self.before, after=self.after)
        self.pr = dict(merged=True, merge_commit_sha=self.after,
                       base=dict(ref='main', sha=self.before, repo=dict(full_name='owner/repo')),
                       head=dict(sha=self.head, repo=dict(full_name='owner/repo')))
        self.run = dict(id=123, head_sha=self.head, event='pull_request', path='.github/workflows/modern.yml',
                        status='completed', conclusion='success')
        self.jobs = [dict(name=name, status='completed', conclusion='success') for name in verified.CHECKS]
        self.calls = []

    def git(self, *args):
        return subprocess.check_output(['git', *args], cwd=self.root, stderr=subprocess.DEVNULL).decode().strip()

    def get(self, path):
        self.calls.append(path)
        if path == '/commits/' + self.after + '/pulls?per_page=100': return [dict(number=42)]
        if path == '/pulls/42': return self.pr
        if path == '/actions/workflows/modern.yml/runs?event=pull_request&head_sha=' + self.head + '&per_page=20':
            return dict(workflow_runs=[self.run])
        if path == '/actions/runs/123/jobs?filter=latest&per_page=100': return dict(jobs=self.jobs)
        raise AssertionError('Unexpected API call: ' + path)

    def verify(self, event=None, get=None):
        return verified.verify(self.event if event is None else event, 'owner/repo', '', root=self.root,
                               get=self.get if get is None else get)

    def test_exact_successful_squash_is_quick_even_after_the_source_branch_was_deleted(self):
        self.git('branch', '-D', 'feature')
        ok, reason = self.verify()
        self.assertTrue(ok); self.assertIn('#42', reason)

    def test_direct_push_unknown_range_forced_push_and_other_refs_are_not_quick(self):
        for update in [dict(ref='refs/heads/other'), dict(forced=True), dict(deleted=True),
                       dict(before='0'*40), dict(after='unknown'), dict(before='f'*40)]:
            with self.subTest(update=update):
                self.assertFalse(self.verify(self.event | update)[0])
        self.assertFalse(self.verify(get=lambda path: [])[0])

    def test_merged_pr_must_belong_to_the_same_repository_base_and_exact_commit(self):
        original = copy.deepcopy(self.pr)
        mutations = [lambda p: p.update(merged=False), lambda p: p.update(merge_commit_sha=self.head),
                     lambda p: p['base'].update(ref='other'), lambda p: p['base'].update(sha=self.head),
                     lambda p: p['head']['repo'].update(full_name='fork/repo'),
                     lambda p: p['base']['repo'].update(full_name='other/repo')]
        for mutate in mutations:
            self.pr = copy.deepcopy(original); mutate(self.pr)
            with self.subTest(pr=self.pr): self.assertFalse(self.verify()[0])

    def test_same_parent_and_merged_pr_do_not_allow_an_unverified_change_in_the_squash_tree(self):
        (self.root / 'file').write_text('different')
        self.git('commit', '-am', 'Другое содержимое', '--amend')
        self.after = self.git('rev-parse', 'HEAD'); self.event['after'] = self.after
        self.pr['merge_commit_sha'] = self.after
        self.assertFalse(self.verify()[0])

    def test_required_checks_cannot_be_missing_skipped_failed_or_cancelled(self):
        original = copy.deepcopy(self.jobs)
        for name in verified.CHECKS:
            for state in ['failure', 'skipped', 'cancelled', '', None]:
                self.jobs = copy.deepcopy(original)
                next(job for job in self.jobs if job['name'] == name)['conclusion'] = state
                with self.subTest(name=name, state=state): self.assertFalse(self.verify()[0])
            self.jobs = [job for job in original if job['name'] != name]
            with self.subTest(missing=name): self.assertFalse(self.verify()[0])

    def test_latest_run_must_be_completed_and_successful(self):
        original = copy.deepcopy(self.run)
        for mutation in [dict(status='in_progress'), dict(conclusion='failure'), dict(conclusion='cancelled'),
                         dict(path='.github/workflows/other.yml'), dict(event='push'), dict(head_sha=self.before)]:
            self.run = original | mutation
            with self.subTest(mutation=mutation): self.assertFalse(self.verify()[0])
        latest = original | dict(id=124, conclusion='failure')
        def get(path):
            if '/actions/workflows/' in path: return dict(workflow_runs=[latest, original])
            return self.get(path)
        self.assertFalse(self.verify(get=get)[0])
        rerun = original | dict(conclusion='failure', updated_at='2026-10-19T01:00:00Z')
        newer_id = original | dict(id=124, updated_at='2026-10-19T00:00:00Z')
        def get_rerun(path):
            if '/actions/workflows/' in path: return dict(workflow_runs=[newer_id, rerun])
            return self.get(path)
        self.assertFalse(self.verify(get=get_rerun)[0])

    def test_api_failure_or_malformed_response_falls_back_to_full_run(self):
        def unavailable(path): raise OSError('offline')
        self.assertFalse(self.verify(get=unavailable)[0])
        self.assertFalse(self.verify(get=lambda path: None)[0])
        self.assertFalse(verified.verify(self.event, 'owner/repo', '', root=self.root)[0])
        self.assertFalse(verified.verify(self.event, 'bad/../repo', '', root=self.root, get=self.get)[0])

    def test_post_merge_retry_waits_only_until_metadata_becomes_available(self):
        sleep = Mock()
        with patch.object(verified, 'verify', side_effect=[(False, verified.FALLBACK), (True, 'подтверждён')]) as check:
            self.assertEqual(verified.verify_after_merge(self.event, 'owner/repo', 'test-token', sleep=sleep),
                             (True, 'подтверждён'))
            self.assertEqual(2, check.call_count)
        sleep.assert_called_once_with(2)

    def test_lagging_commit_index_can_use_the_exact_recently_merged_pr(self):
        def get(path):
            if path.startswith('/commits/'): return []
            if path.startswith('/pulls?'): return [dict(number=42, merge_commit_sha=self.after)]
            return self.get(path)
        self.assertTrue(self.verify(get=get)[0])
        def wrong_commit(path):
            if path.startswith('/commits/'): return []
            if path.startswith('/pulls?'): return [dict(number=42, merge_commit_sha=self.head)]
            return self.get(path)
        self.assertFalse(self.verify(get=wrong_commit)[0])

    def test_post_merge_retry_is_bounded_and_keeps_the_full_run_fallback(self):
        sleep = Mock()
        with patch.object(verified, 'verify', return_value=(False, verified.FALLBACK)) as check:
            self.assertEqual(verified.verify_after_merge(self.event, 'owner/repo', 'test-token', sleep=sleep),
                             (False, verified.FALLBACK))
            self.assertEqual(4, check.call_count)
        self.assertEqual([call(2), call(4), call(8)], sleep.call_args_list)

    def test_available_metadata_does_not_delay_main_ci(self):
        sleep = Mock()
        with patch.object(verified, 'verify', return_value=(True, 'подтверждён')) as check:
            self.assertTrue(verified.verify_after_merge(self.event, 'owner/repo', 'test-token', sleep=sleep)[0])
            check.assert_called_once()
        sleep.assert_not_called()


if __name__ == '__main__': unittest.main()
