#!/usr/bin/env bash
# Ubuntu 既有 Docker 部署：API 容器＋主機 runtime host，ADR-0008。
# 只啟動既有服務，不拉碼、建置、建容器、改設定或啟動使用者 Agent。
set -Eeuo pipefail

fail() { printf '錯誤：%s\n' "$*" >&2; exit 1; }
trap 'printf "啟動未完成，請檢查上方錯誤；已啟動的服務會保留。\n" >&2' ERR

if [[ ${1:-} == '--help' ]]; then
    cat <<'HELP'
使用方式：sudo bash ./start-ymir.sh
適用於已完成 Linux Docker API、runtime host、SQL Server、LiteLLM 及 Tunnel 部署的 Ubuntu。
容器可透過 YMIR_SQL_CONTAINER、YMIR_PG_CONTAINER、YMIR_LITELLM_CONTAINER、
YMIR_API_CONTAINER、YMIR_TUNNEL_CONTAINER 指定；未指定時使用既有名稱或唯一 Compose service。
其他選項：YMIR_API_PORT（5080）、YMIR_START_TIMEOUT（120 秒）、
YMIR_RUNTIME_SERVICE（ymir-runtime-host.service）、YMIR_RUNTIME_SOCKET（/run/ymir-runtime/runtime.sock）。
此腳本不安裝、不建置、不建立容器，不適用於 Podman Quadlet 或 Windows 主機 API。
HELP
    exit 0
fi
[[ $# -eq 0 ]] || fail '不支援這個參數；請執行 bash ./start-ymir.sh --help。'
[[ $EUID -eq 0 ]] || fail '請使用 sudo bash ./start-ymir.sh。'
for command in docker systemctl curl flock; do
    command -v "$command" >/dev/null || fail "找不到 $command，請先完成 Ubuntu 部署。"
done
[[ -d /run/systemd/system ]] || fail '此環境沒有運作中的 systemd；WSL Ubuntu 需先啟用 systemd。'

api_port=${YMIR_API_PORT:-5080}
timeout=${YMIR_START_TIMEOUT:-120}
[[ $api_port =~ ^[0-9]{1,5}$ ]] && (( 10#$api_port >= 1 && 10#$api_port <= 65535 )) || fail 'YMIR_API_PORT 必須介於 1～65535。'
[[ $timeout =~ ^[0-9]{1,3}$ ]] && (( 10#$timeout >= 1 && 10#$timeout <= 600 )) || fail 'YMIR_START_TIMEOUT 必須介於 1～600 秒。'
api_port=$((10#$api_port))
timeout=$((10#$timeout))
runtime_service=${YMIR_RUNTIME_SERVICE:-ymir-runtime-host.service}
runtime_socket=${YMIR_RUNTIME_SOCKET:-/run/ymir-runtime/runtime.sock}
[[ $runtime_service =~ ^[A-Za-z0-9][A-Za-z0-9_.@-]*\.service$ ]] || fail 'runtime host service 名稱不合法。'
[[ $runtime_socket == /* ]] || fail 'runtime host socket 必須是絕對路徑。'

exec 9>/run/lock/ymir-startup.lock
flock -n 9 || fail '另一個 Ymir 啟動程序正在執行，請等它完成。'
[[ $(systemctl show --property=LoadState --value "$runtime_service") == loaded ]] || fail "找不到 $runtime_service，請先安裝 runtime host。"

printf '檢查 Docker 引擎…\n'
if ! docker info >/dev/null 2>&1; then
    [[ $(systemctl show --property=LoadState --value docker.service) == loaded ]] || fail '找不到 Docker systemd service，請先完成 Docker Engine 安裝。'
    systemctl start docker.service
    deadline=$((SECONDS + timeout))
    until docker info >/dev/null 2>&1; do
        (( SECONDS < deadline )) || fail 'Docker 未就緒，請查看 sudo journalctl -u docker.service。'
        sleep 3
    done
fi

resolve_container() {
    local variable=$1 fallback=$2 service=$3 selected=${!1:-}
    local -a matches=()
    if [[ -n $selected ]]; then
        [[ $selected =~ ^[A-Za-z0-9][A-Za-z0-9_.-]*$ ]] || fail "$variable 的容器名稱不合法。"
        docker container inspect "$selected" >/dev/null 2>&1 || fail "找不到 $selected（$variable）。"
    elif docker container inspect "$fallback" >/dev/null 2>&1; then
        selected=$fallback
    elif [[ -n $service ]]; then
        mapfile -t matches < <(docker container ls -a --filter "label=com.docker.compose.service=$service" --format '{{.Names}}')
        [[ ${#matches[@]} -eq 1 ]] || fail "無法唯一識別 $service；請透過 $variable 指定已部署的容器。"
        selected=${matches[0]}
    else
        fail "找不到 $fallback；請先完成部署，或透過 $variable 指定已部署的容器。"
    fi
    printf '%s\n' "$selected"
}

# 先解析所有容器，再啟動服務；不假設不同 Compose project 的名稱相同。
sql=$(resolve_container YMIR_SQL_CONTAINER ymir-sql '')
postgres=$(resolve_container YMIR_PG_CONTAINER ymir-litellm-litellm-db-1 litellm-db)
litellm=$(resolve_container YMIR_LITELLM_CONTAINER ymir-litellm-litellm-1 litellm)
api=$(resolve_container YMIR_API_CONTAINER ymir-api api)
tunnel=$(resolve_container YMIR_TUNNEL_CONTAINER ymir-tunnel-cloudflared-1 cloudflared)

start_container() {
    local name=$1 state
    state=$(docker container inspect --format '{{.State.Status}}' "$name")
    case "$state" in
        running) printf '%s 已啟動。\n' "$name" ;;
        created|exited) printf '啟動 %s…\n' "$name"; docker start "$name" >/dev/null ;;
        *) fail "$name 狀態為 $state，請先處理後重試。" ;;
    esac
}

start_container "$sql"
start_container "$postgres"
start_container "$litellm"
printf '確認 runtime host…\n'
if ! systemctl is-active --quiet "$runtime_service"; then
    systemctl start "$runtime_service"
fi
deadline=$((SECONDS + timeout))
until curl --fail --silent --max-time 5 --unix-socket "$runtime_socket" http://localhost/health >/dev/null 2>&1; do
    (( SECONDS < deadline )) || fail "runtime host 未就緒，請查看 sudo journalctl -u $runtime_service。"
    sleep 3
done

start_container "$api"
printf '等待 API、資料庫與模型閘道健康檢查（最多 %s 秒）…\n' "$timeout"
deadline=$((SECONDS + timeout))
while :; do
    health=$(curl --fail --silent --max-time 5 "http://127.0.0.1:$api_port/health" 2>/dev/null || true)
    [[ $health == Healthy ]] && break
    (( SECONDS < deadline )) || fail "API 未通過健康檢查，請查看 docker logs $api；不會重複啟動 API。"
    sleep 3
done
curl --fail --silent --output /dev/null --max-time 10 "http://127.0.0.1:$api_port/login" || fail 'API 已健康，但本機登入頁無法開啟；請確認 API image 包含前端。'
start_container "$tunnel"
[[ $(docker container inspect --format '{{.State.Status}}' "$tunnel") == running ]] || fail 'Tunnel 容器未持續運行，請檢查其設定。'

printf '\nYmir 已啟動，可以關閉此終端。\n'
printf '本機網頁：http://localhost:%s\n' "$api_port"
printf '正式登入請使用已設定的 HTTPS 網址；外網仍需要正確的 Tunnel、DNS 與網路。\n'
