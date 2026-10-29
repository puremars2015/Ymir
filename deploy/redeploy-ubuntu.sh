#!/usr/bin/env bash
# 單一入口；Python 僅使用標準函式庫，不依賴主機上的一次性 update-* 安裝包。
# 既有 Ubuntu 正式部署的安全重新建置／切換，不是清空資料的首次安裝器。
set -Eeuo pipefail
SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
exec python3 -B - "$SCRIPT_DIR/.." "$@" <<'PYTHON'
import argparse
import datetime
import fcntl
import hashlib
import json
import os
from pathlib import Path
import pwd
import re
import shlex
import shutil
import signal
import subprocess
import sys
import tarfile
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid


class DeployError(RuntimeError):
    pass


def require(value, message):
    if not value:
        raise DeployError(message)


def digest(path):
    value = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            value.update(chunk)
    return value.hexdigest()


def migration_prefix(actual, expected):
    # 不接受未知、跳號或較新 DB；不能只用集合差就略過 schema 不一致。
    require(actual == expected[:len(actual)], 'Migration history 不是來源版本的完整前綴；停止部署')
    return expected[len(actual):]


def ordered_mounts(items):
    return sorted(items, key=lambda item: item['Destination'])


def validate_archive(path):
    with tarfile.open(path) as source:
        for item in source.getmembers():
            require(not item.name.startswith('/') and '..' not in Path(item.name).parts
                    and (item.isfile() or item.isdir()), '來源 archive 含特殊檔案／symlink，需人工審查')


def atomic(path, content, mode=0o644):
    path = Path(path)
    require(not path.is_symlink(), '拒絕覆蓋 symlink 配置')
    temporary = path.with_name(path.name + '.redeploy-' + uuid.uuid4().hex)
    with temporary.open('xb') as stream:
        stream.write(content)
    os.chmod(temporary, mode)
    os.replace(temporary, path)


def arguments(values):
    parser = argparse.ArgumentParser(prog='sudo bash deploy/redeploy-ubuntu.sh',
        description='既有 Ubuntu Ymir 單一重新部署入口；保留 DB、secret、workspace、OAuth 和舊版本。',
        epilog='設定資料檔：/etc/ymir/redeploy.env（root:0600，不作 shell 執行）。支援 YMIR_SQL_CONTAINER、YMIR_SQL_PODMAN_USER（預設 itadmin，空字串為 root）、YMIR_STATE_DIR、YMIR_API_URL、YMIR_HEALTH_TIMEOUT。不安裝缺少的 Gateway／基礎服務，也不清空資料。')
    parser.add_argument('--pull', action='store_true', help='以 repo 擁有者 fetch/rebase origin/main；拒絕 dirty 或非 main')
    parser.add_argument('--ref', help='部署已存在的 commit/tag 快照，不 checkout／改寫工作目錄；可搭配 --pull')
    parser.add_argument('--test', action='store_true', help='先以一般使用者執行 Release unit tests')
    parser.add_argument('--with-agent-image', action='store_true', help='另建唯一 tag 的 Agent image，供人工審查；不換既有容器／runtime env')
    parser.add_argument('--with-services', action='store_true', help='一併重新 publish 已安裝的 MCP Gateway／SiteHost／MaintenanceHost')
    parser.add_argument('--skip-runtime-host', action='store_true', help='保留現有 RuntimeHost')
    parser.add_argument('--skip-migrate', action='store_true', help='仍檢查 DB／排空；若有待套 migration 就拒絕部署')
    parser.add_argument('--accept-additive-migrations', action='store_true', help='確認已審查待套 schema 為 additive 且舊程式相容，才允許自動套用')
    parser.add_argument('--wait-idle', type=int, default=60, metavar='SECONDS', help='入口暫停後等待空佇列的上限，預設 60 秒')
    parser.add_argument('--dry-run', action='store_true', help='只讀基線／history 並列計畫；不 fetch、不建置、不建立目錄／備份／鎖')
    parser.add_argument('--force', action='store_true', help=argparse.SUPPRESS)
    parser.add_argument('--skip-backup', action='store_true', help=argparse.SUPPRESS)
    result = parser.parse_args(values)
    require(not result.force and not result.skip_backup, '不支援 --force／--skip-backup：不能中斷使用中的工作或跳過成套備份')
    require(0 <= result.wait_idle <= 3600, '--wait-idle 必須介於 0 與 3600')
    require(not (result.dry_run and result.pull), '--dry-run 不執行 --pull；請先自行同步，再做唯讀計畫')
    return result


class Deployment:
    API = 'ymir-api.service'
    RUNTIME = 'ymir-runtime-host.service'
    ENTRANCES = ['ymir-cloudflared-production.service', 'ymir-origin-production.service']
    OPTIONAL = {
        'ymir-mcp-gateway.service': ('src/Ymir.McpGateway', 'Ymir.McpGateway', 'mcp-gateway'),
        'ymir-site-host.service': ('src/Ymir.SiteHost', 'Ymir.SiteHost', 'site-host'),
        'ymir-maintenance-host.service': ('src/Ymir.MaintenanceHost', 'Ymir.MaintenanceHost', 'maintenance-host'),
    }
    PROJECTS = {
        'platform': ('src/Platform/Ymir.Platform.Infrastructure', 'PlatformDbContext'),
        'vibemaker': ('src/Modules/VibeMaker/Ymir.VibeMaker.Infrastructure', 'VibeMakerDbContext'),
    }

    def __init__(self, repo, options):
        self.repo, self.options = Path(repo).resolve(), options
        self.quadlet = Path('/etc/containers/systemd/ymir-api.container')
        self.stage = self.build = self.backup = None
        self.locks = []
        self.paused = self.migration_started = self.migrations_complete = False
        self.previous, self.overrides, self.old_dirs = {}, {}, {}
        self.override_modes = {}
        self.services, self.pending, self.expected = {}, {}, {}
        self.phase = 'preflight'

    def command(self, args, data=None, timeout=180, user=None, cwd='/', binary=False):
        env = dict(os.environ)
        # 子程序不得持有部署鎖；不把 master key／連線字串當程序參數。
        if user:
            runuser = shutil.which('runuser')
            require(runuser, '找不到 runuser')
            account = pwd.getpwnam(user)
            env = {'HOME': account.pw_dir, 'PATH': account.pw_dir + '/.dotnet:/usr/local/bin:/usr/bin:/bin',
                   'DOTNET_CLI_TELEMETRY_OPTOUT': '1', 'DOTNET_NOLOGO': '1',
                   'XDG_RUNTIME_DIR': '/run/user/' + str(account.pw_uid)}
            if (Path(account.pw_dir) / '.dotnet/dotnet').is_file():
                env['DOTNET_ROOT'] = account.pw_dir + '/.dotnet'
            args = [runuser, '-u', user, '--', 'env', *[f'{k}={v}' for k, v in env.items()], *map(str, args)]
        try:
            result = subprocess.run(list(map(str, args)), input=data, capture_output=True,
                                    text=not binary, timeout=timeout, cwd=cwd, env=env)
        except (OSError, subprocess.TimeoutExpired) as error:
            raise DeployError('無法完成命令：' + Path(str(args[0])).name) from error
        if result.returncode:
            if self.stage:
                with (self.stage / 'private-errors.log').open('ab') as stream:
                    for output in [result.stdout, result.stderr]:
                        stream.write(output if isinstance(output, bytes) else output.encode())
            raise DeployError('命令失敗：' + Path(str(args[0])).name + '；原始診斷只保存於私有部署目錄')
        return result.stdout if binary else result.stdout.strip()

    def git(self, *args):
        return self.command(['git', '--no-optional-locks', '-c', 'core.fsmonitor=false', '-C', self.repo, *args], user=self.owner)

    def prop(self, service, name):
        return self.command(['systemctl', 'show', service, '--property=' + name, '--value'])

    def podman(self, *args, sql=False, **kwargs):
        return self.command(['podman', *args], user=self.sql_owner if sql else None, **kwargs)

    def sql(self, query, database='ymir'):
        settings = ('SET NOCOUNT ON; SET XACT_ABORT ON; SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; '
                    'SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON; '
                    'SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;\nGO\n')
        command = ('export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"; exec /opt/mssql-tools18/bin/sqlcmd '
                   '-I -C -S localhost -U sa -d ' + database + ' -b -r1 -h-1 -W -w65535')
        return self.podman('exec', '-i', self.sql_container, 'sh', '-c', command,
                           sql=True, data=settings + query + '\nGO\n', timeout=900)

    def config(self):
        # redeploy.env 只能是 root 私有的資料檔，不能 source 成任意 root shell 程式。
        allowed = {'YMIR_SQL_CONTAINER', 'YMIR_SQL_PODMAN_USER', 'YMIR_STATE_DIR', 'YMIR_API_URL', 'YMIR_HEALTH_TIMEOUT'}
        values = {}
        path = Path('/etc/ymir/redeploy.env')
        if path.exists():
            require(not path.is_symlink() and path.stat().st_uid == 0 and path.stat().st_mode & 0o077 == 0,
                    'redeploy.env 必須為 root 擁有、0600 且不是 symlink')
            for line in path.read_text().splitlines():
                if not line.strip() or line.lstrip().startswith('#'):
                    continue
                key, separator, value = line.partition('=')
                require(separator and key.strip() in allowed, 'redeploy.env 含未知設定；請依 --help／操作指南更新')
                parsed = shlex.split(value)
                require(len(parsed) == 1, 'redeploy.env 的值必須是單一字串')
                values[key.strip()] = parsed[0]
        for key in allowed:
            if key in os.environ:
                values[key] = os.environ[key]
        self.sql_container = values.get('YMIR_SQL_CONTAINER', 'ymir-sql')
        require(re.fullmatch('[a-zA-Z0-9][a-zA-Z0-9_.-]*', self.sql_container), 'SQL container 名稱不合法')
        self.sql_owner = values.get('YMIR_SQL_PODMAN_USER', 'itadmin') or None
        require(self.sql_owner in (None, 'itadmin'), '此主機工具只接受 root 或 itadmin 的 SQL store')
        self.state = Path(values.get('YMIR_STATE_DIR', '/var/lib/ymir-deploy'))
        require(self.state.is_absolute() and self.state != Path('/') and not self.state.is_symlink(), '部署紀錄路徑不合法')
        self.api_url = values.get('YMIR_API_URL', 'http://127.0.0.1:5080').rstrip('/')
        url = urllib.parse.urlsplit(self.api_url)
        require(url.scheme == 'http' and url.hostname in ('127.0.0.1', 'localhost') and not url.username
                and not url.password and not url.query and not url.fragment and not url.path, 'API URL 必須是本機 HTTP origin')
        timeout = values.get('YMIR_HEALTH_TIMEOUT', '120')
        require(timeout.isdigit() and 5 <= int(timeout) <= 600, '健康檢查秒數必須介於 5 與 600')
        self.health_timeout = int(timeout)

    def preflight(self):
        require(os.geteuid() == 0, '請使用 sudo；--help 不需 sudo')
        require(sys.platform == 'linux' and Path('/run/systemd/system').is_dir(), '只支援有 systemd 的既有 Ubuntu 主機')
        for executable in ['git', 'podman', 'tar', 'runuser', 'systemctl']:
            require(shutil.which(executable), '缺少必要工具：' + executable)
        self.config()
        self.owner = pwd.getpwuid(self.repo.stat().st_uid).pw_name
        require(self.owner != 'root', 'repo 必須由一般部署使用者擁有，不能以 root 執行 git／MSBuild')
        self.account = pwd.getpwnam(self.owner)
        require((self.repo / 'Ymir.slnx').is_file() and (self.repo / '.git').exists(), '不是 Ymir Git checkout')
        require(self.git('status', '--porcelain') == '', '工作目錄有未提交變更；保留工作，請先 commit 後再部署')
        require(self.git('branch', '--show-current') == 'main', '只接受 main；--ref 不會切換工作分支')
        self.deployer_hash = digest(self.repo / 'deploy/redeploy-ubuntu.sh')
        if self.options.pull:
            self.git('fetch', 'origin')
            self.git('rebase', 'origin/main')
            require(digest(self.repo / 'deploy/redeploy-ubuntu.sh') == self.deployer_hash,
                    '部署器本身已更新；請重新執行新版本，尚未建置／切換')
        self.sha = self.git('rev-parse', '--verify', '--end-of-options', (self.options.ref or 'HEAD') + '^{commit}')
        require(re.fullmatch('[a-f0-9]{40}', self.sha), '無法固定來源 SHA')
        self.original_quadlet = self.quadlet.read_bytes()
        self.quadlet_mode = self.quadlet.stat().st_mode & 0o777
        require(not self.quadlet.is_symlink() and self.quadlet.stat().st_uid == 0, 'API Quadlet 必須為 root 的一般檔案')
        require(len(re.findall(rb'^Image=.+$', self.original_quadlet, re.M)) == 1, 'API Quadlet Image 不明確')
        self.old_api = json.loads(self.podman('inspect', 'ymir-api'))[0]
        require(self.old_api['State']['Running'], 'API 必須先恢復至可驗證的運作基線')
        env = dict(entry.split('=', 1) for entry in self.old_api['Config']['Env'])
        require(env.get('ASPNETCORE_ENVIRONMENT') == 'Production' and env.get('VibeMaker__Runtime__Provider') == 'Remote',
                '目前 API 不是 Production／Remote 基線')
        require(re.search(r'(?:^|;)\s*(?:Database|Initial Catalog)\s*=\s*ymir\s*(?:;|$)', env.get('ConnectionStrings__ymir', ''), re.I),
                '此工具只管理 ymir 正式 DB')
        require(re.search(r'(?:^|;)\s*(?:Server|Data Source)\s*=\s*(?:tcp:)?(?:127\.0\.0\.1|localhost)(?:,1433)?\s*(?:;|$)', env.get('ConnectionStrings__ymir', ''), re.I),
                'API SQL endpoint 不是已審查的本機 1433')
        require(self.old_api['Config']['User'] in ('1654', '1654:1654') and self.old_api['HostConfig']['ReadonlyRootfs']
                and not self.old_api['HostConfig']['Privileged'], 'API 安全基線不符')
        if not self.options.skip_runtime_host:
            self.services[self.RUNTIME] = ('src/Ymir.RuntimeHost', 'Ymir.RuntimeHost', 'runtime-host')
        if self.options.with_services:
            for service, spec in self.OPTIONAL.items():
                if self.prop(service, 'LoadState') != 'not-found':
                    self.services[service] = spec
                else:
                    print('略過未安裝的服務：' + service)
        for service in [self.API, self.RUNTIME, *self.ENTRANCES, *self.services]:
            if self.prop(service, 'LoadState') != 'not-found':
                self.previous[service] = self.prop(service, 'ActiveState') == 'active'
        require(self.previous.get(self.RUNTIME) and self.previous.get(self.API), 'API／RuntimeHost 必須先恢復至運作基線')
        for service in self.services:
            directory = Path(self.prop(service, 'WorkingDirectory'))
            require(directory.is_relative_to('/opt/ymir') and directory.is_dir() and not directory.is_symlink()
                    and directory.resolve().is_relative_to('/opt/ymir'), '主機服務 WorkingDirectory 不在已審查的 /opt/ymir 內')
            self.old_dirs[service] = directory
        self.config_hashes = {}
        for path in map(Path, ['/etc/ymir/api.env', '/etc/ymir/runtime-host.env', '/etc/ymir/mcp-gateway.env',
                               '/etc/ymir/site-host.env', '/etc/ymir/maintenance-host.env']):
            if path.exists():
                require(not path.is_symlink() and path.stat().st_uid == 0 and path.stat().st_mode & 0o077 == 0,
                        '部署 secret 配置必須為 root 私有一般檔案')
                self.config_hashes[str(path)] = digest(path)
        sql_image = json.loads(self.podman('inspect', self.sql_container, sql=True))[0]
        require(sql_image['State']['Running'] and any(m['Destination'] == '/var/opt/mssql' for m in sql_image['Mounts']),
                'SQL 必須運作中且有持久化資料掛載')
        self.refresh_histories()
        self.schema_gate()
        # 無論 skip-migrate 與否都先證明能讀取工作狀態，查詢失敗絕不略過。
        self.jobs()
        print('來源：' + self.sha + '；SQL store：' + (self.sql_owner or 'root'))
        print('目標：API＋' + '、'.join(self.services) + '；保留所有既有 DB／模型／Agent 容器')
        for schema, ids in self.pending.items():
            print(schema + ' 待套 migration：' + (', '.join(ids) or '無'))
        if not self.options.dry_run:
            require(not any(self.pending.values()) or (self.options.accept_additive_migrations and not self.options.skip_migrate),
                    '有待套 migration；請先審查相容性，再加 --accept-additive-migrations；--skip-migrate 不能略過不相容版本')

    def refresh_histories(self):
        for schema, (project, context) in self.PROJECTS.items():
            files = self.git('ls-tree', '-r', '--name-only', self.sha, project + '/Persistence/Migrations').splitlines()
            expected = sorted(Path(name).stem for name in files if re.fullmatch(r'\d{14}_[A-Za-z0-9_]+\.cs', Path(name).name))
            require(expected, '來源 migration 清單為空')
            actual = self.sql(f'SELECT MigrationId FROM {schema}.__ef_migrations_history ORDER BY MigrationId;').splitlines()
            self.expected[schema] = expected
            self.pending[schema] = migration_prefix(actual, expected)

    def jobs(self):
        value = self.sql("SELECT COUNT_BIG(*) FROM vibemaker.agent_executions WHERE status IN ('QUEUED','RUNNING');")
        require(value.isdigit(), '無法確認執行中的工作；拒絕切換')
        return int(value)

    def schema_gate(self):
        tables = ['platform.users', 'platform.system_settings', 'vibemaker.projects', 'vibemaker.conversations', 'vibemaker.agent_executions']
        if any(name.endswith('_UserPluginSettings') and name not in self.pending['vibemaker'] for name in self.expected['vibemaker']):
            tables.append('vibemaker.user_plugin_settings')
        for table in tables:
            require(self.sql(f"SELECT COUNT(*) FROM sys.tables WHERE object_id=OBJECT_ID(N'{table}');") == '1',
                    'Critical schema gate 失敗：' + table)

    def acquire_locks(self):
        # 與目前主機的維護服務及一次性部署器共用，不能另換成不互斥的 fallback 鎖。
        lifecycle = Path('/run/ymir-lifecycle')
        if not lifecycle.exists():
            account = pwd.getpwnam('ymir')
            lifecycle.mkdir(mode=0o750)
            os.chown(lifecycle, account.pw_uid, account.pw_gid)
        require(not lifecycle.is_symlink() and lifecycle.resolve() == lifecycle, '生命週期鎖目錄不安全')
        for path in [Path('/run/lock/ymir-production-v2.lock'), lifecycle / 'deploy.lock']:
            require(not path.is_symlink(), '部署鎖不能是 symlink')
            descriptor = os.open(path, os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600)
            try:
                fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError:
                os.close(descriptor)
                raise DeployError('已有部署／維護作業持鎖，請稍後再試')
            self.locks.append(descriptor)

    def confirm(self):
        require(sys.stderr.isatty(), '正式部署需要互動終端確認；--dry-run 可做唯讀檢查')
        confirmation = 'DEPLOY ' + self.sha[:12]
        print('即將備份並短暫暫停網站；不清空資料。請輸入 ' + confirmation + '：', flush=True)
        with open('/dev/tty') as terminal:
            require(terminal.readline().strip() == confirmation, '已取消，尚未建置／切換')

    def prepare(self):
        self.phase = 'build'
        self.acquire_locks()
        run_id = datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '-' + self.sha[:12] + '-' + uuid.uuid4().hex[:8]
        self.run_id = run_id
        require(shutil.disk_usage('/var').free > 5 * 1024 ** 3, '可用磁碟少於 5 GiB；請先安排容量，不自動 prune')
        self.state.mkdir(parents=True, exist_ok=True, mode=0o700)
        require(self.state.stat().st_uid == 0 and self.state.stat().st_mode & 0o077 == 0, '部署紀錄目錄必須為 root 私有')
        self.stage = self.state / run_id
        self.stage.mkdir(mode=0o700)
        print('私有部署紀錄：' + str(self.stage), flush=True)
        self.backup = self.stage / 'backup'
        self.backup.mkdir(mode=0o700)
        # MSBuild／Git 不以 root 執行；建置區不含部署 env 或備份。
        self.build = Path(self.account.pw_dir) / ('.ymir-build-' + run_id)
        self.build.mkdir(mode=0o700)
        os.chown(self.build, self.account.pw_uid, self.account.pw_gid)
        archive = self.build / 'source.tar'
        self.git('archive', '--format=tar', '--output=' + str(archive), self.sha)
        validate_archive(archive)
        self.source = self.build / 'source'
        self.source.mkdir(mode=0o700)
        os.chown(self.source, self.account.pw_uid, self.account.pw_gid)
        # 原始碼 archive 不含 secret；保留 Git 的檔案模式，避免部署器的 077 umask 使 image 內檔案無法供非 root 使用者讀取。
        self.command(['tar', '--same-permissions', '-xf', archive, '-C', self.source], user=self.owner)
        dotnet = Path(self.account.pw_dir) / '.dotnet/dotnet'
        if not dotnet.is_file():
            dotnet = Path('/usr/bin/dotnet')
        require(dotnet.is_file(), '找不到部署使用者的 .NET SDK；請先安裝 .NET 10 SDK')
        self.dotnet = dotnet
        if self.options.test:
            print('執行 Release unit tests（不使用正式 DB）', flush=True)
            output = self.command([dotnet, 'test', '--project', 'tests/Ymir.UnitTests', '--configuration', 'Release'],
                                  user=self.owner, cwd=self.source, timeout=900)
            (self.stage / 'unit-tests.log').write_text(output)
        print('建置 API／Angular；建置完成前不停止正式服務。', flush=True)
        output = self.podman('build', '--label', 'org.opencontainers.image.revision=' + self.sha,
                             '-f', 'src/Ymir.Api/Containerfile', '-t', 'localhost/ymir/api:' + run_id, '.', cwd=self.source, timeout=1800)
        (self.stage / 'api-build.log').write_text(output)
        self.new_image = json.loads(self.podman('image', 'inspect', 'localhost/ymir/api:' + run_id))[0]
        require(self.new_image['Config']['User'] == '1654' and self.new_image['Labels'].get('ymir.managed') == 'true'
                and self.new_image['Labels'].get('org.opencontainers.image.revision') == self.sha, '新 API image 不符安全／來源 pin')
        self.image_id = self.new_image['Id'].removeprefix('sha256:')
        # 在停止任何正式服務前，以 image 預設非 root 身分確認整個應用程式可讀。
        self.podman('run', '--rm', '--network', 'none', '--read-only', '--entrypoint', '/bin/sh',
                    self.image_id, '-c', 'output=$(find /app ! -readable -print -quit) && test -z "$output"')
        self.command([dotnet, 'tool', 'restore', '--tool-manifest', 'dotnet-tools.json'], user=self.owner, cwd=self.source)
        for schema, (project, context) in self.PROJECTS.items():
            output = self.build / (schema + '.sql')
            self.command([dotnet, 'ef', 'migrations', 'script', '--idempotent', '--project', project,
                          '--context', context, '--output', output], user=self.owner, cwd=self.source, timeout=600)
            shutil.copyfile(output, self.stage / (schema + '.sql'))
        self.release = Path('/opt/ymir/releases') / ('redeploy-' + run_id)
        self.release.mkdir(parents=True, mode=0o755)
        os.chmod(self.release, 0o755)
        for service, (project, executable, name) in self.services.items():
            output = self.build / name
            print('Publish：' + service, flush=True)
            published = self.command([dotnet, 'publish', project, '-c', 'Release', '-r', 'linux-x64', '--self-contained',
                                      '-o', output], user=self.owner, cwd=self.source, timeout=900)
            (self.stage / (name + '-build.log')).write_text(published)
            require(not any(p.is_symlink() for p in output.rglob('*')), 'publish 產物含 symlink')
            destination = self.release / name
            shutil.copytree(output, destination)
            for path in [destination, *destination.rglob('*')]:
                os.chmod(path, 0o755 if path.is_dir() or path.name in (executable, 'createdump') or path.suffix == '.so' else 0o644)
        if self.options.with_agent_image:
            # 只產生候選 image；不偷偷修改 runtime env 或使用者容器。
            image = 'localhost/ymir/agent-runtime:redeploy-' + run_id
            agent_source = self.release / 'agent-source'
            shutil.copytree(self.source / 'runtime/agent', agent_source)
            for path in [agent_source, *agent_source.rglob('*')]:
                os.chmod(path, 0o755 if path.is_dir() or path.stat().st_mode & 0o111 else 0o644)
            self.command(['podman', 'build', '-t', image, '-f', 'Containerfile', '.'],
                         user='ymir', cwd=agent_source, timeout=1800)
            print('Agent 候選 image：' + image + '；未修改 runtime env／既有容器')
        manifest = {str(p.relative_to(self.release)): digest(p) for p in self.release.rglob('*') if p.is_file()}
        (self.stage / 'manifest.json').write_text(json.dumps({'commit': self.sha, 'api_image': self.image_id,
                                                           'publish': manifest}, indent=2))

    def stop_and_backup(self):
        self.phase = 'drain'
        print('暫停入口並排空；不強制中斷工作。', flush=True)
        # 在鎖內重讀，避免建置期間有人改部署基線或 DB history。
        require(self.quadlet.read_bytes() == self.original_quadlet, '建置期間 API Quadlet 已變更')
        for path, expected in getattr(self, 'config_hashes', {}).items():
            require(digest(path) == expected, '建置期間 secret／部署設定已變更；停止，不覆蓋其他工作')
        for service, directory in self.old_dirs.items():
            require(self.prop(service, 'WorkingDirectory') == str(directory), '建置期間主機服務版本已變更')
        self.refresh_histories()
        require(not any(self.pending.values()) or (self.options.accept_additive_migrations and not self.options.skip_migrate),
                '建置期間出現未批准 migration')
        self.paused = True
        for unit in self.ENTRANCES:
            if self.previous.get(unit):
                self.command(['systemctl', 'stop', unit])
        deadline = time.monotonic() + self.options.wait_idle
        while self.jobs():
            require(time.monotonic() < deadline, '仍有 Queued／Running 工作；不強制中斷，恢復原入口')
            time.sleep(2)
        self.command(['systemctl', 'stop', self.API])
        require(self.jobs() == 0, '停止 API 前出現新工作；拒絕切換')
        for service in reversed(list(self.services)):
            if self.previous.get(service):
                self.command(['systemctl', 'stop', service])
        self.phase = 'backup'
        print('建立並驗證 SQL／金鑰／檔案／配置的成套私有備份。', flush=True)
        sql_path = '/var/opt/mssql/backup/ymir-redeploy-' + self.run_id + '.bak'
        self.podman('exec', self.sql_container, 'sh', '-c', 'mkdir -p /var/opt/mssql/backup; test ! -e "$1"',
                    'check-backup', sql_path, sql=True)
        self.sql(f"BACKUP DATABASE ymir TO DISK=N'{sql_path}' WITH COPY_ONLY,CHECKSUM; RESTORE VERIFYONLY FROM DISK=N'{sql_path}' WITH CHECKSUM;")
        export = self.build / 'database-copyonly.bak'
        self.podman('cp', self.sql_container + ':' + sql_path, export, sql=True, timeout=900)
        shutil.copyfile(export, self.backup / 'database-copyonly.bak')
        checksum = self.podman('exec', self.sql_container, 'sha256sum', sql_path, sql=True).split()[0]
        require(digest(self.backup / 'database-copyonly.bak') == checksum, '匯出 SQL 備份 checksum 不符')
        paths = [Path('/etc/ymir'), Path('/etc/containers/systemd'), Path('/etc/systemd/system'),
                 Path('/srv/ymir'), Path('/home/ymir/.config'), Path('/var/lib/ymir-maintenance'), *self.old_dirs.values()]
        paths += [self.repo / 'deploy/litellm' / name for name in ['.env', 'config.chatgpt.runtime.yaml']]
        # 只備份已知本機 OAuth volume，token 不輸出到 log／程序參數。
        volumes = self.podman('volume', 'ls', '--format', '{{.Name}}', sql=True).splitlines()
        if 'ymir-chatgpt-auth' in volumes:
            volume = json.loads(self.podman('volume', 'inspect', 'ymir-chatgpt-auth', sql=True))[0]
            paths.append(Path(volume['Mountpoint']))
        private = self.backup / 'private-preserve.tar'
        existing = sorted({str(p).lstrip('/') for p in paths if p.exists()})
        self.command(['tar', '--acls', '--xattrs', '--numeric-owner', '-cpf', private, '-C', '/', *existing], timeout=1800)
        self.command(['tar', '--compare', '--acls', '--xattrs', '-f', private], timeout=1800)
        (self.backup / 'SHA256SUMS').write_text('\n'.join(digest(p) + '  ' + p.name for p in [private, self.backup / 'database-copyonly.bak']) + '\n')
        (self.backup / 'original-quadlet').write_bytes(self.original_quadlet)
        (self.backup / 'api-baseline-private.json').write_text(json.dumps(self.old_api, indent=2))
        (self.backup / 'service-baseline.json').write_text(json.dumps({'active': self.previous,
            'working_directories': {unit: str(path) for unit, path in self.old_dirs.items()}}, indent=2))
        self.phase = 'migration'
        print('備份通過；套用已批准的 migration（若有）。', flush=True)
        for schema in self.PROJECTS:
            if self.pending[schema]:
                self.migration_started = True
                self.sql((self.stage / (schema + '.sql')).read_text(encoding='utf-8-sig'))
        self.refresh_histories()
        require(not any(self.pending.values()), '套用後 migration history 未達目標')
        self.schema_gate()
        self.migrations_complete = True

    def switch(self):
        self.phase = 'switch'
        print('切換精確 image／release，驗證後再恢復入口。', flush=True)
        for service, (project, executable, name) in self.services.items():
            directory = Path('/etc/systemd/system') / (service + '.d')
            directory.mkdir(exist_ok=True, mode=0o755)
            target = directory / 'zzzzzzzzzzzz-redeploy.conf'
            require(not target.is_symlink(), '拒絕覆蓋 symlink drop-in')
            self.overrides[target] = target.read_bytes() if target.exists() else None
            self.override_modes[target] = target.stat().st_mode & 0o777 if target.exists() else 0o644
            (self.backup / 'override-state.json').write_text(json.dumps({str(path): {
                'original_hex': body.hex() if body is not None else None, 'mode': self.override_modes[path]}
                for path, body in self.overrides.items()}, indent=2))
            new_dir = self.release / name
            atomic(target, f'[Service]\nExecStart=\nExecStart={new_dir}/{executable}\nWorkingDirectory={new_dir}\n'.encode())
        self.api_switched = True  # atomic replace 後若立即發生 signal，也須回復。
        atomic(self.quadlet, re.sub(rb'^Image=.+$', b'Image=sha256:' + self.image_id.encode(), self.original_quadlet, flags=re.M), self.quadlet_mode)
        self.command(['systemctl', 'daemon-reload'])
        self.start_services()
        self.phase = 'verify'
        self.wait_http(self.api_url + '/health', b'Healthy')
        self.wait_http(self.api_url + '/api/auth/providers')
        self.wait_http(self.api_url + '/login')
        current = json.loads(self.podman('inspect', 'ymir-api'))[0]
        (self.stage / 'api-after-private.json').write_text(json.dumps(current, indent=2))
        require(current['Image'].removeprefix('sha256:') == self.image_id, '正在服務的 image 與 pin 不符')
        require(ordered_mounts(current['Mounts']) == ordered_mounts(self.old_api['Mounts']), 'API 掛載發生非預期變更')
        before = dict(item.split('=', 1) for item in self.old_api['Config']['Env'])
        after = dict(item.split('=', 1) for item in current['Config']['Env'])
        ignored = {'DOTNET_VERSION', 'ASPNET_VERSION'}
        require({k: v for k, v in before.items() if k not in ignored} == {k: v for k, v in after.items() if k not in ignored}, '有效部署 env 變更，拒絕成功')
        for key in ['Privileged', 'ReadonlyRootfs', 'SecurityOpt', 'CapAdd', 'CapDrop', 'NetworkMode']:
            a, b = self.old_api['HostConfig'].get(key), current['HostConfig'].get(key)
            require((sorted(a) if isinstance(a, list) else a) == (sorted(b) if isinstance(b, list) else b), 'API 安全政策發生變更')
        require(current['Config']['User'] == self.old_api['Config']['User'], 'API user 變更')
        for service, (_, _, name) in self.services.items():
            require(self.prop(service, 'WorkingDirectory') == str(self.release / name), '服務 drop-in 未生效')
            if self.previous.get(service):
                require(self.prop(service, 'ActiveState') == 'active', '主機服務未恢復')
        if self.previous.get('ymir-mcp-gateway.service') and self.options.with_services:
            self.wait_http('http://127.0.0.1:5310/health')
        if self.previous.get('ymir-site-host.service') and self.options.with_services:
            self.wait_http('http://127.0.0.1:5300/.ymir/health')
        self.verify_agent_networks()
        self.resume_entrances()
        self.verify_public()
        self.write_result(True)
        print('部署完成：' + self.sha + '；image ' + self.image_id)
        print('私有結果／成套備份：' + str(self.stage))
        print('未執行真 SSO／付費模型／Graph／冷開機／資料還原驗收；沒有刪除舊版或備份。')

    def wait_http(self, url, expected=None):
        deadline = time.monotonic() + self.health_timeout
        request = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0'})
        while True:
            try:
                with urllib.request.urlopen(request, timeout=5) as response:
                    if response.status == 200 and (expected is None or response.read(1024) == expected):
                        return
            except (OSError, urllib.error.URLError):
                pass
            require(time.monotonic() < deadline, '健康檢查逾時：' + url)
            time.sleep(2)

    def resume_entrances(self):
        for service in reversed(self.ENTRANCES):
            if self.previous.get(service):
                self.command(['systemctl', 'start', service])

    def start_services(self):
        # API 的 socket bind mount 必須先存在；維護服務也可能提供掛載目錄。
        core = [self.RUNTIME, 'ymir-maintenance-host.service', self.API]
        for service in [*core, *(unit for unit in self.services if unit not in core)]:
            if self.previous.get(service):
                self.command(['systemctl', 'start', service])

    def verify_public(self):
        # 取現有 PublicEdge hostname，不把登入／Tunnel 改成另一個來源。
        entries = dict(item.split('=', 1) for item in self.old_api['Config']['Env'])
        host = entries.get('Ymir__PublicEdge__PublicHostname')
        if host and re.fullmatch(r'[a-zA-Z0-9.-]+', host):
            self.wait_http('https://' + host + '/login')
            self.public_verified = True
        else:
            print('公開 hostname 未能由既有 env 確定；公開登入頁仍待人工驗收。')

    def verify_ci(self):
        url = 'https://api.github.com/repos/puremars2015/Ymir/actions/runs?head_sha=' + self.sha + '&per_page=30'
        try:
            with urllib.request.urlopen(url, timeout=20) as response:
                runs = json.load(response)['workflow_runs']
            ci = next(item for item in runs if item.get('name') == 'CI' and item.get('head_sha') == self.sha)
        except (OSError, ValueError, KeyError, StopIteration) as error:
            raise DeployError('無法確認此 SHA 的 GitHub CI；不切換服務，請先檢查網路與來源 CI') from error
        require(ci.get('status') == 'completed' and ci.get('conclusion') == 'success', '此來源的最新 CI 尚未通過，停止部署')
        print('來源 CI 通過：' + ci['html_url'])

    def verify_agent_networks(self):
        # 無憑證 liveliness，不發送 prompt、不接觸 Graph；探針不掛 workspace／engine socket。
        values = {}
        for line in Path('/etc/ymir/runtime-host.env').read_text().splitlines():
            key, separator, value = line.partition('=')
            if separator and key.startswith('VibeMaker__Runtime__'):
                values[key] = value.strip().strip('"\'')
        image = values.get('VibeMaker__Runtime__Image', '')
        require(image.startswith('localhost/ymir/agent-runtime:'), 'Agent 探針需要已核對的本機 runtime image')
        networks = [values.get('VibeMaker__Runtime__Network'), values.get('VibeMaker__Runtime__RestrictedNetwork')]
        require(networks[0], '此主機必須明確設定 Agent 具名網路，不能猜測模型路徑')
        entries = dict(item.split('=', 1) for item in self.old_api['Config']['Env'])
        model_url = urllib.parse.urlsplit(entries.get('VibeMaker__Pi__ModelBaseUrl', ''))
        require(model_url.scheme in ('http', 'https') and model_url.hostname and not model_url.username and not model_url.password,
                'Agent 模型 URL 不合法')
        urls = [urllib.parse.urlunsplit((model_url.scheme, model_url.netloc, '/health/liveliness', '', ''))]
        if self.previous.get('ymir-mcp-gateway.service'):
            urls.append('http://mcp-gateway:5310/health')
        code = 'Promise.all(' + json.dumps(urls) + '.map(u=>fetch(u,{signal:AbortSignal.timeout(10000)}).then(r=>{if(!r.ok)throw Error("unhealthy")}))).then(()=>console.log("ok")).catch(()=>process.exit(1))'
        for network in dict.fromkeys(item for item in networks if item):
            require(re.fullmatch('[a-zA-Z0-9_.-]+', network), 'Agent network 名稱不合法')
            result = self.command(['podman', 'run', '--rm', '--name', 'ymir-deploy-probe-' + uuid.uuid4().hex[:12],
                                   '--network', network, '--read-only', '--cap-drop=ALL', '--security-opt=no-new-privileges',
                                   '--userns=keep-id:uid=1000,gid=1000', '--user=1000:1000', '--memory=192m', '--cpus=0.5',
                                   '--pids-limit=64', '--entrypoint=node', image, '-e', code], user='ymir')
            require(result == 'ok', 'Agent 網路探針失敗')

    def write_result(self, success, manual=False):
        if self.stage and self.stage.is_dir():
            (self.stage / 'result.json').write_text(json.dumps({'success': success, 'commit': self.sha,
                'phase': self.phase, 'manual_recovery_required': manual, 'backup': str(self.backup),
                'deployer_sha256': getattr(self, 'deployer_hash', None),
                'api_image': getattr(self, 'image_id', None), 'publish_release': str(getattr(self, 'release', '')),
                'public_login_verified': getattr(self, 'public_verified', False),
                'manual_pending': ['real_login', 'pi_sse', 'existing_encrypted_data', 'reboot', 'restore']}, indent=2))

    def recover(self):
        if not self.paused:
            self.write_result(False)
            return
        if self.migration_started and not self.migrations_complete:
            self.write_result(False, manual=True)
            print('Migration 未證明完整成功；維持入口／API 關閉，請依私有備份與診斷人工處理。', file=sys.stderr)
            return
        try:
            if getattr(self, 'api_switched', False):
                self.command(['systemctl', 'stop', self.API])
                for service in reversed(list(self.services)):
                    self.command(['systemctl', 'stop', service])
                atomic(self.quadlet, self.original_quadlet, self.quadlet_mode)
            for target, original in self.overrides.items():
                if original is None:
                    target.unlink(missing_ok=True)  # 只移除本次新增的單一 drop-in。
                else:
                    atomic(target, original, self.override_modes.get(target, 0o644))
            self.command(['systemctl', 'daemon-reload'])
            self.start_services()
            self.wait_http(self.api_url + '/health', b'Healthy')
            self.resume_entrances()
            self.write_result(False)
            print('已恢復原程式／入口；保留相容 additive migration、全部備份與舊版本。', file=sys.stderr)
        except Exception:
            self.write_result(False, manual=True)
            print('自動回復未完成；請查看 root 私有 result／diagnostics，不要重跑或刪除資料。', file=sys.stderr)

    def execute(self):
        try:
            self.preflight()
            if self.options.dry_run:
                print('DRY-RUN 完成：只讀取狀態；沒有建立檔案、fetch、鎖、建置、停止服務或修改 DB。')
                return
            self.verify_ci()
            self.confirm()
            self.prepare()
            self.stop_and_backup()
            self.switch()
        except (Exception, KeyboardInterrupt) as error:
            print('部署停止：' + (str(error) if isinstance(error, DeployError) else '非預期失敗／中斷，未輸出機密'), file=sys.stderr)
            try:
                self.recover()
            except Exception:
                print('無法完整寫入回復結果；請檢查私有部署目錄，勿重跑或清除備份。', file=sys.stderr)
            raise SystemExit(1)
        finally:
            for descriptor in self.locks:
                os.close(descriptor)


def main():
    options = arguments(sys.argv[2:])
    os.umask(0o077)
    signal.signal(signal.SIGTERM, lambda *unused: (_ for _ in ()).throw(KeyboardInterrupt()))
    Deployment(sys.argv[1], options).execute()


if __name__ == '__main__':
    try:
        main()
    except DeployError as error:
        print(str(error), file=sys.stderr)
        sys.exit(2)
PYTHON
