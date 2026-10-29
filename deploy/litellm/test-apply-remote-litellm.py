#!/usr/bin/env python3
"""apply-remote-litellm.sh 的隔離回歸測試：不用 sudo、不連網路、不操作正式服務、DB 或 container。"""
import json
import os
from pathlib import Path
import stat
import subprocess
import tempfile
import types
import unittest
from unittest.mock import patch

SCRIPT = Path(__file__).with_name('apply-remote-litellm.sh')
TEMPLATE = Path(__file__).with_name('ymir-remote-models.env.example').read_text()
CODE = SCRIPT.read_text().split("<<'PYTHON'\n", 1)[1].rsplit('\nPYTHON', 1)[0]
switch = types.ModuleType('apply_remote_litellm_tests')
exec(compile(CODE, str(SCRIPT) + '<python>', 'exec'), switch.__dict__)

SECRETS = {
    'VibeMaker__LiteLlm__MasterKey': 'sk-admin-test',
    'VibeMaker__LiteLlm__CfAccessClientId': 'client.access',
    'VibeMaker__LiteLlm__CfAccessClientSecret': 'cfast_secret',
}
ADMIN = 'https://llm-admin.webpromaterial.com'
LLM = 'https://llm.webpromaterial.com/v1'

OLD_ENV = '''# 既有設定
ConnectionStrings__ymir=Server=127.0.0.1,1433;Database=ymir;User Id=sa;Password=x;TrustServerCertificate=True
VibeMaker__LiteLlm__BaseUrl=http://127.0.0.1:4000
VibeMaker__LiteLlm__MasterKey=sk-local-master
VibeMaker__LiteLlm__MonthlyBudgetUsd=20
VibeMaker__LiteLlm__KeyLifetime=1.00:00:00
VibeMaker__LiteLlm__AllowedModels__0=minimax
VibeMaker__Pi__ModelBaseUrl=http://host.containers.internal:4000/v1
VibeMaker__Pi__ModelId=minimax
VibeMaker__Pi__ModelIdleTimeout=00:05:00
VibeMaker__Models__0__Id=minimax
VibeMaker__Models__0__DisplayName=MiniMax M2.7
VibeMaker__Runtime__Provider=Remote
'''


def rendered():
    return switch.render_template(TEMPLATE, SECRETS)


class FakeRemote:
    """模擬 llm-admin（Cloudflare Access + LiteLLM）與 llm；記錄每個請求的 URL 與 header。"""

    def __init__(self, protected=True, token_ok=True, admin=True, models=None):
        self.protected, self.token_ok, self.admin = protected, token_ok, admin
        self.models = models if models is not None else switch.template_settings(rendered())[1]
        self.requests = []

    def __call__(self, url, headers):
        self.requests.append((url, dict(headers)))
        if url.startswith(ADMIN):
            has_token = headers.get('CF-Access-Client-Id') == 'client.access' and headers.get('CF-Access-Client-Secret') == 'cfast_secret'
            if self.protected and not (has_token and self.token_ok):
                return 403, b''
            if '/key/list' in url:
                return (200, b'{}') if self.admin and headers.get('Authorization') == 'Bearer sk-admin-test' else (403, b'')
            return 200, b'"I\'m alive!"'
        if url == LLM + '/models':
            return 200, json.dumps({'data': [{'id': m} for m in self.models]}).encode()
        return 404, b''


class RemoteLiteLlmSwitchTests(unittest.TestCase):
    def test_remote_thinking_levels_match_upstream_capabilities(self):
        values, ids = switch.template_settings(rendered())
        expected = {
            'claude-opus-5.5': ['low', 'medium', 'high', 'xhigh', 'max'],
            'gpt-6.1-sol': ['low', 'medium', 'high', 'xhigh', 'max'],
            'gpt-6.1-terra': ['none', 'low', 'medium', 'high', 'xhigh', 'max'],
            'gpt-6-astra': ['low', 'medium', 'high', 'xhigh', 'max'],
            'gpt-6-luna': ['none', 'low', 'medium', 'high', 'xhigh', 'max'],
            'minimax-m3.1-flash': ['low', 'medium', 'high', 'xhigh', 'max'],
        }
        for model, levels in expected.items():
            prefix = f'VibeMaker__Models__{ids.index(model)}__Thinking__'
            self.assertEqual(values[prefix + 'Parameter'], 'reasoning_effort')
            self.assertEqual([values[prefix + f'Levels__{i}'] for i in range(len(levels))], levels)
            self.assertNotIn(prefix + f'Levels__{len(levels)}', values)

    def test_secrets_use_controlling_terminal_even_with_heredoc_stdin(self):
        with patch.object(switch.os, 'open', return_value=123) as opened, \
             patch.object(switch.os, 'isatty', return_value=True), \
             patch.object(switch.os, 'close') as closed, \
             patch.object(switch.getpass, 'getpass', side_effect=list(SECRETS.values())):
            self.assertEqual(switch.ask_secrets({}, ADMIN), SECRETS)
        self.assertEqual(opened.call_args.args[0], '/dev/tty')
        closed.assert_called_once_with(123)

    def test_secrets_reject_missing_controlling_terminal(self):
        with patch.object(switch.os, 'open', side_effect=OSError('no tty')):
            with self.assertRaises(switch.SwitchError):
                switch.ask_secrets({}, ADMIN)

    def temporary(self):
        result = tempfile.TemporaryDirectory(prefix='ymir-remote-litellm-test-')
        self.addCleanup(result.cleanup)
        return Path(result.name)

    def test_bash_syntax_and_help_need_no_root(self):
        subprocess.run(['bash', '-n', str(SCRIPT)], check=True)
        result = subprocess.run(['bash', str(SCRIPT), '--help'], check=True, text=True, capture_output=True)
        self.assertIn('--check', result.stdout)

    def test_committed_template_renders_to_eight_https_models(self):
        settings, ids = switch.template_settings(rendered())
        self.assertEqual(8, len(ids))
        self.assertEqual('claude-sonnet-5.5', settings['VibeMaker__Pi__ModelId'])
        self.assertEqual(ADMIN, settings['VibeMaker__LiteLlm__BaseUrl'])
        self.assertEqual(LLM, settings['VibeMaker__Pi__ModelBaseUrl'])
        for key, value in SECRETS.items():
            self.assertEqual(value, settings[key])

    def test_invalid_secrets_are_rejected(self):
        for bad in ['', '<admin key>', ' padded ', 'with space', 'quote"d']:
            with self.subTest(bad=bad), self.assertRaises(switch.SwitchError):
                switch.render_template(TEMPLATE, {**SECRETS, 'VibeMaker__LiteLlm__MasterKey': bad})

    def test_template_cannot_smuggle_unmanaged_settings(self):
        with self.assertRaises(switch.SwitchError):
            switch.render_template(TEMPLATE + 'ConnectionStrings__ymir=evil\n', SECRETS)

    def test_template_must_be_consistent_and_https(self):
        broken = rendered().replace('VibeMaker__LiteLlm__AllowedModels__7=minimax-m3.1-flash\n', '')
        with self.assertRaises(switch.SwitchError):
            switch.template_settings(broken)
        with self.assertRaises(switch.SwitchError):
            switch.template_settings(rendered().replace('https://llm-admin', 'http://llm-admin'))

    def test_rewrite_replaces_only_managed_settings_and_is_idempotent(self):
        once = switch.rewrite_env(OLD_ENV, rendered())
        values = switch.parse_env(once)
        # 受管理的舊值全部換掉
        self.assertNotIn('sk-local-master', once)
        self.assertNotIn('minimax\n', once.replace('minimax-m3.1-flash', ''))
        self.assertNotIn('host.containers.internal', once)
        self.assertEqual(ADMIN, values['VibeMaker__LiteLlm__BaseUrl'])
        # 預算、key 有效期、其他 Pi 與 runtime 設定、DB 連線保留
        for key in ['VibeMaker__LiteLlm__MonthlyBudgetUsd', 'VibeMaker__LiteLlm__KeyLifetime', 'VibeMaker__Pi__ModelIdleTimeout',
                    'VibeMaker__Runtime__Provider', 'ConnectionStrings__ymir']:
            self.assertEqual(switch.parse_env(OLD_ENV)[key], values[key])
        self.assertTrue(once.startswith('# 既有設定\n'))
        self.assertEqual(once, switch.rewrite_env(once, rendered()))

    def test_secrets_are_reused_only_for_the_same_remote(self):
        local = switch.parse_env(OLD_ENV)
        self.assertEqual({}, switch.reusable(local, ADMIN))
        remote = switch.parse_env(switch.rewrite_env(OLD_ENV, rendered()))
        self.assertEqual(SECRETS, switch.reusable(remote, ADMIN + '/'))

    def test_database_name_is_parsed_safely(self):
        self.assertEqual('ymir', switch.database_name('Server=x;Database=ymir;User Id=sa'))
        for bad in ['', 'Server=x', "Database=ym'ir;", 'Database=ymir drop;']:
            with self.subTest(bad=bad), self.assertRaises(switch.SwitchError):
                switch.database_name(bad)

    def test_preflight_passes_and_never_sends_access_token_to_llm(self):
        remote = FakeRemote()
        settings, ids = switch.template_settings(rendered())
        switch.preflight(settings, ids, get=remote)
        self.assertEqual({}, remote.requests[0][1])  # 第一個請求刻意不帶任何憑證
        for url, headers in remote.requests:
            if not url.startswith(ADMIN):
                self.assertNotIn('CF-Access-Client-Secret', headers)
                self.assertNotIn('CF-Access-Client-Id', headers)

    def test_preflight_fails_closed(self):
        settings, ids = switch.template_settings(rendered())
        cases = {
            'Access 沒有保護': FakeRemote(protected=False),
            'token 錯': FakeRemote(token_ok=False),
            'key 沒有管理權限': FakeRemote(admin=False),
            '遠端少模型': FakeRemote(models=ids[:-1]),
        }
        for name, remote in cases.items():
            with self.subTest(name), self.assertRaises(switch.SwitchError):
                switch.preflight(settings, ids, get=remote)

    def run_main(self, healthy=True, executions=0, check=False):
        root = self.temporary()
        api_env = root / 'api.env'
        api_env.write_text(OLD_ENV)
        os.chmod(api_env, 0o640)
        calls = []

        def fake_run(command, **kwargs):
            calls.append(command)
            return subprocess.CompletedProcess(command, 0, '', '')

        patches = [
            patch.dict(switch.__dict__, {
                'API_ENV': api_env, 'BACKUP_ROOT': root / 'backups', 'LOCKS': [root / 'deploy.lock'],
                'ask_secrets': lambda current, target: dict(SECRETS),
                'preflight': lambda settings, ids: None,
                'active_executions': lambda database: executions,
                'wait_for': lambda url, accept, seconds: healthy,
                'run': fake_run,
            }),
            patch.object(switch.os, 'geteuid', return_value=0),
            patch.object(switch.shutil, 'which', return_value='/usr/bin/true'),
            patch.object(switch.subprocess, 'run', side_effect=fake_run),
        ]
        for item in patches:
            item.start()
            self.addCleanup(item.stop)
        try:
            switch.main(str(SCRIPT.parent), ['--check'] if check else [])
            exited = None
        except SystemExit as error:
            exited = error.code
        return api_env, calls, exited

    def test_check_mode_changes_nothing(self):
        api_env, calls, exited = self.run_main(check=True)
        self.assertIsNone(exited)
        self.assertEqual(OLD_ENV, api_env.read_text())
        self.assertEqual([], calls)

    def test_successful_switch_rewrites_env_and_keeps_permissions(self):
        api_env, calls, exited = self.run_main()
        self.assertIsNone(exited)
        self.assertEqual(ADMIN, switch.parse_env(api_env.read_text())['VibeMaker__LiteLlm__BaseUrl'])
        self.assertEqual(0o640, stat.S_IMODE(api_env.stat().st_mode))
        self.assertIn(['systemctl', 'stop', 'ymir-api.service'], calls)
        self.assertIn(['systemctl', 'start', 'ymir-api.service'], calls)
        backups = list((api_env.parent / 'backups').iterdir())
        self.assertEqual(OLD_ENV, (backups[0] / 'api.env').read_text())

    def test_unhealthy_api_restores_previous_env(self):
        api_env, calls, exited = self.run_main(healthy=False)
        self.assertEqual(1, exited)
        self.assertEqual(OLD_ENV, api_env.read_text())
        self.assertIn(['systemctl', 'restart', 'ymir-api.service'], calls)
        self.assertIn(['systemctl', 'start', *switch.INGRESS], calls)

    def test_running_executions_cancel_without_touching_env(self):
        api_env, calls, exited = self.run_main(executions=2)
        self.assertEqual(1, exited)
        self.assertEqual(OLD_ENV, api_env.read_text())
        self.assertNotIn(['systemctl', 'stop', 'ymir-api.service'], calls)
        self.assertIn(['systemctl', 'start', *switch.INGRESS], calls)


if __name__ == '__main__':
    unittest.main()
