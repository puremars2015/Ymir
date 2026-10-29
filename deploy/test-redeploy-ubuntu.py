#!/usr/bin/env python3
"""部署器的隔離回歸測試：不用 sudo、不操作正式服務、DB 或 container。"""
import contextlib
import io
import json
import os
from pathlib import Path
import subprocess
import tarfile
import tempfile
import types
import unittest
from unittest.mock import Mock, patch

SCRIPT = Path(__file__).with_name('redeploy-ubuntu.sh')
CODE = SCRIPT.read_text().split("<<'PYTHON'\n", 1)[1].rsplit('\nPYTHON', 1)[0]
deploy = types.ModuleType('redeploy_ubuntu_tests')
exec(compile(CODE, str(SCRIPT) + '<python>', 'exec'), deploy.__dict__)


class RedeployTests(unittest.TestCase):
    def temporary(self):
        root = '/tmp/opencode' if Path('/tmp/opencode').is_dir() else None
        result = tempfile.TemporaryDirectory(prefix='ymir-redeploy-test-', dir=root)
        self.addCleanup(result.cleanup)
        return Path(result.name)

    def instance(self, *args):
        return deploy.Deployment(self.temporary(), deploy.arguments(list(args)))

    def test_bash_syntax_and_help_need_no_root_or_state(self):
        subprocess.run(['bash', '-n', str(SCRIPT)], check=True)
        result = subprocess.run(['bash', str(SCRIPT), '--help'], check=True, text=True, capture_output=True)
        self.assertIn('--dry-run', result.stdout)
        self.assertIn('--accept-additive-migrations', result.stdout)

    def test_source_modes_survive_private_umask_and_image_readability_is_checked(self):
        root = self.temporary()
        source = root / 'appsettings.json'
        source.write_text('{}')
        source.chmod(0o644)
        archive = root / 'source.tar'
        with tarfile.open(archive, 'w') as tar:
            tar.add(source, arcname='appsettings.json')
        target = root / 'out'
        target.mkdir()
        subprocess.run(['tar', '--same-permissions', '-xf', str(archive), '-C', str(target)],
                       check=True, preexec_fn=lambda: os.umask(0o077))
        self.assertEqual((target / 'appsettings.json').stat().st_mode & 0o777, 0o644)
        self.assertIn("['tar', '--same-permissions'", CODE)
        self.assertIn("'--network', 'none', '--read-only', '--entrypoint', '/bin/sh'", CODE)
        self.assertIn('find /app ! -readable', CODE)

    def test_unsafe_legacy_flags_are_rejected(self):
        for flag in ['--force', '--skip-backup']:
            with self.subTest(flag=flag), self.assertRaises(deploy.DeployError):
                deploy.arguments([flag])

    def test_dry_run_cannot_fetch_or_change_branch(self):
        with self.assertRaises(deploy.DeployError):
            deploy.arguments(['--dry-run', '--pull'])

    def test_wait_interval_is_bounded(self):
        for value in ['-1', '3601']:
            with self.assertRaises(deploy.DeployError):
                deploy.arguments(['--wait-idle', value])

    def test_migration_exact_prefix(self):
        self.assertEqual(deploy.migration_prefix(['a'], ['a', 'b']), ['b'])
        self.assertEqual(deploy.migration_prefix(['a', 'b'], ['a', 'b']), [])

    def test_unknown_newer_or_missing_migrations_fail_closed(self):
        for actual in [['b'], ['a', 'c'], ['a', 'b', 'c'], ['a', 'a']]:
            with self.subTest(actual=actual), self.assertRaises(deploy.DeployError):
                deploy.migration_prefix(actual, ['a', 'b'])

    def test_mount_order_is_not_policy_change_but_source_and_rw_are(self):
        mounts = [{'Destination': '/a', 'Source': '/one', 'RW': False}, {'Destination': '/b', 'Source': '/two', 'RW': True}]
        self.assertEqual(deploy.ordered_mounts(mounts), deploy.ordered_mounts(list(reversed(mounts))))
        for key, value in [('Source', '/wrong'), ('RW', True)]:
            changed = [dict(mounts[0]), dict(mounts[1])]
            changed[0][key] = value
            self.assertNotEqual(deploy.ordered_mounts(mounts), deploy.ordered_mounts(changed))

    def test_atomic_replace_preserves_explicit_private_mode(self):
        path = self.temporary() / 'config'
        path.write_bytes(b'old')
        deploy.atomic(path, b'new', 0o600)
        self.assertEqual(path.read_bytes(), b'new')
        self.assertEqual(path.stat().st_mode & 0o777, 0o600)

    def test_atomic_replace_rejects_symlink(self):
        root = self.temporary()
        (root / 'original').write_bytes(b'keep')
        (root / 'link').symlink_to(root / 'original')
        with self.assertRaises(deploy.DeployError):
            deploy.atomic(root / 'link', b'bad')
        self.assertEqual((root / 'original').read_bytes(), b'keep')

    def test_source_archive_rejects_escape_and_symlinks(self):
        for name, kind in [('../escape', tarfile.REGTYPE), ('/absolute', tarfile.REGTYPE), ('link', tarfile.SYMTYPE)]:
            path = self.temporary() / 'source.tar'
            with tarfile.open(path, 'w') as archive:
                item = tarfile.TarInfo(name)
                item.type = kind
                item.linkname = '/etc/passwd'
                archive.addfile(item)
            with self.assertRaises(deploy.DeployError):
                deploy.validate_archive(path)

    def test_source_archive_accepts_regular_files(self):
        path = self.temporary() / 'source.tar'
        with tarfile.open(path, 'w') as archive:
            item = tarfile.TarInfo('src/README.md')
            archive.addfile(item)
        deploy.validate_archive(path)

    def test_dry_run_does_not_prepare_confirm_lock_write_or_start_services(self):
        obj = self.instance('--dry-run')
        obj.preflight = Mock()
        obj.verify_ci = obj.confirm = obj.prepare = obj.stop_and_backup = obj.switch = Mock(side_effect=AssertionError('mutation'))
        with patch.object(Path, 'mkdir', side_effect=AssertionError('write')), contextlib.redirect_stdout(io.StringIO()):
            obj.execute()
        obj.preflight.assert_called_once()
        obj.prepare.assert_not_called()

    def test_nonroot_preflight_stops_before_reading_deployment_config(self):
        obj = self.instance()
        obj.config = Mock()
        with patch.object(deploy.os, 'geteuid', return_value=1000), self.assertRaises(deploy.DeployError):
            obj.preflight()
        obj.config.assert_not_called()

    def test_git_does_not_modify_root_global_safe_directory_or_index(self):
        obj = self.instance()
        obj.owner = 'itadmin'
        obj.command = Mock(return_value='main')
        obj.git('status', '--porcelain')
        args = obj.command.call_args.args[0]
        self.assertIn('--no-optional-locks', args)
        self.assertIn('core.fsmonitor=false', args)
        self.assertNotIn('--global', args)
        self.assertEqual(obj.command.call_args.kwargs['user'], 'itadmin')

    def test_sql_enables_all_filtered_index_options_and_uses_stdin(self):
        obj = self.instance()
        obj.sql_container = 'ymir-sql'
        obj.podman = Mock(return_value='0')
        obj.sql('SELECT 1;')
        call = obj.podman.call_args
        self.assertIn('sqlcmd -I ', call.args[-1])
        self.assertIn('SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"', call.args[-1])
        query = call.kwargs['data']
        for setting in ['QUOTED_IDENTIFIER ON', 'ANSI_NULLS ON', 'ANSI_PADDING ON', 'ANSI_WARNINGS ON',
                        'ARITHABORT ON', 'CONCAT_NULL_YIELDS_NULL ON', 'NUMERIC_ROUNDABORT OFF']:
            self.assertIn('SET ' + setting + ';', query)
        self.assertNotIn('SELECT 1;', ' '.join(call.args))

    def test_busy_query_failures_are_not_ignored_even_when_skip_migrate(self):
        obj = self.instance('--skip-migrate')
        for value in ['', 'SQL error', '1 row']:
            obj.sql = Mock(return_value=value)
            with self.assertRaises(deploy.DeployError):
                obj.jobs()
        obj.sql = Mock(return_value='2')
        self.assertEqual(obj.jobs(), 2)

    def test_busy_work_prevents_stopping_api_or_backing_up(self):
        obj = self.instance('--wait-idle', '0')
        obj.quadlet = self.temporary() / 'api.container'
        obj.original_quadlet = b'Image=old\n'
        obj.quadlet.write_bytes(obj.original_quadlet)
        obj.refresh_histories = Mock()
        obj.pending = {'platform': [], 'vibemaker': []}
        obj.previous = {name: True for name in obj.ENTRANCES}
        obj.jobs = Mock(return_value=1)
        obj.command = Mock()
        with self.assertRaises(deploy.DeployError):
            obj.stop_and_backup()
        for call in obj.command.call_args_list:
            self.assertNotIn(obj.API, call.args[0])
        self.assertTrue(obj.paused)

    def test_partial_migration_keeps_api_and_public_entry_closed(self):
        obj = self.instance()
        obj.paused = obj.migration_started = True
        obj.migrations_complete = False
        obj.command = Mock()
        obj.write_result = Mock()
        with contextlib.redirect_stderr(io.StringIO()):
            obj.recover()
        obj.command.assert_not_called()
        obj.write_result.assert_called_once_with(False, manual=True)

    def test_rollback_restores_exact_quadlet_and_dropin_without_deleting_releases(self):
        obj = self.instance()
        root = self.temporary()
        obj.paused = obj.api_switched = True
        obj.quadlet, obj.quadlet_mode = root / 'api.container', 0o600
        obj.original_quadlet = b'Image=old\n'
        obj.quadlet.write_bytes(b'Image=new\n')
        existing, added = root / 'old.conf', root / 'added.conf'
        existing.write_bytes(b'new dropin')
        added.write_bytes(b'added dropin')
        retained = root / 'retained-release'
        retained.mkdir()
        obj.overrides = {existing: b'original dropin', added: None}
        obj.previous = {obj.RUNTIME: True, obj.API: True, obj.ENTRANCES[0]: True}
        obj.services = {obj.RUNTIME: ('project', 'exe', 'runtime-host')}
        obj.command = Mock()
        obj.wait_http = Mock()
        obj.write_result = Mock()
        obj.api_url = 'http://127.0.0.1:5080'
        with contextlib.redirect_stderr(io.StringIO()):
            obj.recover()
        self.assertEqual(obj.quadlet.read_bytes(), obj.original_quadlet)
        self.assertEqual(obj.quadlet.stat().st_mode & 0o777, 0o600)
        self.assertEqual(existing.read_bytes(), b'original dropin')
        self.assertFalse(added.exists())
        self.assertTrue(retained.exists())
        obj.write_result.assert_called_once_with(False)

    def test_command_failure_redacts_console_but_preserves_private_diagnostics(self):
        obj = self.instance()
        obj.stage = self.temporary()
        result = types.SimpleNamespace(returncode=1, stdout='mock-secret', stderr='private diagnostic')
        with patch.object(deploy.subprocess, 'run', return_value=result):
            with self.assertRaises(deploy.DeployError) as caught:
                obj.command(['podman', 'inspect', 'fixture'])
        self.assertNotIn('mock-secret', str(caught.exception))
        self.assertIn('mock-secret', (obj.stage / 'private-errors.log').read_text())

    def test_ci_requires_exact_sha_and_completed_success(self):
        obj = self.instance()
        obj.sha = 'a' * 40
        for status, conclusion, sha in [('in_progress', None, obj.sha), ('completed', 'failure', obj.sha), ('completed', 'success', 'b' * 40)]:
            run = {'name': 'CI', 'head_sha': sha, 'status': status, 'conclusion': conclusion, 'html_url': 'https://example.test/ci'}
            response = io.StringIO(json.dumps({'workflow_runs': [run]}))
            with patch.object(deploy.urllib.request, 'urlopen', return_value=response), self.assertRaises(deploy.DeployError):
                obj.verify_ci()

    def test_ci_accepts_exact_source_success(self):
        obj = self.instance()
        obj.sha = 'a' * 40
        run = {'name': 'CI', 'head_sha': obj.sha, 'status': 'completed', 'conclusion': 'success', 'html_url': 'https://example.test/ci'}
        with patch.object(deploy.urllib.request, 'urlopen', return_value=io.StringIO(json.dumps({'workflow_runs': [run]}))), contextlib.redirect_stdout(io.StringIO()):
            obj.verify_ci()

    def test_health_retries_and_checks_body(self):
        obj = self.instance()
        obj.health_timeout = 5
        bad, good = Mock(), Mock()
        bad.__enter__ = Mock(return_value=types.SimpleNamespace(status=200, read=lambda count: b'Degraded'))
        good.__enter__ = Mock(return_value=types.SimpleNamespace(status=200, read=lambda count: b'Healthy'))
        bad.__exit__ = good.__exit__ = Mock(return_value=False)
        with patch.object(deploy.urllib.request, 'urlopen', side_effect=[bad, good]) as request, patch.object(deploy.time, 'sleep'):
            obj.wait_http('http://127.0.0.1/health', b'Healthy')
        self.assertEqual(request.call_count, 2)

    def test_health_timeout_is_bounded(self):
        obj = self.instance()
        obj.health_timeout = 5
        with patch.object(deploy.time, 'monotonic', side_effect=[0, 10]), patch.object(deploy.urllib.request, 'urlopen', side_effect=OSError()), self.assertRaises(deploy.DeployError):
            obj.wait_http('http://127.0.0.1/health')

    def test_config_does_not_source_shell_code_and_rejects_nonlocal_api(self):
        obj = self.instance()
        for url in ['https://example.com', 'http://127.0.0.1:5080/path', 'http://secret@localhost:5080', 'http://localhost:5080?key=x']:
            with patch.dict(os.environ, {'YMIR_API_URL': url}, clear=True), patch.object(Path, 'exists', return_value=False), self.assertRaises(deploy.DeployError):
                obj.config()

    def test_default_config_matches_current_host_without_extra_files(self):
        obj = self.instance()
        with patch.dict(os.environ, {}, clear=True), patch.object(Path, 'exists', return_value=False):
            obj.config()
        self.assertEqual(obj.sql_owner, 'itadmin')
        self.assertEqual(obj.sql_container, 'ymir-sql')
        self.assertEqual(obj.api_url, 'http://127.0.0.1:5080')

    def test_rollback_failure_is_reported_not_claimed_success(self):
        obj = self.instance()
        obj.paused = True
        obj.command = Mock(side_effect=deploy.DeployError('fixture'))
        obj.write_result = Mock()
        with contextlib.redirect_stderr(io.StringIO()):
            obj.recover()
        obj.write_result.assert_called_once_with(False, manual=True)

    def backup_fixture(self, *flags):
        obj = self.instance(*flags)
        root = self.temporary()
        obj.quadlet = root / 'api.container'
        obj.original_quadlet = b'Image=old\n'
        obj.quadlet.write_bytes(obj.original_quadlet)
        obj.run_id = 'fixture'
        obj.old_api = {'fixture': True}
        obj.sql_container = 'fixture-sql'
        obj.build, obj.stage, obj.backup = root / 'build', root / 'stage', root / 'backup'
        for path in [obj.build, obj.stage, obj.backup]:
            path.mkdir()
        obj.refresh_histories = Mock()
        obj.schema_gate = Mock()
        obj.pending = {'platform': [], 'vibemaker': []}
        obj.previous = {}
        obj.jobs = Mock(return_value=0)
        def podman(*args, **kwargs):
            if args[0] == 'cp':
                Path(args[2]).write_bytes(b'fixture-backup')
            if 'sha256sum' in args:
                return deploy.digest(obj.build / 'database-copyonly.bak') + '  fixture'
            return ''
        def command(args, **kwargs):
            if '-cpf' in args:
                Path(args[args.index('-cpf') + 1]).write_bytes(b'fixture-private-backup')
            return ''
        obj.podman = Mock(side_effect=podman)
        obj.command = Mock(side_effect=command)
        obj.sql = Mock(return_value='')
        return obj

    def test_no_pending_migrations_still_requires_exported_paired_backup(self):
        obj = self.backup_fixture()
        obj.stop_and_backup()
        obj.sql.assert_called_once()
        self.assertIn('BACKUP DATABASE', obj.sql.call_args.args[0])
        self.assertIn('RESTORE VERIFYONLY', obj.sql.call_args.args[0])
        self.assertEqual((obj.backup / 'database-copyonly.bak').read_bytes(), b'fixture-backup')
        self.assertTrue((obj.backup / 'private-preserve.tar').exists())
        self.assertIn('database-copyonly.bak', (obj.backup / 'SHA256SUMS').read_text())
        self.assertTrue(obj.migrations_complete)
        self.assertFalse(obj.migration_started)

    def test_pending_migration_cannot_be_skipped(self):
        obj = self.backup_fixture('--skip-migrate')
        obj.pending['vibemaker'] = ['pending']
        with self.assertRaises(deploy.DeployError):
            obj.stop_and_backup()
        obj.sql.assert_not_called()
        obj.command.assert_not_called()

    def test_migration_failure_occurs_after_verified_backup_and_blocks_public_recovery(self):
        obj = self.backup_fixture('--accept-additive-migrations')
        obj.pending['vibemaker'] = ['pending']
        (obj.stage / 'vibemaker.sql').write_text('CREATE TABLE fixture;')
        obj.sql.side_effect = ['', deploy.DeployError('migration fixture failure')]
        with self.assertRaises(deploy.DeployError):
            obj.stop_and_backup()
        self.assertTrue(obj.migration_started)
        self.assertFalse(obj.migrations_complete)
        self.assertTrue((obj.backup / 'SHA256SUMS').exists())
        obj.command.reset_mock()
        obj.write_result = Mock()
        with contextlib.redirect_stderr(io.StringIO()):
            obj.recover()
        obj.command.assert_not_called()
        obj.write_result.assert_called_once_with(False, manual=True)

    def test_critical_schema_gate_rejects_missing_table_even_with_matching_history(self):
        obj = self.instance()
        obj.expected = {'vibemaker': ['20261016081348_UserPluginSettings']}
        obj.pending = {'vibemaker': []}
        obj.sql = Mock(return_value='0')
        with self.assertRaises(deploy.DeployError):
            obj.schema_gate()

    def test_critical_schema_gate_includes_already_applied_plugin_table(self):
        obj = self.instance()
        obj.expected = {'vibemaker': ['20261016081348_UserPluginSettings', '20261020000000_Newer']}
        obj.pending = {'vibemaker': ['20261020000000_Newer']}
        obj.sql = Mock(return_value='1')
        obj.schema_gate()
        self.assertTrue(any('user_plugin_settings' in call.args[0] for call in obj.sql.call_args_list))

    def test_agent_probes_use_private_networks_without_credentials_or_workspace(self):
        obj = self.instance('--with-services')
        obj.old_api = {'Config': {'Env': ['VibeMaker__Pi__ModelBaseUrl=http://litellm:4000/v1']}}
        obj.previous = {'ymir-mcp-gateway.service': True}
        obj.command = Mock(return_value='ok')
        runtime = ('VibeMaker__Runtime__Image=localhost/ymir/agent-runtime:1.0.0\n'
                   'VibeMaker__Runtime__Network=ymir-agents-net\n'
                   'VibeMaker__Runtime__RestrictedNetwork=ymir-agents\n')
        with patch.object(Path, 'read_text', return_value=runtime):
            obj.verify_agent_networks()
        self.assertEqual(obj.command.call_count, 2)
        for call in obj.command.call_args_list:
            args = call.args[0]
            self.assertEqual(call.kwargs['user'], 'ymir')
            self.assertIn('--read-only', args)
            self.assertIn('--cap-drop=ALL', args)
            self.assertNotIn('--volume', args)
            self.assertNotIn('--mount', args)
            self.assertNotIn('--env', args)
            self.assertIn('http://litellm:4000/health/liveliness', args[-1])
            self.assertIn('http://mcp-gateway:5310/health', args[-1])
            self.assertNotIn('chat/completions', args[-1])

    def test_current_committed_git_archive_passes_source_validation(self):
        path = self.temporary() / 'source.tar'
        subprocess.run(['git', '-C', str(SCRIPT.parent.parent), 'archive', '--format=tar',
                        '--output=' + str(path), 'HEAD'], check=True)
        deploy.validate_archive(path)

    def test_configuration_change_during_build_stops_before_pausing(self):
        obj = self.backup_fixture()
        config = self.temporary() / 'private.env'
        config.write_text('original')
        obj.config_hashes = {str(config): deploy.digest(config)}
        config.write_text('changed-by-other-work')
        with self.assertRaises(deploy.DeployError):
            obj.stop_and_backup()
        obj.command.assert_not_called()
        self.assertFalse(obj.paused)

    def test_recovery_log_failure_still_returns_nonzero_without_traceback(self):
        obj = self.instance()
        obj.preflight = obj.verify_ci = obj.confirm = Mock()
        obj.prepare = Mock(side_effect=deploy.DeployError('fixture'))
        obj.recover = Mock(side_effect=OSError('private fixture'))
        error_output = io.StringIO()
        with contextlib.redirect_stderr(error_output), self.assertRaises(SystemExit) as caught:
            obj.execute()
        self.assertEqual(caught.exception.code, 1)
        self.assertNotIn('Traceback', error_output.getvalue())
        self.assertNotIn('private fixture', error_output.getvalue())

    def test_build_user_commands_do_not_inherit_secrets_or_run_as_root(self):
        obj = self.instance()
        account = types.SimpleNamespace(pw_dir=str(self.temporary()), pw_uid=1234)
        result = types.SimpleNamespace(returncode=0, stdout='fixture-ok', stderr='')
        with patch.object(deploy.pwd, 'getpwnam', return_value=account), patch.object(deploy.shutil, 'which', return_value='/usr/sbin/runuser'), patch.dict(os.environ, {'PRIVATE_FIXTURE_KEY': 'do-not-forward'}), patch.object(deploy.subprocess, 'run', return_value=result) as run:
            self.assertEqual(obj.command(['dotnet', '--version'], user='fixture-user'), 'fixture-ok')
        self.assertEqual(run.call_args.args[0][:4], ['/usr/sbin/runuser', '-u', 'fixture-user', '--'])
        self.assertNotIn('PRIVATE_FIXTURE_KEY', run.call_args.kwargs['env'])
        self.assertNotIn('do-not-forward', run.call_args.args[0])

    def test_socket_providing_services_start_before_api_and_gateway_after_api(self):
        obj = self.instance('--with-services')
        obj.services = {obj.RUNTIME: (), 'ymir-mcp-gateway.service': (), 'ymir-maintenance-host.service': ()}
        obj.previous = {name: True for name in [obj.RUNTIME, obj.API, 'ymir-mcp-gateway.service', 'ymir-maintenance-host.service']}
        obj.command = Mock()
        obj.start_services()
        actual = [call.args[0][-1] for call in obj.command.call_args_list]
        self.assertEqual(actual, [obj.RUNTIME, 'ymir-maintenance-host.service', obj.API, 'ymir-mcp-gateway.service'])


if __name__ == '__main__':
    unittest.main(buffer=True)
