#!/usr/bin/env bash
# 更新目前 Ubuntu 正式 API 與既有 rootless LiteLLM；不重建 SQL／PostgreSQL。
set -Eeuo pipefail
umask 077
fail() { printf '錯誤：%s\n' "$*" >&2; exit 1; }
if [[ ${1:-} == --help ]]; then
    printf '使用：sudo bash deploy/litellm/apply-linux-models.sh\n從 deploy/litellm/.env 載入金鑰，同步六個正式模型並重建 LiteLLM 代理。\n'
    exit 0
fi
[[ $# == 0 && $EUID == 0 ]] || fail '請使用 sudo 執行，不支援其他參數。'
directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
for tool in podman podman-compose python3 curl flock runuser; do
    command -v "$tool" >/dev/null || fail "缺少 $tool"
done
exec 9>/run/lock/ymir-production-v2.lock
flock -n 9 || fail '另一個 Ymir 部署程序正在執行。'
owner=itadmin
uid=$(id -u "$owner")
home=$(getent passwd "$owner" | cut -d: -f6)
# 背景 rootlessport／conmon 不得持有父程序的部署鎖。
as_owner() { runuser -u "$owner" -- env HOME="$home" XDG_RUNTIME_DIR="/run/user/$uid" "$@" 9>&-; }
for file in /etc/ymir/api.env "$directory/.env" "$directory/ymir-models.env.example"; do
    [[ -f $file ]] || fail "缺少 $file"
done
systemctl is-active --quiet ymir-api.service || fail '正式 API 尚未啟動。'
if as_owner podman exec litellm_litellm_1 python -c 'import os,sys; sys.exit(0 if os.environ.get("CHATGPT_TOKEN_DIR") else 1)'; then
    fail 'ChatGPT OAuth 已啟用；請使用 apply-chatgpt-linux.sh 更新，避免六模型工具移除 OAuth 路由。'
fi
database=$(python3 - "$directory/.env" <<'PY'
import pathlib, re, sys
def read(path):
    result = {}
    for line in pathlib.Path(path).read_text().splitlines():
        if '=' in line and not line.lstrip().startswith('#'):
            key, value = line.split('=', 1)
            result[key.strip()] = value.strip().strip('\"\'')
    return result
proxy = read(sys.argv[1])
api = read('/etc/ymir/api.env')
for key in ['OPENROUTER_API_KEY', 'MINIMAX_API_KEY', 'LITELLM_MASTER_KEY', 'LITELLM_SALT_KEY', 'LITELLM_DB_PASSWORD']:
    if not proxy.get(key) or proxy[key].startswith('<'):
        raise SystemExit('LiteLLM 缺少有效設定：' + key)
if proxy['LITELLM_MASTER_KEY'] != api.get('VibeMaker__LiteLlm__MasterKey'):
    raise SystemExit('API 與 LiteLLM master key 不一致，未修改設定')
match = re.search(r'(?:^|;)\s*(?:Database|Initial Catalog)\s*=\s*([A-Za-z0-9_-]+)\s*(?:;|$)', api.get('ConnectionStrings__ymir', ''), re.I)
if not match:
    raise SystemExit('無法安全解析 Ymir database 名稱')
print(match[1])
PY
)
backup=/var/backups/ymir/models-$(date +%Y%m%d-%H%M%S)-$$
install -d -m 700 "$backup"
cp -a /etc/ymir/api.env "$backup/api.env"
cp -a "$directory/.env" "$backup/litellm.env"
cp -a "$directory/config.yaml" "$backup/config.yaml"
# 保存代理與資料庫原始設定供維運回復（含密鑰，僅 root 可讀）。
as_owner podman inspect litellm_litellm_1 litellm_litellm-db_1 >"$backup/containers.json"
printf '設定備份：%s（含密鑰，請勿分享）\n' "$backup"
changed=false
recover() {
    local status=$?
    trap - ERR INT TERM
    set +e
    if [[ $changed == true ]]; then cp -a "$backup/api.env" /etc/ymir/api.env; fi
    systemctl start ymir-api.service ymir-origin-production.service ymir-cloudflared-production.service
    printf '\n更新未完成，已嘗試恢復 API 設定及入口。備份：%s\nLiteLLM 的重建不會自動撤回；請保留錯誤輸出，不要刪除資料容器。\n' "$backup" >&2
    (( status != 0 )) || status=1
    exit "$status"
}
trap recover ERR INT TERM
# 先停止外部入口，不中斷 API 正在執行的工作。
systemctl stop ymir-cloudflared-production.service ymir-origin-production.service
count=$(as_owner podman exec ymir-sql sh -c 'export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"; exec /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -d "$1" -h -1 -W -b -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM vibemaker.agent_executions WHERE status IN ('"'"'QUEUED'"'"', '"'"'RUNNING'"'"');"' sh "$database")
count=${count//[[:space:]]/}
if [[ $count != 0 ]]; then
    printf '尚有排隊或執行中的工作，取消更新並恢復入口。\n' >&2
    false
fi
systemctl stop ymir-api.service
python3 - "$directory/ymir-models.env.example" <<'PY'
import pathlib, sys
path = pathlib.Path('/etc/ymir/api.env')
prefixes = ('VibeMaker__Models__', 'VibeMaker__LiteLlm__AllowedModels__', 'VibeMaker__Pi__ModelId=')
retained = [line for line in path.read_text().splitlines() if not line.startswith(prefixes)]
settings = pathlib.Path(sys.argv[1]).read_text()
path.write_text('\n'.join(retained) + '\n\n' + settings)
PY
changed=true
# --no-deps 只重建代理，使新增的環境變數生效；保留 PostgreSQL 容器／volume。
as_owner podman-compose --env-file "$directory/.env" -f "$directory/compose.yml" up -d --no-deps --force-recreate litellm
ready=false
for ((i=0; i<60; i++)); do
    if curl -fsS --max-time 5 http://127.0.0.1:4000/health/liveliness >/dev/null 2>&1; then ready=true; break; fi
    sleep 2
done
[[ $ready == true ]]
# 只查模型目錄，不呼叫付費推論，不輸出金鑰。
as_owner podman exec litellm_litellm_1 python -c 'import os,json,urllib.request; r=urllib.request.Request("http://127.0.0.1:4000/v1/models", headers={"Authorization":"Bearer "+os.environ["LITELLM_MASTER_KEY"]}); ids={m["id"] for m in json.load(urllib.request.urlopen(r,timeout=10))["data"]}; expected={"minimax","minimax-m3","openrouter-gpt-6.1-sol","openrouter-gpt-6-luna","openrouter-sonnet-5.5","openrouter-haiku-5.5"}; assert expected <= ids; assert os.environ.get("OPENROUTER_API_KEY"); print("LiteLLM 六個模型及 OpenRouter key 載入檢查通過")'
systemctl start ymir-api.service
ready=false
for ((i=0; i<60; i++)); do
    if [[ $(curl -fsS --max-time 5 http://127.0.0.1:5080/health 2>/dev/null || true) == Healthy ]]; then ready=true; break; fi
    sleep 2
done
[[ $ready == true ]]
systemctl start ymir-origin-production.service ymir-cloudflared-production.service
[[ $(curl -fsS --max-time 10 -o /dev/null -w '%{http_code}' http://127.0.0.1/login) == 200 ]]
trap - ERR INT TERM
printf '\n六個模型已載入，MiniMax M2 已替換為 M2.7。\n請重新整理「管理 → 系統設定 → 開放模型」，勾選要開放的模型並儲存。\n既有全域與個人權限未被覆寫；尚未執行付費模型推論。\n'
