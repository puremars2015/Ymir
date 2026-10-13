#!/usr/bin/env bash
# 修復目前 Ubuntu 主機的既有正式部署；不建置、不遷移資料庫、不重建容器資料。
set -Eeuo pipefail
umask 077

fail() { printf '錯誤：%s\n' "$*" >&2; exit 1; }
[[ ${1:-} == --help ]] && {
    printf '使用：sudo bash deploy/api/activate-linux-production.sh\nCloudflare 來源維持 http://ymir-web:80，由 nginx 代理正式 API。僅適用於本機既有 Quadlet 部署。\n'
    exit 0
}
[[ $# == 0 ]] || fail '不支援參數。'
[[ $EUID == 0 ]] || fail '請使用 sudo 執行；不要將密碼傳給 AI。'
for cmd in podman systemctl python3 curl flock runuser install cp; do
    command -v "$cmd" >/dev/null || fail "缺少 $cmd"
done
exec 9>/run/lock/ymir-production-v2.lock
flock -n 9 || fail '另一個部署程序正在執行。'

quadlet=/etc/containers/systemd
api_unit=$quadlet/ymir-api.container
tunnel_unit=$quadlet/ymir-cloudflared-production.container
origin_unit=$quadlet/ymir-origin-production.container
script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
nginx_source=$script_dir/../cloudflared/ymir-production-nginx.conf
for file in "$api_unit" "$tunnel_unit" "$origin_unit" "$nginx_source" /etc/ymir/origin-nginx.conf /etc/ymir/api.env /etc/ymir/cloudflared-production.env; do
    [[ -f $file ]] || fail "缺少既有部署設定：$file"
done
owner=itadmin
owner_uid=$(id -u "$owner")
owner_home=$(getent passwd "$owner" | cut -d: -f6)
# rootlessport／conmon 等背景子程序不得繼承部署鎖，否則部署結束後仍被鎖住。
user_command() { runuser -u "$owner" -- env HOME="$owner_home" XDG_RUNTIME_DIR="/run/user/$owner_uid" "$@" 9>&-; }
for name in ymir-sql litellm_litellm-db_1 litellm_litellm_1; do
    user_command podman container exists "$name" || fail "缺少既有依賴容器 $name"
done

# 僅列印非機密欄位；不輸出完整 env / inspect。
python3 - "$api_unit" "$tunnel_unit" "$origin_unit" <<'PY'
import pathlib, subprocess, sys
def image(path):
    values = [line.split('=', 1)[1].strip() for line in pathlib.Path(path).read_text().splitlines() if line.startswith('Image=')]
    if len(values) != 1:
        raise SystemExit('Quadlet Image 設定不明確')
    subprocess.run(['podman', 'image', 'exists', values[0]], check=True)
for path in sys.argv[1:]:
    image(path)
env = {}
for line in pathlib.Path('/etc/ymir/api.env').read_text().splitlines():
    if line.strip() and not line.lstrip().startswith('#') and '=' in line:
        key, value = line.split('=', 1)
        env[key.strip()] = value.strip().strip('\"\'')
for key in ['ConnectionStrings__ymir', 'VibeMaker__LiteLlm__MasterKey', 'VibeMaker__Runtime__Remote__Token']:
    if not env.get(key) or env[key].startswith('<'):
        raise SystemExit('缺少有效設定：' + key)
if env.get('ASPNETCORE_ENVIRONMENT', 'Production') != 'Production':
    raise SystemExit('API 必須使用 Production')
if env.get('VibeMaker__Runtime__Provider', 'Remote') != 'Remote':
    raise SystemExit('API 必須使用 Remote runtime')
if env.get('ASPNETCORE_URLS', 'http://127.0.0.1:5080') != 'http://127.0.0.1:5080':
    raise SystemExit('API 必須只綁定 http://127.0.0.1:5080')
if env.get('VibeMaker__Runtime__Remote__Endpoint', 'unix:/run/ymir-runtime/runtime.sock') != 'unix:/run/ymir-runtime/runtime.sock':
    raise SystemExit('API 必須使用既有 RuntimeHost Unix socket')
if env.get('VibeMaker__Harness', 'Pi') != 'Pi':
    raise SystemExit('正式部署必須使用 Pi harness')
if env.get('Ymir__PublicEdge__Enabled', '').lower() != 'true':
    raise SystemExit('API 必須開啟 PublicEdge')
if env.get('VibeMaker__LiteLlm__BaseUrl', '').rstrip('/') != 'http://127.0.0.1:4000':
    raise SystemExit('此修復腳本預期 LiteLLM 在 http://127.0.0.1:4000')
print('既有映像及正式部署必要設定檢查通過。')
PY

nginx_image=$(sed -n 's/^Image=//p' "$origin_unit")
# 隔離測試正式映像的設定，不綁主機網路，也不啟動入口。
podman run --rm --network none --user 10001:10001 --read-only \
    --cap-drop ALL --cap-add NET_BIND_SERVICE --security-opt no-new-privileges \
    --tmpfs /tmp:rw,size=16m,mode=1777 \
    -v "$nginx_source:/etc/nginx/nginx.conf:ro" \
    --entrypoint /usr/sbin/nginx "$nginx_image" -t

printf '\nCloudflare Dashboard 的所有 Ymir Public Hostname 來源應為 http://ymir-web:80。\n正式 nginx（ymir-web）會改為代理 5080，不再代理 4200。\n若先前已改為 127.0.0.1:5080，請先改回 ymir-web:80。\n'
read -r -p '已完成請輸入 SWITCH；其他輸入取消：' confirmation
[[ $confirmation == SWITCH ]] || fail '已取消，尚未更動部署。'

backup=/var/backups/ymir/production-$(date +%Y%m%d-%H%M%S)-$$
install -d -m 700 "$backup/quadlet" "$backup/systemd"
cp -a /etc/ymir "$backup/"
cp -a "$api_unit" "$tunnel_unit" "$origin_unit" "$backup/quadlet/"
units=(ymir-api.service ymir-origin-production.service ymir-cloudflared-production.service)
for unit in "${units[@]}"; do
    systemctl is-active "$unit" >"$backup/$unit.active" || true
    systemctl is-enabled "$unit" >"$backup/$unit.enabled" || true
    if [[ -e /etc/systemd/system/$unit || -L /etc/systemd/system/$unit ]]; then
        cp -a "/etc/systemd/system/$unit" "$backup/systemd/"
    fi
done
user_command systemctl --user is-active ymir-api.service >"$backup/user-api.active" || true
user_command systemctl --user is-enabled ymir-api.service >"$backup/user-api.enabled" || true
printf '設定備份（含密鑰，僅 root 可讀）：%s\n' "$backup"

rollback() {
    local status=$?
    trap - ERR INT TERM
    set +e
    systemctl stop ymir-cloudflared-production.service ymir-origin-production.service ymir-api.service
    cp -a "$backup/quadlet/ymir-api.container" "$api_unit"
    cp -a "$backup/quadlet/ymir-cloudflared-production.container" "$tunnel_unit"
    cp -a "$backup/quadlet/ymir-origin-production.container" "$origin_unit"
    cp -a "$backup/ymir/origin-nginx.conf" /etc/ymir/origin-nginx.conf
    for unit in "${units[@]}"; do
        if [[ -e $backup/systemd/$unit || -L $backup/systemd/$unit ]]; then
            cp -a --remove-destination "$backup/systemd/$unit" "/etc/systemd/system/$unit"
        fi
    done
    systemctl daemon-reload
    for unit in "${units[@]}"; do
        if grep -qx active "$backup/$unit.active"; then systemctl start "$unit"; fi
    done
    if grep -qx enabled "$backup/user-api.enabled"; then user_command systemctl --user enable ymir-api.service; fi
    if grep -qx active "$backup/user-api.active"; then user_command systemctl --user start ymir-api.service; fi
    printf '\n部署未完成，已嘗試恢復原服務設定。備份：%s\nCloudflare Dashboard 的來源需由你手動恢復；已啟動的資料庫與 LiteLLM 保留。\n' "$backup" >&2
    (( status != 0 )) || status=1
    exit "$status"
}
trap rollback ERR INT TERM

# 確保沿用資料而非建立新 SQL / PostgreSQL；啟動不等於 readiness。
systemctl start ymir-retained-dependencies.service ymir-runtime-host.service
for name in ymir-sql litellm_litellm-db_1 litellm_litellm_1; do
    user_command podman start "$name" >/dev/null
done
ready=false
for ((i=0; i<60; i++)); do
    if curl -fsS --max-time 3 http://127.0.0.1:4000/health/liveliness >/dev/null 2>&1; then ready=true; break; fi
    sleep 2
done
if [[ $ready != true ]]; then
    printf 'LiteLLM 4000 未就緒；不切換入口、不自動重建或重啟資料容器。\n' >&2
    false
fi
curl -fsS --max-time 5 --unix-socket /run/ymir-runtime/runtime.sock http://localhost/health >/dev/null

# 停用主機 dotnet run 開發服務，避免和正式 API 搶佔 5080。
user_command systemctl --user disable --now ymir-api.service
systemctl stop ymir-origin-production.service ymir-cloudflared-production.service
python3 - "$api_unit" "$tunnel_unit" "$origin_unit" <<'PY'
import pathlib, sys
api = pathlib.Path(sys.argv[1])
text = api.read_text()
if '[Container]\n' not in text:
    raise SystemExit('API Quadlet 格式不符')
# 移除先前腳本加入的同名設定，讓重複執行不累積；env_file 已由 preflight 驗證。
keys = ['ASPNETCORE_ENVIRONMENT', 'ASPNETCORE_URLS', 'VibeMaker__Runtime__Provider', 'VibeMaker__Runtime__Remote__Endpoint']
text = '\n'.join(line for line in text.splitlines() if not any(line.startswith('Environment=' + key + '=') for key in keys)) + '\n'
text = text.replace('[Container]\n', '[Container]\nEnvironment=ASPNETCORE_ENVIRONMENT=Production\nEnvironment=ASPNETCORE_URLS=http://127.0.0.1:5080\nEnvironment=VibeMaker__Runtime__Provider=Remote\nEnvironment=VibeMaker__Runtime__Remote__Endpoint=unix:/run/ymir-runtime/runtime.sock\n', 1)
api.write_text(text)
tunnel = pathlib.Path(sys.argv[2])
text = tunnel.read_text()
text = text.replace('After=ymir-api.service', 'After=ymir-origin-production.service').replace('Requires=ymir-api.service', 'Requires=ymir-origin-production.service')
text = '\n'.join(line for line in text.splitlines() if not line.startswith('AddHost=ymir-web:')) + '\n'
text = text.replace('[Container]\n', '[Container]\nAddHost=ymir-web:127.0.0.1\n', 1)
tunnel.write_text(text)
origin = pathlib.Path(sys.argv[3])
text = origin.read_text()
text = '\n'.join('ContainerName=ymir-web' if line.startswith('ContainerName=') else line for line in text.splitlines()) + '\n'
origin.write_text(text)
PY
install -m 644 "$nginx_source" /etc/ymir/origin-nginx.conf
systemctl unmask ymir-api.service ymir-origin-production.service ymir-cloudflared-production.service
systemctl daemon-reload
systemctl restart ymir-api.service
ready=false
for ((i=0; i<60; i++)); do
    if [[ $(curl -fsS --max-time 5 http://127.0.0.1:5080/health 2>/dev/null || true) == Healthy ]]; then ready=true; break; fi
    sleep 2
done
[[ $ready == true ]]
[[ $(curl -sS --max-time 10 -o /dev/null -w '%{http_code}' http://127.0.0.1:5080/login) == 200 ]]
dev_status=$(curl -sS --max-time 10 -o /dev/null -w '%{http_code}' -X POST http://127.0.0.1:5080/api/dev/login)
[[ $dev_status == 404 || $dev_status == 401 || $dev_status == 405 ]]
systemctl restart ymir-origin-production.service
[[ $(curl -sS --retry 5 --retry-connrefused --retry-delay 1 --max-time 10 -o /dev/null -w '%{http_code}' -H 'Host: localhost' http://127.0.0.1/login) == 200 ]]
[[ $(curl -sS --max-time 5 -o /dev/null -w '%{http_code}' http://127.0.0.1/health) == 404 ]]
[[ $(curl -sS --max-time 5 -o /dev/null -w '%{http_code}' -X POST http://127.0.0.1/api/dev/login) == 404 ]]
systemctl start ymir-cloudflared-production.service
sleep 5
systemctl is-active --quiet ymir-cloudflared-production.service
trap - ERR INT TERM
printf '\n正式 API 與 ymir-web nginx 已通過本機檢查，Tunnel 已啟動。\n路徑：Tunnel -> ymir-web:80 -> API:5080。\n設定備份：%s\n請驗證公開網址、Entra 登入與 SSE；Tunnel active 不代表外網路由已驗證。\nSQL 尚未遷移到持久化 volume：請勿刪除／重建 ymir-sql。\n' "$backup"
