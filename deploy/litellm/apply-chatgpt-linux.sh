#!/usr/bin/env bash
# Append the approved OAuth routes. Preserve other providers, database and policies.
set -Eeuo pipefail
umask 077
fail() { printf '錯誤：%s\n' "$*" >&2; exit 1; }
if [[ ${1:-} == --help ]]; then
    printf '使用：sudo bash deploy/litellm/apply-chatgpt-linux.sh\n先完成官方 OAuth，再接入已核對的三個模型；不呼叫模型推論。\n'
    exit 0
fi
[[ $# == 0 && $EUID == 0 ]] || fail '請使用 sudo 執行。'
directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
exec 9>/run/lock/ymir-production-v2.lock
flock -n 9 || fail '另一個 Ymir 部署程序正在執行。'
uid=$(id -u itadmin)
as_owner() { (cd / && runuser -u itadmin -- env HOME=/home/itadmin XDG_RUNTIME_DIR="/run/user/$uid" "$@" 9>&-); }
systemctl is-active --quiet ymir-api.service || fail '正式 API 未啟動。'
python3 - "$directory/.env" <<'PY'
from pathlib import Path
import sys
def read(path):
    values={}
    for line in Path(path).read_text().splitlines():
        if '=' in line and not line.lstrip().startswith('#'):
            key,value=line.split('=',1)
            values[key.strip()]=value.strip().strip('\"\'')
    return values
proxy=read(sys.argv[1]); api=read('/etc/ymir/api.env')
if not proxy.get('LITELLM_MASTER_KEY') or proxy['LITELLM_MASTER_KEY'] != api.get('VibeMaker__LiteLlm__MasterKey'):
    raise SystemExit('API 與 LiteLLM master key 不一致，未更動服務')
for key in ['MINIMAX_API_KEY','OPENROUTER_API_KEY','LITELLM_SALT_KEY','LITELLM_DB_PASSWORD']:
    if not proxy.get(key): raise SystemExit('缺少既有 provider 設定：'+key)
PY
as_owner podman volume exists ymir-chatgpt-auth || fail '尚未建立 OAuth volume。'
mount=$(as_owner podman volume inspect ymir-chatgpt-auth --format '{{.Mountpoint}}')
[[ -f $mount/auth.json ]] || fail '請先完成官方 OAuth 登入。'
# Headless lookup verifies login and selected IDs before touching running services.
as_owner podman run --rm --cap-drop ALL --security-opt no-new-privileges --read-only \
    --tmpfs /tmp:rw,size=64m,mode=1777 --env HOME=/tmp \
    --env CHATGPT_TOKEN_DIR=/var/lib/litellm/chatgpt \
    --volume ymir-chatgpt-auth:/var/lib/litellm/chatgpt \
    --volume "$directory/chatgpt-oauth.py:/opt/chatgpt-oauth.py:ro" \
    --entrypoint python ghcr.io/berriai/litellm:v1.103.2 /opt/chatgpt-oauth.py --catalog-only
backup=/var/backups/ymir/chatgpt-$(date +%Y%m%d-%H%M%S)-$$
install -d -m 700 "$backup"
cp -a /etc/ymir/api.env "$backup/api.env"
# Record only presence/mount locations, not OAuth tokens or full container Env.
if as_owner podman exec litellm_litellm_1 python -c 'import os,sys; sys.exit(0 if os.environ.get("CHATGPT_TOKEN_DIR") else 1)'; then
    previous_oauth=true
else
    previous_oauth=false
fi
generated=$directory/config.chatgpt.runtime.yaml
if [[ -f $generated ]]; then cp -a "$generated" "$backup/config.chatgpt.runtime.yaml"; fi
printf 'API 設定備份：%s（含密鑰，請勿分享）\n' "$backup"
restart_proxy() {
    local enabled=$1
    if [[ $enabled == true ]]; then
        as_owner podman-compose --env-file "$directory/.env" -f "$directory/compose.yml" -f "$directory/compose.chatgpt.yml" up -d --no-deps --force-recreate litellm
    else
        as_owner podman-compose --env-file "$directory/.env" -f "$directory/compose.yml" up -d --no-deps --force-recreate litellm
    fi
}
wait_proxy() {
    local i
    for ((i=0; i<60; i++)); do
        if curl -fsS --max-time 3 http://127.0.0.1:4000/health/liveliness >/dev/null 2>&1; then return 0; fi
        sleep 2
    done
    return 1
}
wait_api() {
    local i
    for ((i=0; i<60; i++)); do
        if [[ $(curl -fsS --max-time 5 http://127.0.0.1:5080/health 2>/dev/null || true) == Healthy ]]; then return 0; fi
        sleep 2
    done
    return 1
}
changed=false
recover() {
    local status=$?
    trap - ERR INT TERM
    set +e
    if [[ $changed == true ]]; then
        cp -a "$backup/api.env" /etc/ymir/api.env
        if [[ -f $backup/config.chatgpt.runtime.yaml ]]; then cp -a "$backup/config.chatgpt.runtime.yaml" "$generated"; fi
        restart_proxy "$previous_oauth"
        wait_proxy
        systemctl restart ymir-api.service
        wait_api
    fi
    systemctl start ymir-origin-production.service ymir-cloudflared-production.service
    printf 'OAuth 部署未完成，已嘗試恢復原 provider／API。token volume 保留，不會刪除帳號憑證。\n' >&2
    (( status != 0 )) || status=1
    exit "$status"
}
trap recover ERR INT TERM
database=$(python3 - <<'PY'
from pathlib import Path
import re
line=next(line for line in Path('/etc/ymir/api.env').read_text().splitlines() if line.startswith('ConnectionStrings__ymir='))
match=re.search(r'(?:^|;)\s*(?:Database|Initial Catalog)\s*=\s*([A-Za-z0-9_-]+)\s*(?:;|$)',line.split('=',1)[1],re.I)
if not match: raise SystemExit('無法解析 Ymir database 名稱')
print(match[1])
PY
)
systemctl stop ymir-cloudflared-production.service ymir-origin-production.service
count=$(as_owner podman exec ymir-sql sh -c 'export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"; exec /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -d "$1" -h -1 -W -b -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM vibemaker.agent_executions WHERE status IN ('"'"'QUEUED'"'"', '"'"'RUNNING'"'"');"' sh "$database")
count=${count//[[:space:]]/}
[[ $count == 0 ]] || { printf '尚有工作，取消部署。\n' >&2; false; }
systemctl stop ymir-api.service
changed=true
as_owner podman run --rm --network none --read-only --cap-drop ALL --security-opt no-new-privileges \
    --volume "$directory/config.yaml:/base.yaml:ro" \
    --volume "$directory/chatgpt-models.yaml:/addon.yaml:ro" \
    --volume "$directory/build-chatgpt-config.py:/builder.py:ro" \
    --entrypoint python ghcr.io/berriai/litellm:v1.103.2 /builder.py /base.yaml /addon.yaml >"$generated"
chmod 644 "$generated" # Contains env references/model aliases only, never token values.
python3 "$directory/chatgpt-ymir-models.py" /etc/ymir/api.env "$mount/models.json"
restart_proxy true
wait_proxy
as_owner podman exec litellm_litellm_1 python -c 'import json,os,urllib.request; req=urllib.request.Request("http://127.0.0.1:4000/v1/models",headers={"Authorization":"Bearer "+os.environ["LITELLM_MASTER_KEY"]}); ids={m["id"] for m in json.load(urllib.request.urlopen(req,timeout=10))["data"]}; expected={"minimax","minimax-m3","openrouter-gpt-6.1-sol","openrouter-gpt-6-luna","openrouter-sonnet-5.5","openrouter-haiku-5.5","chatgpt-gpt-6.1-sol","chatgpt-gpt-6-luna","chatgpt-gpt-6-astra"}; assert expected <= ids; print("原六模型與新增 OAuth 三模型已載入")'
systemctl start ymir-api.service
wait_api
systemctl start ymir-origin-production.service ymir-cloudflared-production.service
[[ $(curl -fsS --retry 5 --retry-connrefused --max-time 10 -o /dev/null -w '%{http_code}' http://127.0.0.1/login) == 200 ]]
trap - ERR INT TERM
printf '\nChatGPT OAuth 三模型已接入，原 provider 保留，API Healthy。\n請到管理 → 開放模型勾選 OAuth 模型並儲存。\n未呼叫推論；三模型共用訂閱，每路由初期限 6 RPM／64000 TPM，美元預算不能代表訂閱限制。\n'
