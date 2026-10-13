#!/usr/bin/env bash
# 只修復已知的 system service -> rootless Podman cgroup exec 權限問題。
set -Eeuo pipefail
umask 077
fail() { printf '錯誤：%s\n' "$*" >&2; exit 1; }
if [[ ${1:-} == --help ]]; then
    printf '使用：sudo bash deploy/runtime-host/fix-linux-exec-scope.sh\n先測試 user scope exec，通過後才替換 ContainerExecutable 並重啟 RuntimeHost。\n'
    exit 0
fi
[[ $# == 0 && $EUID == 0 ]] || fail '請使用 sudo 執行。'
exec 9>/run/lock/ymir-production-v2.lock
flock -n 9 || fail '另一個 Ymir 部署程序正在執行。'
directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
uid=$(id -u ymir)
# 呼叫端可能位於 itadmin 的私人家目錄，ymir 無權解析 cwd；不放寬家目錄權限。
as_ymir() { (cd / && runuser -u ymir -- env HOME=/home/ymir XDG_RUNTIME_DIR="/run/user/$uid" DBUS_SESSION_BUS_ADDRESS="unix:path=/run/user/$uid/bus" "$@" 9>&-); }
[[ -f /etc/ymir/runtime-host.env ]] || fail '缺少 runtime-host.env。'
systemctl is-active --quiet ymir-runtime-host.service || fail 'RuntimeHost 未啟動。'
systemctl is-active --quiet "user@$uid.service" || fail 'ymir user manager 未啟動。'
mapfile -t names < <(as_ymir podman ps --filter label=ymir.user-id --format '{{.Names}}')
[[ ${#names[@]} -gt 0 ]] || fail '沒有現存執行中的 Agent 容器可驗證，未修改設定。'
for name in "${names[@]}"; do
    [[ $name =~ ^ymir-user-[0-9a-f]{32}$ ]] || fail 'Agent 容器名稱不符預期。'
    printf '驗證 user scope 下的 Agent exec：%s\n' "$name"
    as_ymir /usr/bin/systemd-run --user --scope --quiet --expand-environment=no -- /usr/bin/podman exec "$name" sh -c 'test "$(id -u)" = 1000 && pi --version'
    # 測試 stdin 和環境變數能傳入，不使用模型金鑰、不呼叫上游。
    printf 'ymir-scope-stdin\n' | as_ymir env YMIR_SCOPE_PROBE=probe /usr/bin/systemd-run --user --scope --quiet --expand-environment=no -- /usr/bin/podman exec -i --env YMIR_SCOPE_PROBE "$name" sh -c 'read -r line; test "$line" = ymir-scope-stdin && test "$YMIR_SCOPE_PROBE" = probe'
done
database=$(python3 - <<'PY'
import pathlib,re
text=pathlib.Path('/etc/ymir/api.env').read_text()
line=next((line for line in text.splitlines() if line.startswith('ConnectionStrings__ymir=')), '')
match=re.search(r'(?:^|;)\s*(?:Database|Initial Catalog)\s*=\s*([A-Za-z0-9_-]+)\s*(?:;|$)', line.split('=',1)[-1], re.I)
if not match: raise SystemExit('無法解析 database 名稱，取消修復')
print(match[1])
PY
)
count=$(runuser -u itadmin -- env HOME=/home/itadmin XDG_RUNTIME_DIR=/run/user/$(id -u itadmin) podman exec ymir-sql sh -c 'export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"; exec /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -d "$1" -h -1 -W -b -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM vibemaker.agent_executions WHERE status IN ('"'"'QUEUED'"'"', '"'"'RUNNING'"'"');"' sh "$database" 9>&-)
count=${count//[[:space:]]/}
[[ $count == 0 ]] || fail '尚有排隊或執行中的工作，未修改設定；請待工作結束再執行。'
backup=/var/backups/ymir/runtime-scope-$(date +%Y%m%d-%H%M%S)-$$
install -d -m 700 "$backup"
cp -a /etc/ymir/runtime-host.env "$backup/runtime-host.env"
target=/usr/local/libexec/ymir-podman-scope
if [[ -e $target ]]; then cp -a "$target" "$backup/ymir-podman-scope"; fi
dropin=/etc/systemd/system/ymir-runtime-host.service.d/ymir-socket-directory.conf
if [[ -f $dropin ]]; then cp -a "$dropin" "$backup/ymir-socket-directory.conf"; fi
dropin_written=false
printf '設定備份：%s（含密鑰，請勿分享）\n' "$backup"
recover() {
    local status=$?
    trap - ERR INT TERM
    set +e
    cp -a "$backup/runtime-host.env" /etc/ymir/runtime-host.env
    if [[ -f $backup/ymir-podman-scope ]]; then cp -a "$backup/ymir-podman-scope" "$target"; fi
    if [[ $dropin_written == true ]]; then
        if [[ -f $backup/ymir-socket-directory.conf ]]; then
            cp -a "$backup/ymir-socket-directory.conf" "$dropin"
        else
            rm -- "$dropin" # 僅移除本次腳本新建的單一 drop-in。
        fi
        systemctl daemon-reload
    fi
    systemctl restart ymir-runtime-host.service
    systemctl restart ymir-api.service
    systemctl start ymir-origin-production.service ymir-cloudflared-production.service
    printf '修復未完成，已嘗試恢復 RuntimeHost 設定。\n' >&2
    (( status != 0 )) || status=1
    exit "$status"
}
trap recover ERR INT TERM
install -d -o root -g root -m 755 /usr/local/libexec
install -o root -g root -m 755 "$directory/ymir-podman-scope" "$target"
python3 - <<'PY'
from pathlib import Path
path=Path('/etc/ymir/runtime-host.env')
key='VibeMaker__Runtime__ContainerExecutable='
lines=[line for line in path.read_text().splitlines() if not line.startswith(key)]
lines.append(key+'/usr/local/libexec/ymir-podman-scope')
path.write_text('\n'.join(lines)+'\n')
PY
install -d -o root -g root -m 755 "$(dirname -- "$dropin")"
printf '[Service]\nRuntimeDirectoryPreserve=yes\n' >"$dropin"
chmod 644 "$dropin"
dropin_written=true
systemctl daemon-reload
systemctl restart ymir-runtime-host.service
# curl 對 Unix socket 連線失敗不一定遵守 --retry-connrefused；明確等待 readiness。
ready=false
for ((i=0; i<30; i++)); do
    if curl -fsS --max-time 3 --unix-socket /run/ymir-runtime/runtime.sock http://localhost/health >/dev/null 2>&1; then
        ready=true
        break
    fi
    sleep 2
done
if [[ $ready != true ]]; then
    printf 'RuntimeHost socket 在 60 秒內未就緒，回復設定。\n' >&2
    false
fi
# 從 system service 的 cgroup 實際驗證 wrapper，不僅測試登入 shell。
systemd-run --quiet --wait --pipe --collect --expand-environment=no --property=User=ymir --property=Group=ymir-runtime \
    "$target" exec "${names[0]}" sh -c 'test "$(id -u)" = 1000 && pi --version' 9>&-
# 舊 API 已掛載先前被移除的 socket 目錄，必須重新建立容器掛載。
systemctl restart ymir-api.service
ready=false
for ((i=0; i<30; i++)); do
    if [[ $(curl -fsS --max-time 5 http://127.0.0.1:5080/health 2>/dev/null || true) == Healthy ]]; then
        ready=true
        break
    fi
    sleep 2
done
[[ $ready == true ]]
systemctl start ymir-origin-production.service ymir-cloudflared-production.service
[[ $(curl -fsS --retry 5 --retry-connrefused --retry-delay 1 --max-time 10 -o /dev/null -w '%{http_code}' http://127.0.0.1/login) == 200 ]]
trap - ERR INT TERM
printf '\nRuntimeHost user scope 修復與 Agent exec／stdin／環境變數測試通過。\nAPI 健康檢查 Healthy、nginx 登入頁 200；socket 目錄保留設定已套用。\n既有 Agent 容器、workspace、資料庫保留；未呼叫付費模型。\n請回網站重新送出訊息確認對話。\n'
