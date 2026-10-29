#!/usr/bin/env bash
# 把目前 Ubuntu 正式 API 切換到外部 LiteLLM（llm.webpromaterial.com，ADR-0004 補充）：
# 模型呼叫走 llm，LiteLLM 管理走 llm-admin（Cloudflare Access）；模型清單換成 ymir-remote-models.env.example。
# 只改 /etc/ymir/api.env 的模型與 LiteLLM 連線設定並重啟 API；不動本機 LiteLLM、SQL、PostgreSQL 與其他設定。
# Python 僅使用標準函式庫；金鑰只從隱藏輸入或既有 api.env 取得，不經程序參數、不寫 log。
set -Eeuo pipefail
SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
exec python3 -B - "$SCRIPT_DIR" "$@" <<'PYTHON'
import argparse
import datetime
import fcntl
import getpass
import json
import os
from pathlib import Path
import pwd
import re
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request
import uuid


class SwitchError(RuntimeError):
    pass


def require(value, message):
    if not value:
        raise SwitchError(message)


API_ENV = Path('/etc/ymir/api.env')
BACKUP_ROOT = Path('/var/backups/ymir')
LOCKS = [Path('/run/lock/ymir-production-v2.lock'), Path('/run/ymir-lifecycle/deploy.lock')]
API_URL = 'http://127.0.0.1:5080'
SQL_OWNER = 'itadmin'
INGRESS = ['ymir-cloudflared-production.service', 'ymir-origin-production.service']
SECRET_KEYS = {
    'VibeMaker__LiteLlm__MasterKey': 'LiteLLM admin key',
    'VibeMaker__LiteLlm__CfAccessClientId': 'Cloudflare Access 用戶端識別碼',
    'VibeMaker__LiteLlm__CfAccessClientSecret': 'Cloudflare Access 用戶端密碼',
}
# 這份腳本負責的設定：舊值整批移除後換成範本內容；其他設定（預算、key 有效期、DB、runtime…）保留。
MANAGED_KEYS = {
    'VibeMaker__Pi__ModelId', 'VibeMaker__Pi__ModelBaseUrl', 'VibeMaker__Rag__BaseUrl',
    'VibeMaker__LiteLlm__BaseUrl', *SECRET_KEYS,
}
MANAGED_PREFIXES = ('VibeMaker__Models__', 'VibeMaker__LiteLlm__AllowedModels__')
MARKER = '# --- 外部 LiteLLM（apply-remote-litellm.sh 產生；來源 deploy/litellm/ymir-remote-models.env.example）---'


def parse_env(text):
    """與 systemd EnvironmentFile 相同的簡單格式：KEY=VALUE，# 開頭為註解；不作 shell 展開。"""
    values = {}
    for line in text.splitlines():
        if '=' in line and not line.lstrip().startswith('#'):
            key, value = line.split('=', 1)
            values[key.strip()] = value.strip().strip('"\'')
    return values


def is_managed(key):
    return key in MANAGED_KEYS or key.startswith(MANAGED_PREFIXES)


def is_placeholder(value):
    return not value or value.startswith('<')


def render_template(template, secrets):
    """把範本中三個金鑰的佔位值換成實際值；任何設定仍是佔位值就拒絕，避免寫出無效設定。
    只輸出 KEY=VALUE（範本的註解留在版控），重跑時才不會在 api.env 疊加註解。"""
    lines = []
    for line in template.splitlines():
        if not line.strip() or line.lstrip().startswith('#'):
            continue
        if '=' in line:
            key = line.split('=', 1)[0].strip()
            require(is_managed(key), '範本含有不屬於本腳本管理的設定：' + key)
            if key in SECRET_KEYS:
                value = secrets.get(key, '')
                require(not is_placeholder(value) and value == value.strip() and not re.search(r'[\s"\'\\]', value),
                        SECRET_KEYS[key] + ' 無效（不可為空、不可含空白或引號）')
                line = key + '=' + value
            require(not is_placeholder(line.split('=', 1)[1].strip()), '範本仍有佔位值：' + key)
        lines.append(line)
    return '\n'.join(lines).strip() + '\n'


def template_settings(rendered):
    values = parse_env(rendered)
    ids = [v for k, v in values.items() if re.fullmatch(r'VibeMaker__Models__\d+__Id', k)]
    allowed = [v for k, v in values.items() if k.startswith('VibeMaker__LiteLlm__AllowedModels__')]
    require(ids and ids == allowed and values.get('VibeMaker__Pi__ModelId') in ids, '範本的 Models、AllowedModels、Pi ModelId 不一致')
    for key in ['VibeMaker__LiteLlm__BaseUrl', 'VibeMaker__Pi__ModelBaseUrl', 'VibeMaker__Rag__BaseUrl']:
        require(values.get(key, '').startswith('https://'), key + ' 必須是 https')
    return values, ids


def rewrite_env(current, rendered):
    """移除舊的受管理設定（含先前執行留下的區塊標記），保留其他行與順序，最後附上新區塊；重複執行結果相同。"""
    kept = []
    for line in current.splitlines():
        if line.strip() == MARKER:
            continue
        if '=' in line and not line.lstrip().startswith('#') and is_managed(line.split('=', 1)[0].strip()):
            continue
        kept.append(line)
    while kept and not kept[-1].strip():
        kept.pop()
    return '\n'.join(kept) + '\n\n' + MARKER + '\n' + rendered


def database_name(connection_string):
    match = re.search(r'(?:^|;)\s*(?:Database|Initial Catalog)\s*=\s*([A-Za-z0-9_-]+)\s*(?:;|$)', connection_string or '', re.I)
    require(match, '無法安全解析 Ymir database 名稱')
    return match[1]


class NoRedirect(urllib.request.HTTPRedirectHandler):
    # 不跟隨 redirect：Access 設定錯誤時的 302 應視為失敗，也避免 service token 被帶到別的位址。
    def redirect_request(self, *args, **kwargs):
        return None


def fetch(url, headers):
    opener = urllib.request.build_opener(NoRedirect)
    request = urllib.request.Request(url, headers={'User-Agent': 'ymir-apply-remote-litellm', **headers})
    try:
        with opener.open(request, timeout=20) as response:
            return response.status, response.read(2_000_000)
    except urllib.error.HTTPError as error:
        return error.code, b''
    except (urllib.error.URLError, TimeoutError, OSError) as error:
        raise SwitchError('無法連線 ' + url.split('?')[0] + '：' + type(error).__name__) from None


def preflight(settings, model_ids, get=fetch):
    """切換前在不改任何設定的情況下確認：Access 會擋、token 能過、key 有管理權限、llm 提供範本的全部模型。"""
    admin = settings['VibeMaker__LiteLlm__BaseUrl'].rstrip('/')
    llm = settings['VibeMaker__Pi__ModelBaseUrl'].rstrip('/')
    access = {'CF-Access-Client-Id': settings['VibeMaker__LiteLlm__CfAccessClientId'],
              'CF-Access-Client-Secret': settings['VibeMaker__LiteLlm__CfAccessClientSecret']}
    key = {'Authorization': 'Bearer ' + settings['VibeMaker__LiteLlm__MasterKey']}
    status, _ = get(admin + '/health/liveliness', {})
    require(status in (302, 401, 403), f'llm-admin 不帶 Access token 回 {status}：Cloudflare Access 沒有保護管理端點，停止切換')
    status, _ = get(admin + '/health/liveliness', access)
    require(status == 200, f'llm-admin 帶 Access token 回 {status}：用戶端識別碼／密碼錯誤或 Access policy 未放行')
    status, _ = get(admin + '/key/list?page_size=1', {**access, **key})
    require(status == 200, f'llm-admin /key/list 回 {status}：admin key 錯誤或沒有管理權限（需 master key 或 proxy_admin）')
    status, body = get(llm + '/models', key)
    require(status == 200, f'llm /v1/models 回 {status}')
    try:
        available = {item['id'] for item in json.loads(body)['data']}
    except (ValueError, KeyError, TypeError):
        raise SwitchError('llm /v1/models 回應格式不正確') from None
    missing = [m for m in model_ids if m not in available]
    require(not missing, '遠端沒有這些模型：' + ', '.join(missing))


def arguments(values):
    parser = argparse.ArgumentParser(prog='sudo bash deploy/litellm/apply-remote-litellm.sh',
        description='把正式 API 切換到外部 LiteLLM（模型走 llm、管理走 llm-admin + Cloudflare Access），模型清單換成 ymir-remote-models.env.example。',
        epilog='金鑰以隱藏輸入取得；api.env 已指向同一個外部 LiteLLM 時按 Enter 沿用。失敗時自動還原 api.env 並重新啟動 API 與入口。')
    parser.add_argument('--check', action='store_true', help='只做連線與權限檢查，不改設定、不重啟服務')
    return parser.parse_args(values)


def run(command, **kwargs):
    return subprocess.run(command, check=True, text=True, **kwargs)


def as_owner(command):
    account = pwd.getpwnam(SQL_OWNER)
    return ['runuser', '-u', SQL_OWNER, '--', 'env', 'HOME=' + account.pw_dir, f'XDG_RUNTIME_DIR=/run/user/{account.pw_uid}', *command]


def active_executions(database):
    query = "SET NOCOUNT ON; SELECT COUNT(*) FROM vibemaker.agent_executions WHERE status IN ('QUEUED', 'RUNNING');"
    script = 'export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"; exec /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -d "$1" -h -1 -W -b -Q "$2"'
    output = run(as_owner(['podman', 'exec', 'ymir-sql', 'sh', '-c', script, 'sh', database, query]), capture_output=True).stdout
    require(output.strip().isdigit(), '無法讀取執行中的工作數')
    return int(output.strip())


def wait_for(url, accept, seconds):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        try:
            status, body = fetch(url, {})
            if accept(status, body):
                return True
        except SwitchError:
            pass
        time.sleep(2)
    return False


def reusable(current, target_admin):
    """既有 api.env 已經指向同一個外部 LiteLLM 時，金鑰可以沿用（重跑腳本不必重新輸入）；指向本機 LiteLLM 時舊 master key 不能用。"""
    if current.get('VibeMaker__LiteLlm__BaseUrl', '').rstrip('/') != target_admin.rstrip('/'):
        return {}
    return {key: current[key] for key in SECRET_KEYS if not is_placeholder(current.get(key, ''))}


def ask_secrets(current, target_admin):
    # Python 程式碼由 shell heredoc 傳入 stdin；應檢查 getpass 使用的控制終端，而不是 stdin。
    try:
        descriptor = os.open('/dev/tty', os.O_RDONLY | os.O_NOCTTY)
    except OSError:
        raise SwitchError('需要在終端機執行以輸入金鑰（不接受從參數或管線傳入）') from None
    try:
        require(os.isatty(descriptor), '需要在終端機執行以輸入金鑰（不接受從參數或管線傳入）')
    finally:
        os.close(descriptor)
    existing = reusable(current, target_admin)
    secrets = {}
    for key, label in SECRET_KEYS.items():
        reuse = key in existing
        value = getpass.getpass(f'{label}{"（Enter 沿用目前的值）" if reuse else ""}：').strip()
        secrets[key] = value or existing.get(key, '')
    return secrets


def main(script_dir, values):
    options = arguments(values)
    require(os.geteuid() == 0, '請使用 sudo 執行')
    for tool in ['systemctl', 'runuser', 'podman']:
        require(shutil.which(tool), '缺少 ' + tool)
    require(API_ENV.is_file() and not API_ENV.is_symlink(), f'缺少 {API_ENV} 或它是 symlink')
    template = (Path(script_dir) / 'ymir-remote-models.env.example').read_text()

    locks = []
    for path in LOCKS:
        if path.parent.is_dir():
            stream = path.open('a')
            try:
                fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError:
                raise SwitchError('另一個 Ymir 部署程序正在執行') from None
            locks.append(stream)

    original = API_ENV.read_text()
    current = parse_env(original)
    rendered = render_template(template, ask_secrets(current, parse_env(template)['VibeMaker__LiteLlm__BaseUrl']))
    settings, model_ids = template_settings(rendered)
    print('檢查外部 LiteLLM 的連線與權限…')
    preflight(settings, model_ids)
    print(f'檢查通過：Access 有保護管理端點、admin key 有管理權限、遠端提供 {len(model_ids)} 個模型。')
    if current.get('VibeMaker__Runtime__RestrictedNetwork'):
        print('注意：被關閉對外連線（internet 能力）的使用者連不到外部模型端點，執行會失敗。')
    if options.check:
        print('--check：未修改任何設定。')
        return

    require(subprocess.run(['systemctl', 'is-active', '--quiet', 'ymir-api.service']).returncode == 0, '正式 API 尚未啟動')
    database = database_name(current.get('ConnectionStrings__ymir'))
    backup = BACKUP_ROOT / ('remote-litellm-' + datetime.datetime.now().strftime('%Y%m%d-%H%M%S') + '-' + uuid.uuid4().hex[:6])
    backup.mkdir(mode=0o700, parents=True)
    shutil.copy2(API_ENV, backup / 'api.env')
    print(f'設定備份：{backup}（含密鑰，請勿分享）')

    changed = False
    try:
        # 先停止外部入口，不中斷 API 正在執行的工作；有工作就取消切換。
        run(['systemctl', 'stop', *INGRESS])
        count = active_executions(database)
        require(count == 0, f'尚有 {count} 個排隊或執行中的工作，取消切換')
        run(['systemctl', 'stop', 'ymir-api.service'])
        stat = API_ENV.stat()
        temporary = API_ENV.with_name(API_ENV.name + '.remote-litellm-' + uuid.uuid4().hex)
        with temporary.open('x') as stream:
            stream.write(rewrite_env(original, rendered))
        os.chown(temporary, stat.st_uid, stat.st_gid)
        os.chmod(temporary, stat.st_mode & 0o777)
        os.replace(temporary, API_ENV)
        changed = True
        run(['systemctl', 'start', 'ymir-api.service'])
        # /health 含 litellm 檢查：Healthy 代表 API 經 llm-admin（含 Access token）連得到外部 LiteLLM。
        require(wait_for(API_URL + '/health', lambda s, b: s == 200 and b.strip() == b'Healthy', 120), 'API 重啟後 /health 不是 Healthy')
        run(['systemctl', 'start', *INGRESS])
        require(wait_for('http://127.0.0.1/login', lambda s, b: s == 200, 60), '入口重新啟動後 /login 不是 200')
    except BaseException as error:
        print(f'\n切換未完成：{error}', file=sys.stderr)
        if changed:
            shutil.copy2(backup / 'api.env', API_ENV)
            print('已還原 api.env。', file=sys.stderr)
        subprocess.run(['systemctl', 'restart' if changed else 'start', 'ymir-api.service'])
        subprocess.run(['systemctl', 'start', *INGRESS])
        print(f'已嘗試重新啟動 API 與入口。備份：{backup}', file=sys.stderr)
        raise SystemExit(1) from None

    print(f'\n已切換到外部 LiteLLM，{len(model_ids)} 個模型；預設 {settings["VibeMaker__Pi__ModelId"]}。')
    print('下一步：到「管理 → 系統設定 → 開放模型」勾選新模型並儲存（舊的開放清單與個人授權是舊模型 id）。')
    print('本機 LiteLLM proxy 未停止；確認一切正常後可自行停用。')
    print(f'回復：sudo cp {backup}/api.env {API_ENV} && sudo systemctl restart ymir-api.service')


if __name__ == '__main__':
    try:
        main(sys.argv[1], sys.argv[2:])
    except SwitchError as error:
        print('錯誤：' + str(error), file=sys.stderr)
        raise SystemExit(1) from None
PYTHON
