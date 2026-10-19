#!/usr/bin/env bash
# Ubuntu 正式主機的「一鍵增量重新部署」：修改程式後在 repo 執行即可。
# 拓樸：docs/guides/ubuntu-install.md（rootful Podman API Quadlet + 主機 RuntimeHost）。
# 前提：已依該指南完成首次安裝；本 script 不建帳號、不建資料庫、不覆蓋 /etc/ymir 內任何 secret。
#
#   sudo bash deploy/redeploy-ubuntu.sh [選項]
#
# 流程：同步程式 → 建置 API image（含 Angular）→ publish RuntimeHost →
#       取部署鎖 → 檢查執行中工作 → 有待套 migration 才備份並套用 →
#       切換 API image / RuntimeHost → health 驗證 → 失敗自動回復舊版。
set -Eeuo pipefail

usage() {
    cat <<'HELP'
用法：sudo bash deploy/redeploy-ubuntu.sh [選項]

  --pull             先 git fetch 並 fast-forward 目前分支（預設只部署工作目錄現況）
  --ref <ref>        改為 checkout 指定 commit / tag（detached），隱含 --pull 的 fetch
  --test             建置前先跑單元測試（dotnet test --project tests/Ymir.UnitTests）
  --skip-migrate     不處理資料庫 migration
  --skip-backup      有 migration 時也不先做 SQL 備份（不建議）
  --skip-runtime-host  不更新 RuntimeHost
  --with-agent-image 重建 Agent runtime image（只有 runtime/agent 變動才需要）
  --with-services    一併更新已安裝的 MCP Gateway / SiteHost / MaintenanceHost
  --force            即使有 Queued / Running 的 Agent 工作也繼續（會中斷它們）
  --wait-idle <秒>   有執行中工作時最多等待這麼久，逾時才中止（預設 0 = 不等）
  --dry-run          只列出將執行的動作與待套 migration，不做任何變更
  -h, --help         顯示說明

環境變數（也可寫在 /etc/ymir/redeploy.env，內容為 KEY=VALUE 的 shell 語法）：
  YMIR_API_URL          API 本機位址（預設 http://127.0.0.1:5080）
  YMIR_SQL_CONTAINER    SQL Server container 名稱（預設 ymir-sql）
  YMIR_SQL_PODMAN_USER  SQL container 在哪個帳號的 rootless Podman（預設空 = root）
  YMIR_AGENT_IMAGE      Agent image tag（預設 localhost/ymir/agent-runtime:1.0.0）
  YMIR_PI_VERSION       Agent image 的 Pi 版本（預設 1.0.0）
  YMIR_BACKUP_DIR       SQL 備份在 container 內的目錄（預設 /var/opt/mssql/backup）
  YMIR_STATE_DIR        部署紀錄與備份（預設 /var/lib/ymir-deploy）
  YMIR_KEEP_RELEASES    保留幾份舊的 API image / 發佈目錄（預設 5）
  YMIR_HEALTH_TIMEOUT   health 等待秒數（預設 120）

不會做的事：重建 SQL / LiteLLM / nginx / Tunnel、改動 /etc/ymir/*.env、刪除使用者資料。
HELP
}

# ---------- 參數 ----------
DO_PULL=0 REF='' DO_TEST=0 DO_MIGRATE=1 DO_BACKUP=1 DO_RUNTIME_HOST=1
DO_AGENT_IMAGE=0 DO_SERVICES=0 FORCE=0 WAIT_IDLE=0 DRY_RUN=0
while (( $# )); do
    case $1 in
        --pull) DO_PULL=1 ;;
        --ref) [[ $# -ge 2 ]] || { echo '--ref 需要參數' >&2; exit 2; }; REF=$2; DO_PULL=1; shift ;;
        --test) DO_TEST=1 ;;
        --skip-migrate) DO_MIGRATE=0 ;;
        --skip-backup) DO_BACKUP=0 ;;
        --skip-runtime-host) DO_RUNTIME_HOST=0 ;;
        --with-agent-image) DO_AGENT_IMAGE=1 ;;
        --with-services) DO_SERVICES=1 ;;
        --force) FORCE=1 ;;
        --wait-idle) [[ ${2:-} =~ ^[0-9]+$ ]] || { echo '--wait-idle 需要秒數' >&2; exit 2; }; WAIT_IDLE=$2; shift ;;
        --dry-run) DRY_RUN=1 ;;
        -h|--help) usage; exit 0 ;;
        *) echo "不支援的參數：$1（--help 看說明）" >&2; exit 2 ;;
    esac
    shift
done

# ---------- 設定 ----------
[[ -r /etc/ymir/redeploy.env ]] && . /etc/ymir/redeploy.env
API_URL=${YMIR_API_URL:-http://127.0.0.1:5080}
SQL_CONTAINER=${YMIR_SQL_CONTAINER:-ymir-sql}
SQL_PODMAN_USER=${YMIR_SQL_PODMAN_USER:-}
AGENT_IMAGE=${YMIR_AGENT_IMAGE:-localhost/ymir/agent-runtime:1.0.0}
PI_VERSION=${YMIR_PI_VERSION:-1.0.0}
BACKUP_DIR=${YMIR_BACKUP_DIR:-/var/opt/mssql/backup}
STATE_DIR=${YMIR_STATE_DIR:-/var/lib/ymir-deploy}
KEEP=${YMIR_KEEP_RELEASES:-5}
HEALTH_TIMEOUT=${YMIR_HEALTH_TIMEOUT:-120}

SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
REPO_ROOT=$(cd -- "$SCRIPT_DIR/.." && pwd)
QUADLET=/etc/containers/systemd/ymir-api.container
RUNTIME_HOST_DIR=/opt/ymir/runtime-host
RELEASES_DIR=/opt/ymir/releases
IMAGE_REPO=localhost/ymir/api
TS=$(date +%Y%m%d-%H%M%S)

log()  { printf '\n==> %s\n' "$*"; }
info() { printf '    %s\n' "$*"; }
warn() { printf '警告：%s\n' "$*" >&2; }
ROLLED_BACK=0
fail() {
    printf '錯誤：%s\n' "$*" >&2
    trap - ERR
    if (( ! DRY_RUN && ! ROLLED_BACK )); then ROLLED_BACK=1; rollback; fi
    exit 1
}
run()  { if (( DRY_RUN )); then info "[dry-run] $*"; else "$@"; fi; }

# ---------- 回復所需狀態 ----------
QUADLET_BACKUP='' RUNTIME_BACKUP='' API_SWITCHED=0 RUNTIME_SWITCHED=0 LOCKED=0
STAGING=''

rollback() {
    warn '部署失敗，回復舊版（已套用的 additive migration 與資料保留）。'
    if (( RUNTIME_SWITCHED )) && [[ -n $RUNTIME_BACKUP && -d $RUNTIME_BACKUP ]]; then
        info '回復 RuntimeHost'
        rm -rf "$RUNTIME_HOST_DIR.failed" && mv "$RUNTIME_HOST_DIR" "$RUNTIME_HOST_DIR.failed" \
            && mv "$RUNTIME_BACKUP" "$RUNTIME_HOST_DIR" \
            && systemctl restart ymir-runtime-host.service || warn 'RuntimeHost 回復失敗，請手動處理。'
    fi
    if (( API_SWITCHED )) && [[ -n $QUADLET_BACKUP && -f $QUADLET_BACKUP ]]; then
        info '回復 API Quadlet'
        install -m 644 "$QUADLET_BACKUP" "$QUADLET" && systemctl daemon-reload \
            && systemctl restart ymir-api.service || warn 'API 回復失敗，請手動處理（備份：'"$QUADLET_BACKUP"'）。'
    elif (( LOCKED )); then
        # 只停了 API 還沒切換（例如 migration 失敗）：把舊版 API 開回來。
        systemctl start ymir-api.service 2>/dev/null || true
    fi
}
cleanup() { [[ -n $STAGING && -d $STAGING ]] && rm -rf "$STAGING"; return 0; }
on_error() {
    local code=$? line=$1
    trap - ERR
    printf '錯誤：第 %s 行失敗（exit %s）。\n' "$line" "$code" >&2
    if (( ! DRY_RUN && ! ROLLED_BACK )); then ROLLED_BACK=1; rollback; fi
    exit "$code"
}
trap 'on_error $LINENO' ERR
trap cleanup EXIT

# ---------- 前置檢查 ----------
log '前置檢查'
[[ $EUID -eq 0 ]] || fail '請使用 sudo 執行。'
for c in git podman dotnet curl flock python3 systemctl sed; do
    command -v "$c" >/dev/null || fail "找不到 $c。"
done
[[ -d /run/systemd/system ]] || fail '需要運作中的 systemd。'
[[ -f $QUADLET ]] || fail "找不到 $QUADLET，請先完成首次安裝（docs/guides/ubuntu-install.md §8）。"
[[ -f /etc/ymir/api.env ]] || fail '找不到 /etc/ymir/api.env。'
(( ! DO_RUNTIME_HOST )) || [[ -d $RUNTIME_HOST_DIR ]] || fail "找不到 $RUNTIME_HOST_DIR，請先完成 RuntimeHost 安裝，或加 --skip-runtime-host。"
[[ $KEEP =~ ^[0-9]+$ ]] || fail 'YMIR_KEEP_RELEASES 必須是數字。'
[[ -f $REPO_ROOT/Ymir.slnx ]] || fail "$REPO_ROOT 不是 Ymir repo。"
cd "$REPO_ROOT"

# repo 若由一般使用者擁有，root 下的 git 會報 dubious ownership；只對此路徑放行。
git config --global --get-all safe.directory | grep -qx "$REPO_ROOT" || git config --global --add safe.directory "$REPO_ROOT"

sql_exec() {
    # 以 stdin 餵 SQL；sa 密碼只在 container 內由環境變數取得，不經程序參數。
    local inner='export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"; exec /opt/mssql-tools18/bin/sqlcmd -I -C -S localhost -U sa -b "$@"'
    if [[ -n $SQL_PODMAN_USER ]]; then
        local uid; uid=$(id -u "$SQL_PODMAN_USER")
        sudo -H -u "$SQL_PODMAN_USER" env XDG_RUNTIME_DIR="/run/user/$uid" \
            podman exec -i "$SQL_CONTAINER" sh -c "$inner" sqlcmd "$@"
    else
        podman exec -i "$SQL_CONTAINER" sh -c "$inner" sqlcmd "$@"
    fi
}

if (( DO_MIGRATE )); then
    echo 'SELECT 1' | sql_exec -d master -h -1 >/dev/null 2>&1 \
        || fail "連不上 SQL container（$SQL_CONTAINER）。可用 YMIR_SQL_CONTAINER / YMIR_SQL_PODMAN_USER 指定，或加 --skip-migrate。"
fi

# ---------- 同步程式 ----------
if (( DO_PULL )); then
    log '同步程式碼'
    git fetch --tags origin
    if [[ -n $REF ]]; then
        git switch --detach "$REF"
    else
        git merge --ff-only '@{u}'
    fi
fi
SHA=$(git rev-parse --short=12 HEAD)
if [[ -n $(git status --porcelain) ]]; then
    warn '工作目錄有未提交變更，會連同部署（image tag 附時間戳）。'
    TAG="$SHA-dirty-$TS"
else
    TAG=$SHA
fi
info "版本：$TAG（$(git log -1 --format=%s)）"

if (( DO_TEST )); then
    log '單元測試'
    run dotnet test --project tests/Ymir.UnitTests
fi

# ---------- 建置（鎖之外，長時間作業不阻擋維護服務） ----------
log "建置 API image（含 Angular）：$IMAGE_REPO:$TAG"
NEW_IMAGE_ID=''
if (( DRY_RUN )); then
    info "[dry-run] podman build -f src/Ymir.Api/Containerfile -t $IMAGE_REPO:$TAG ."
else
    podman build -f src/Ymir.Api/Containerfile -t "$IMAGE_REPO:$TAG" .
    NEW_IMAGE_ID=$(podman image inspect "$IMAGE_REPO:$TAG" --format '{{.Id}}')
    info "image ID：${NEW_IMAGE_ID:0:12}"
fi

# Quadlet 目前的 Image=（可能是 tag 或 64 碼 ID）換算成 ID，用來判斷是否有變。
CUR_REF=$(sed -n 's/^Image=//p' "$QUADLET" | head -1)
CUR_IMAGE_ID=$(podman image inspect "$CUR_REF" --format '{{.Id}}' 2>/dev/null || true)
info "目前 Quadlet：${CUR_REF:0:19}…"

if (( DO_AGENT_IMAGE )); then
    log "建置 Agent image：$AGENT_IMAGE（ymir 帳號的 rootless 儲存區）"
    YUID=$(id -u ymir)
    if (( DRY_RUN )); then
        info '[dry-run] podman build runtime/agent'
    else
        sudo -H -u ymir env XDG_RUNTIME_DIR="/run/user/$YUID" \
            podman build --build-arg "PI_VERSION=$PI_VERSION" -t "$AGENT_IMAGE" \
            -f runtime/agent/Containerfile runtime/agent
        warn 'Agent image 已更新；既有使用者 container 不會自動替換，下次重建時才用新版（保留 workspace）。'
    fi
fi

install -d -m 755 /opt/ymir "$RELEASES_DIR"
STAGING=$(mktemp -d /opt/ymir/.staging.XXXXXX)

RUNTIME_CHANGED=0
if (( DO_RUNTIME_HOST )); then
    log 'publish RuntimeHost'
    if (( DRY_RUN )); then
        info '[dry-run] dotnet publish src/Ymir.RuntimeHost'
    else
        dotnet publish src/Ymir.RuntimeHost -c Release -o "$STAGING/runtime-host" --nologo -v quiet
        if diff -rq "$STAGING/runtime-host" "$RUNTIME_HOST_DIR" >/dev/null 2>&1; then
            info 'RuntimeHost 與已安裝版本相同，略過。'
        else
            RUNTIME_CHANGED=1
            info 'RuntimeHost 有差異，將於切換階段更新。'
        fi
    fi
fi

# ---------- migration 清單（只讀） ----------
PENDING=0
declare -A MIG_SQL=()
if (( DO_MIGRATE )); then
    log '檢查待套用的 migration'
    dotnet tool restore >/dev/null
    declare -A MIG_PROJECT=(
        [platform]=src/Platform/Ymir.Platform.Infrastructure
        [vibemaker]=src/Modules/VibeMaker/Ymir.VibeMaker.Infrastructure
    )
    declare -A MIG_CONTEXT=([platform]=PlatformDbContext [vibemaker]=VibeMakerDbContext)
    for schema in platform vibemaker; do
        applied=$(printf "SET NOCOUNT ON; IF OBJECT_ID(N'%s.__ef_migrations_history') IS NOT NULL SELECT MigrationId FROM [%s].[__ef_migrations_history];" "$schema" "$schema" \
            | sql_exec -d ymir -h -1 -W | tr -d '\r' | sed '/^$/d')
        all=$(dotnet ef migrations list --no-connect --project "${MIG_PROJECT[$schema]}" \
            --context "${MIG_CONTEXT[$schema]}" 2>/dev/null | grep -Eo '^[0-9]{14}_[A-Za-z0-9_]+' || true)
        [[ -n $all ]] || fail "讀不到 $schema 的 migration 清單。"
        pending=$(comm -13 <(printf '%s\n' "$applied" | sort) <(printf '%s\n' "$all" | sort) || true)
        if [[ -n $pending ]]; then
            while IFS= read -r m; do info "待套用：$schema / $m"; PENDING=$((PENDING+1)); done <<<"$pending"
            MIG_SQL[$schema]="$STATE_DIR/migrations/$TAG/$schema.sql"
            if (( ! DRY_RUN )); then
                install -d -m 700 "$STATE_DIR/migrations/$TAG"
                dotnet ef migrations script --idempotent --project "${MIG_PROJECT[$schema]}" \
                    --context "${MIG_CONTEXT[$schema]}" --output "${MIG_SQL[$schema]}" >/dev/null
            fi
        fi
    done
    (( PENDING )) || info '沒有待套用的 migration。'
fi

# ---------- 判斷是否有事可做 ----------
API_CHANGED=1
[[ -n $NEW_IMAGE_ID && $NEW_IMAGE_ID == "$CUR_IMAGE_ID" ]] && API_CHANGED=0
if (( ! DRY_RUN && ! API_CHANGED && ! RUNTIME_CHANGED && ! PENDING && ! DO_SERVICES )); then
    log '沒有任何差異，不需要重新部署。'
    exit 0
fi
if (( DRY_RUN )); then
    log 'dry-run 結束：未做任何變更。'
    exit 0
fi

# ---------- 部署鎖 + 排空檢查 ----------
log '取得部署鎖'
LOCK=/run/ymir-lifecycle/deploy.lock
if [[ ! -d $(dirname "$LOCK") ]]; then
    warn '找不到 /run/ymir-lifecycle（未安裝 MaintenanceHost 鎖目錄），改用 /run/lock/ymir-deploy.lock。'
    LOCK=/run/lock/ymir-deploy.lock
fi
exec 9>"$LOCK"
flock -w 600 9 || fail '等不到部署鎖（另一個部署或維護作業進行中）。'
LOCKED=1

busy_count() {
    printf "SET NOCOUNT ON; SELECT COUNT(*) FROM [vibemaker].[agent_executions] WHERE [status] IN (N'QUEUED', N'RUNNING');" \
        | sql_exec -d ymir -h -1 -W 2>/dev/null | tr -d '\r[:space:]' || true
}
if (( DO_MIGRATE )); then
    # 重啟 API 會中斷背景執行中的 Agent 工作，先確認已排空。
    deadline=$((SECONDS + WAIT_IDLE))
    while :; do
        busy=$(busy_count)
        [[ $busy =~ ^[0-9]+$ ]] || { warn '無法查詢執行中的工作，略過檢查。'; break; }
        (( busy == 0 )) && break
        if (( SECONDS < deadline )); then info "仍有 $busy 個 Queued/Running 工作，等待中…"; sleep 10; continue; fi
        (( FORCE )) && { warn "有 $busy 個工作會被中斷（--force）。"; break; }
        fail "有 $busy 個 Queued/Running 的 Agent 工作。等它們結束、加 --wait-idle <秒>，或用 --force。"
    done
else
    warn '--skip-migrate 時不查詢資料庫，無法確認是否有執行中的 Agent 工作；API 重啟會中斷它們。'
fi

# ---------- migration（API 停止後套用） ----------
if (( PENDING )); then
    log '停止 API 並套用 migration'
    systemctl stop ymir-api.service
    if (( DO_BACKUP )); then
        BAK="ymir-pre-$TAG-$TS.bak"
        info "SQL 備份（COPY_ONLY, CHECKSUM）→ container 內 $BACKUP_DIR/$BAK"
        printf "BACKUP DATABASE [ymir] TO DISK = N'%s/%s' WITH COPY_ONLY, CHECKSUM, INIT;\nRESTORE VERIFYONLY FROM DISK = N'%s/%s' WITH CHECKSUM;\n" \
            "$BACKUP_DIR" "$BAK" "$BACKUP_DIR" "$BAK" | sql_exec -d master >/dev/null
        info '備份並驗證完成；請另外把它複製到容器外保存（docs/guides/backup-restore.md）。'
    else
        warn '已略過 SQL 備份（--skip-backup）。'
    fi
    # 兩個模組依序套用；SET 選項是 filtered index 所需（Msg 1934），Python 同時去除 BOM。
    for schema in platform vibemaker; do
        [[ -n ${MIG_SQL[$schema]:-} ]] || continue
        info "套用 $schema"
        python3 - "${MIG_SQL[$schema]}" <<'PY' | sql_exec -d ymir >/dev/null
from pathlib import Path
import sys
print("SET NOCOUNT ON; SET XACT_ABORT ON; SET QUOTED_IDENTIFIER ON;")
print("SET ANSI_NULLS ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON;")
print("SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;\nGO")
sys.stdout.write(Path(sys.argv[1]).read_text(encoding="utf-8-sig"))
print("\nGO")
PY
    done
fi

# ---------- 切換 ----------
if (( RUNTIME_CHANGED )); then
    log '更新 RuntimeHost'
    RUNTIME_BACKUP="$RELEASES_DIR/runtime-host-before-$TAG-$TS"
    # 設定檔在 /etc/ymir，不在發佈目錄，所以整個目錄替換是安全的。
    cp -a "$RUNTIME_HOST_DIR" "$RUNTIME_BACKUP"
    rm -rf "$RUNTIME_HOST_DIR.new"
    cp -a "$STAGING/runtime-host" "$RUNTIME_HOST_DIR.new"
    chmod -R a+rX "$RUNTIME_HOST_DIR.new"
    RUNTIME_SWITCHED=1
    rm -rf "$RUNTIME_HOST_DIR.old"
    mv "$RUNTIME_HOST_DIR" "$RUNTIME_HOST_DIR.old" && mv "$RUNTIME_HOST_DIR.new" "$RUNTIME_HOST_DIR"
    rm -rf "$RUNTIME_HOST_DIR.old"
    systemctl restart ymir-runtime-host.service   # Agent container 不受影響，下次 EnsureRuntime 沿用
    deadline=$((SECONDS + 60))
    until sudo -u ymir curl -fsS --max-time 5 --unix-socket /run/ymir-runtime/runtime.sock http://localhost/health >/dev/null 2>&1; do
        (( SECONDS < deadline )) || fail 'RuntimeHost 重啟後 health 未就緒。'
        sleep 2
    done
    info 'RuntimeHost 就緒'
fi

log "切換 API image → ${NEW_IMAGE_ID:0:12}"
QUADLET_BACKUP="$RELEASES_DIR/ymir-api.container.$TS"
install -d -m 755 "$RELEASES_DIR"
cp -a "$QUADLET" "$QUADLET_BACKUP"
# 只替換 Image= 一行，固定到 image ID（浮動 tag 不會換版）。
sed -i "s|^Image=.*|Image=$NEW_IMAGE_ID|" "$QUADLET"
API_SWITCHED=1
systemctl daemon-reload
systemctl restart ymir-api.service

# ---------- 驗證 ----------
log '驗證'
wait_http() {
    local url=$1 deadline=$((SECONDS + HEALTH_TIMEOUT))
    until curl -fsS --max-time 5 -o /dev/null "$url"; do
        (( SECONDS < deadline )) || fail "逾時未就緒：$url（查看 journalctl -u ymir-api.service -n 100）"
        sleep 3
    done
    info "OK  $url"
}
wait_http "$API_URL/health"
wait_http "$API_URL/api/auth/providers"
wait_http "$API_URL/login"
[[ $(curl -fsS --max-time 5 "$API_URL/health") == Healthy ]] || fail "/health 不是 Healthy。"
running=$(podman inspect ymir-api --format '{{.Image}}' 2>/dev/null || true)
[[ $running == "$NEW_IMAGE_ID" ]] || fail 'ymir-api 容器沒有使用新 image。'

# ---------- 可選服務 ----------
if (( DO_SERVICES )); then
    log '更新其他主機服務'
    declare -A SVC=(
        [ymir-mcp-gateway]="src/Ymir.McpGateway:/opt/ymir/mcp-gateway"
        [ymir-site-host]="src/Ymir.SiteHost:/opt/ymir/site-host"
        [ymir-maintenance-host]="src/Ymir.MaintenanceHost:/opt/ymir/maintenance-host"
    )
    for unit in "${!SVC[@]}"; do
        systemctl cat "$unit.service" >/dev/null 2>&1 || { info "略過 $unit（未安裝）"; continue; }
        proj=${SVC[$unit]%%:*} dest=${SVC[$unit]##*:}
        out="$STAGING/$unit"
        dotnet publish "$proj" -c Release -o "$out" --nologo -v quiet
        if diff -rq "$out" "$dest" >/dev/null 2>&1; then info "$unit 無差異"; continue; fi
        cp -a "$dest" "$RELEASES_DIR/$unit-before-$TAG-$TS" 2>/dev/null || true
        # 就地覆蓋內容而非換目錄，保留 unit 的 WorkingDirectory / 權限。
        cp -a "$out/." "$dest/"
        systemctl restart "$unit.service"
        info "已更新並重啟 $unit"
    done
fi

# ---------- 清理舊版（保留最近 KEEP 份；不動使用中的 image） ----------
log '清理舊版本'
in_use=$(podman image inspect "$IMAGE_REPO:$TAG" --format '{{.Id}}')
mapfile -t old_tags < <(podman images --format '{{.CreatedAt}}|{{.Repository}}:{{.Tag}}' "$IMAGE_REPO" 2>/dev/null \
    | grep -v ':<none>' | sort -r | cut -d'|' -f2 | tail -n +"$((KEEP + 1))")
for t in "${old_tags[@]:-}"; do
    [[ -n $t ]] || continue
    id=$(podman image inspect "$t" --format '{{.Id}}' 2>/dev/null || true)
    [[ $id == "$in_use" ]] && continue
    podman rmi "$t" >/dev/null 2>&1 && info "移除舊 image $t" || true
done
find "$RELEASES_DIR" -maxdepth 1 -mindepth 1 \( -name 'ymir-api.container.*' -o -name '*-before-*' \) -printf '%T@ %p\n' 2>/dev/null \
    | sort -rn | tail -n +"$((KEEP * 3 + 1))" | cut -d' ' -f2- | xargs -r rm -rf

trap - ERR
log "部署完成：$TAG（image ${NEW_IMAGE_ID:0:12}）"
info "公開入口（nginx / Tunnel）不受影響；回復用 Quadlet 備份：$QUADLET_BACKUP"
