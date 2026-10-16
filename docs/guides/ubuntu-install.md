# Ubuntu 正式安裝總指南

> 適用：**乾淨的 Ubuntu 24.04 LTS、x86-64、單機部署**。API 使用 rootful Podman，Agent 使用專用帳號的 rootless Podman。Windows／Docker 開發驗證請看 [Windows 指南](windows-docker.md)。
>
> 這是安裝順序與配置參考，**不是已在所有乾淨主機驗證的一鍵安裝器**。文件已對照程式與部署範本；實機仍須完成本文驗收及還原演練。不要在既有正式主機逐段重跑首次安裝命令；更新請直接看[更新與回復](#12-更新與回復)。

## 1. 先選定架構與準備資料

本指南統一使用 Podman，不需要同時安裝 Docker daemon：

```text
瀏覽器 → HTTPS / Cloudflare → cloudflared（ymir，rootless，host network）
                               ↓ http://127.0.0.1:5080
                         API + Angular（rootful，程序 uid 1654）
                           ├─ SQL Server：127.0.0.1:1433
                           ├─ LiteLLM：127.0.0.1:4000 → 專用 PostgreSQL
                           └─ RuntimeHost Unix socket + token
                                  ↓ 帳號 ymir / rootless Podman
                             每位使用者／群組的 Agent container
                                  ↓ http://litellm:4000/v1
                             rootless proxy → Unix bridge → LiteLLM
```

**與現有正式主機的差別**：現有主機可能有 `cloudflared → ymir-web nginx → API`。本文為新主機採現有範本支援的 **Tunnel 直連 loopback API**，不安裝 `ymir-origin-production.service`。不要更改既有主機 DNS／Tunnel 來源，也不要把 `activate-linux-production.sh`、`fix-linux-exec-scope.sh` 或主機專用 `ymir-production-install/` 當全新安裝器。

準備清單：

| 項目 | 要確認的內容 |
|---|---|
| 主機 | systemd 255 或更新、cgroup v2、Podman 4.9 或更新（本文使用 `.network` Quadlet）；建議 4 vCPU／16 GiB RAM／100 GiB SSD 起，容量須依使用者及文件量調整，這不是壓測保證 |
| 執行容量 | SQL、API、LiteLLM、OS 都要留 RAM；Agent 範本每個上限 2 GiB，請設定人數／執行政策，不要用 swap 取代容量規劃 |
| 原始碼 | 有 GitHub 存取權；選擇 CI 通過的 commit，記錄完整 SHA，不直接以浮動 `main` 當部署版本 |
| .NET | 系統層 .NET 10 SDK **及 ASP.NET Core 10 runtime**；依 [Microsoft Ubuntu 安裝說明](https://learn.microsoft.com/dotnet/core/install/linux-ubuntu-install) 安裝，避免混用不同來源套件 |
| Node | 若在主機跑前端／Pi 測試，準備 Node 24 LTS（至少 24.15）；API Containerfile 自帶 Node 建置階段，僅建 image 時不需主機 Node |
| 模型 | 至少一個支援工具呼叫的真實模型路由與金鑰；LiteLLM alias 要與 Ymir Model ID 一致；真實 smoke test 可能計費 |
| 登入 | Entra tenant／client／secret 與管理員 app role，或先建立本機管理員；Google 為選用，不預設啟用 |
| 公開網域 | 例如 `ymir.example.com`、Cloudflare Tunnel 權限；部署前先確認 hostname 與 SSO callback URI |
| 備份 | 獨立且加密的備份位置、保管 SQL／keyring／workspace／LiteLLM PostgreSQL 及 salt 的方法 |

防火牆只開管理需要的 SSH；不公開 1433、4000、5080、5090、PostgreSQL 或 runtime socket。Tunnel 為 outbound 連線，依 Cloudflare 官方要求允許其出站流量；不要為了排錯關閉整台防火牆。

## 2. 安裝工具、取得固定版本

以具 sudo 權限的管理帳號執行；所有命令都必須在看過輸出且成功後才進下一步：

```bash
sudo apt-get update
sudo apt-get install -y git curl ca-certificates openssl jq python3 podman uidmap \
  dbus-user-session slirp4netns fuse-overlayfs socat

dotnet --info
dotnet --list-runtimes
podman --version
stat -fc %T /sys/fs/cgroup
```

確認 ASP.NET Core 10 在 runtime 清單、cgroup 為 `cgroup2fs`。範本 RuntimeHost 使用 `/usr/bin/dotnet`，如果 .NET 裝在別處，先調整自己的 unit；不要假設互動 shell 的 PATH 會傳給 systemd。Ubuntu 套件的 Node 版本不一定符合 Angular，需另外確認。

```bash
mkdir -p "$HOME/src"
git clone https://github.com/puremars2015/Ymir.git "$HOME/src/Ymir"
cd "$HOME/src/Ymir"
git fetch origin
# 填入已通過 CI 的完整 commit；只在這份全新部署 checkout 操作。
read -r -p '部署 commit SHA: ' RELEASE
git switch --detach "$RELEASE"
RELEASE=$(git rev-parse HEAD)
REPO_ROOT=$PWD
export RELEASE REPO_ROOT
dotnet tool restore
```

下文假設同一個 Bash session 保留這兩個變數。版本最低要求以 [`global.json`](../../global.json)、[`web/package.json`](../../web/package.json) 及 Containerfile 為準。首次部署開始前，確認 `/etc/ymir`、`ymir` 帳號與同名服務不是其他部署留下的；若已存在，停止並先盤點，不覆蓋。

本指南使用 Bash；執行 pipeline 前開啟 `set -o pipefail`，讓建置／SQL 輸入來源失敗也能被發現。**不要開 `set -x`**，避免將秘密展開到 console／log。

```bash
set -o pipefail
```

## 3. 建立身分、目錄與 rootless 基礎

```bash
sudo groupadd --system ymir-runtime
sudo useradd --create-home --shell /usr/sbin/nologin ymir
sudo loginctl enable-linger ymir
sudo systemctl start "user@$(id -u ymir).service"
grep '^ymir:' /etc/subuid /etc/subgid

sudo install -d -m 755 /etc/ymir /etc/containers/systemd
sudo install -d -o ymir -g ymir -m 700 /srv/ymir/workspaces
sudo install -d -o 1654 -g 1654 -m 700 /srv/ymir/api/keys
sudo install -m 644 deploy/maintenance/ymir-lifecycle.tmpfiles.conf /etc/tmpfiles.d/ymir-lifecycle.conf
sudo systemd-tmpfiles --create /etc/tmpfiles.d/ymir-lifecycle.conf
```

`/etc/subuid`、`/etc/subgid` 必須各有供 `ymir` 使用、互不衝突的至少 65536 個 id。`useradd` 通常會分配；沒有時由系統管理員先保留未使用範圍，再以 `usermod --add-subuids/--add-subgids` 設定。**不要盲目複製其他帳號的 100000 起始範圍**。不要廣泛 chown 主機使用者的資料或 cgroup。

在本 session 定義 helper，固定切到 `ymir` 自己的 cwd，避免管理者 home 無法存取：

```bash
ymir_run() {
  sudo -u ymir -H env XDG_RUNTIME_DIR="/run/user/$(id -u ymir)" \
    sh -c 'cd /home/ymir && exec "$@"' sh "$@"
}
ymir_run podman info --format '{{.Host.Security.Rootless}}'
```

應回 `true`。API、資料庫／模型基礎服務的 image 在 **root** store；Agent、模型 proxy、Tunnel 在 **ymir** store。用錯帳號時 image／network 看似不存在，不要改成 privileged 來解決。

## 4. 安裝持久化 SQL Server、PostgreSQL 與 LiteLLM

本節的 rootful Quadlet 是全新主機的參考配置，沿用專案 LiteLLM sample 的 image 版本及設定；若用公司既有資料庫／模型服務，可略過對應容器，但須自行確認權限、TLS、備份與開機恢復。

### 4.1 密鑰與模型設定

先建立**不存在的** secret 檔，確認沒有同名檔案才執行：

```bash
sudo install -m 600 /dev/null /etc/ymir/sql.env
sudo install -m 600 /dev/null /etc/ymir/postgres.env
sudo install -m 600 /dev/null /etc/ymir/litellm.env
sudo install -m 644 deploy/litellm/config.yaml /etc/ymir/litellm.yaml
sudoedit /etc/ymir/sql.env /etc/ymir/postgres.env /etc/ymir/litellm.env /etc/ymir/litellm.yaml
```

`/etc/ymir/sql.env`：

```dotenv
ACCEPT_EULA=Y
# Express 可用於正式用途，但有容量／資源限制；超過限制時須採實際授權的 edition。
# 不要把 Developer edition 當正式生產授權；Evaluation 也有期限。
MSSQL_PID=Express
MSSQL_SA_PASSWORD=<由密碼管理器產生的高強度密碼，符合SQL密碼政策>
```

`/etc/ymir/postgres.env`：

```dotenv
POSTGRES_DB=litellm
POSTGRES_USER=litellm
POSTGRES_PASSWORD=<獨立的PostgreSQL密碼>
```

`/etc/ymir/litellm.env`：

```dotenv
LITELLM_MASTER_KEY=<sk-開頭的隨機master-key>
LITELLM_SALT_KEY=<獨立且固定的隨機salt>
DATABASE_URL=postgresql://litellm:<與POSTGRES_PASSWORD相同，須URL編碼>@ymir-litellm-db:5432/litellm
STORE_MODEL_IN_DB=False
OR_APP_NAME=Ymir
MINIMAX_API_KEY=<只在啟用MiniMax時填入>
OPENROUTER_API_KEY=<只在啟用OpenRouter時填入>
```

以上 placeholder 均須替換。為避免 URL 編碼錯誤，PostgreSQL 密碼可選足夠長的隨機十六進位值；SQL Server 密碼另外須符合複雜度政策。**salt 上線後不可任意更換**。不要 `source /etc/ymir/*.env`，這是 systemd EnvironmentFile，不是任意可執行 shell 腳本。

編輯 `/etc/ymir/litellm.yaml`：只保留確實可用且有金鑰的 `model_list` 項目；正式環境移除 `fake-model` 與未使用供應商的項目，避免 undefined `FAKE_LLM_BASE` 或誤開模型。此 YAML 可讀取但**不得內嵌秘密**，只使用 `os.environ/...` 引用 600 env 檔中的值；保留 `general_settings.master_key`、關閉 telemetry，核對價格、圖片／tools 能力；詳見 [LiteLLM 指南](../../deploy/litellm/README.md)。

### 4.2 Rootful Quadlet 參考配置

以下各區塊分別寫到標示的檔案，權限 644、owner root；不要把 secret 直接寫進 unit。首次拉取並記錄 image ID：

```bash
sudo podman pull mcr.microsoft.com/mssql/server:2022-latest
sudo podman pull docker.io/library/postgres:17.6-alpine
sudo podman pull ghcr.io/berriai/litellm:v1.103.2
sudo podman image inspect mcr.microsoft.com/mssql/server:2022-latest --format '{{.Id}}'
sudo podman image inspect docker.io/library/postgres:17.6-alpine --format '{{.Id}}'
sudo podman image inspect ghcr.io/berriai/litellm:v1.103.2 --format '{{.Id}}'
```

把下列 `Image=` 替換成各自的實際 ID，固定部署版本；尤其 SQL 的 `2022-latest` 是拉取入口，不是不可變版本。先保留 image 與設定，更新不得同時任意升級資料庫 major version。

```ini
# /etc/containers/systemd/ymir-sql.volume
[Volume]
VolumeName=ymir-sql-data
```

```ini
# /etc/containers/systemd/ymir-sql.container
[Unit]
Description=Ymir SQL Server
Wants=network-online.target
After=network-online.target
[Container]
Image=<SQL image ID>
Pull=never
ContainerName=ymir-sql
EnvironmentFile=/etc/ymir/sql.env
PublishPort=127.0.0.1:1433:1433
Volume=ymir-sql.volume:/var/opt/mssql:U
[Service]
Restart=on-failure
TimeoutStartSec=300
[Install]
WantedBy=multi-user.target
```

`:U` 只用於這個**新建、專用 named volume**，讓 SQL 的非 root 使用者可寫；不要將它套到既有主機路徑或未知資料。container 重建不等於刪 volume，禁止 `volume prune` 或刪除資料 volume 作為更新步驟。

```ini
# /etc/containers/systemd/ymir-model-infra.network
[Network]
NetworkName=ymir-model-infra
```

```ini
# /etc/containers/systemd/ymir-litellm-db.volume
[Volume]
VolumeName=ymir-litellm-db-data
```

```ini
# /etc/containers/systemd/ymir-litellm-db.container
[Unit]
Description=Ymir LiteLLM PostgreSQL
[Container]
Image=<PostgreSQL image ID>
Pull=never
ContainerName=ymir-litellm-db
Network=ymir-model-infra.network
NetworkAlias=ymir-litellm-db
EnvironmentFile=/etc/ymir/postgres.env
Volume=ymir-litellm-db.volume:/var/lib/postgresql/data
HealthCmd=pg_isready -U litellm -d litellm
[Service]
Restart=on-failure
[Install]
WantedBy=multi-user.target
```

```ini
# /etc/containers/systemd/ymir-litellm.container
[Unit]
Description=Ymir LiteLLM
Requires=ymir-litellm-db.service
After=ymir-litellm-db.service network-online.target
Wants=network-online.target
[Container]
Image=<LiteLLM image ID>
Pull=never
ContainerName=ymir-litellm
Network=ymir-model-infra.network
EnvironmentFile=/etc/ymir/litellm.env
Volume=/etc/ymir/litellm.yaml:/app/config.yaml:ro
PublishPort=127.0.0.1:4000:4000
Exec=--config /app/config.yaml --port 4000
[Service]
Restart=on-failure
RestartSec=5
TimeoutStartSec=300
[Install]
WantedBy=multi-user.target
```

PostgreSQL **不發布主機 port**。它是 LiteLLM 的 virtual key／預算／用量資料庫，不是 Ymir 的 SQL Server。

```bash
sudo systemctl daemon-reload
sudo systemctl start ymir-sql.service ymir-litellm-db.service
sudo podman exec ymir-litellm-db pg_isready -U litellm -d litellm
sudo podman exec ymir-sql sh -c \
  'export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"; exec /opt/mssql-tools18/bin/sqlcmd -I -C -S localhost -U sa -b -Q "SELECT 1"'
sudo systemctl start ymir-litellm.service
```

SQL 首次初始化及 LiteLLM 啟動需要時間；上述 readiness 未通過就停下排錯，不能只看 unit active。Quadlet 由 `[Install] WantedBy` 在 generator 建立開機依賴，**不要對 generator 產生的 `.service` 執行 `systemctl enable`**；一般自寫 system service 則需要 enable。

## 5. 建立資料庫並套用兩個模組 migration

先確認 SQL 確實是本指南的新容器，不是其他部署的資料庫。建立空 `ymir`：

```bash
printf '%s\n' 'CREATE DATABASE [ymir];' | sudo podman exec -i ymir-sql sh -c \
  'export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"; exec /opt/mssql-tools18/bin/sqlcmd -I -C -S localhost -U sa -d master -b'

mkdir -p "$REPO_ROOT/out/migrations/$RELEASE"
dotnet ef migrations script --idempotent --project src/Platform/Ymir.Platform.Infrastructure \
  --context PlatformDbContext --output "out/migrations/$RELEASE/platform.sql"
dotnet ef migrations script --idempotent --project src/Modules/VibeMaker/Ymir.VibeMaker.Infrastructure \
  --context VibeMakerDbContext --output "out/migrations/$RELEASE/vibemaker.sql"
```

檢閱 SQL、確認目標 commit 後，依序套用；無須把正式密碼傳進 `dotnet ef --connection` 的程序參數：

```bash
apply_ymir_migration() {
  python3 - "$1" <<'PY' | sudo podman exec -i ymir-sql sh -c \
    'export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"; exec /opt/mssql-tools18/bin/sqlcmd -I -C -S localhost -U sa -d ymir -b'
from pathlib import Path
import sys
print("SET NOCOUNT ON; SET XACT_ABORT ON; SET QUOTED_IDENTIFIER ON;")
print("SET ANSI_NULLS ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON;")
print("SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;\nGO")
sys.stdout.write(Path(sys.argv[1]).read_text(encoding="utf-8-sig"))
print("\nGO")
PY
}
# 第一個成功後，才執行第二個；任一失敗即停止本步。
apply_ymir_migration "out/migrations/$RELEASE/platform.sql" && \
  apply_ymir_migration "out/migrations/$RELEASE/vibemaker.sql"
```

**每個指令 exit code 都要為 0**；`-b` 讓 SQL 錯誤失敗，`-I` 與前置 SQL 明確設定 filtered index 所需 session options；Python 同時移除檔案起始 BOM，避免將 BOM 插到 SQL batch 中間。見 [API 部署注意事項](../../deploy/api/README.md)。不要只因 history 有新紀錄就忽略失敗的 schema 操作。Production 不自動 migrate；完整 main 需檢查**兩個** history，不只套最後一個功能的 migration。

### API 使用最小權限帳號

不要把 `sa` 放入 API env。例如建立 `ymir_api` login，授予 `ymir` 的 `db_datareader`、`db_datawriter`；schema migration 仍由部署帳號執行。本機範例限制密碼字元以避免 SQL／connection string quoting 問題，請使用密碼管理器產生至少 24 字元且符合 SQL 密碼政策的值：

```bash
sudo -v
read -r -s -p '新的 ymir_api 密碼（英數與底線，24字元以上）: ' API_DB_PASSWORD
printf '\n'
if [[ ! "$API_DB_PASSWORD" =~ ^[A-Za-z0-9_]{24,128}$ ]]; then
  unset API_DB_PASSWORD
  echo '密碼格式不符，停止。'
  false
else
  printf "CREATE LOGIN [ymir_api] WITH PASSWORD=N'%s';\nUSE [ymir];\nCREATE USER [ymir_api] FOR LOGIN [ymir_api];\nALTER ROLE [db_datareader] ADD MEMBER [ymir_api];\nALTER ROLE [db_datawriter] ADD MEMBER [ymir_api];\n" \
    "$API_DB_PASSWORD" | sudo podman exec -i ymir-sql sh -c \
    'export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"; exec /opt/mssql-tools18/bin/sqlcmd -I -C -S localhost -U sa -d master -b'
  sql_result=$?
  unset API_DB_PASSWORD
  test "$sql_result" -eq 0
fi
```

密碼另存到 secret 管理工具，稍後填入 API env；不把它貼到聊天或命令列 `-P`。跨主機 SQL 應驗證 TLS 憑證；範本 `TrustServerCertificate=True` 只為本機 loopback 自簽連線提供方便，不能照搬到不可信網路。

## 6. 建立 Agent image 與模型連線路徑

先以 `ymir` 身分建立一般與受限網路，再用 stdin 傳建置 context，避免 `ymir` 需要讀管理者的私人 home：

```bash
ymir_run podman network create ymir-agents-net
ymir_run podman network create --internal ymir-agents
tar -C "$REPO_ROOT/runtime/agent" -cf - . | ymir_run podman build \
  --build-arg PI_VERSION=1.0.0 -t localhost/ymir/agent-runtime:1.0.0 -f Containerfile -
ymir_run podman image inspect localhost/ymir/agent-runtime:1.0.0 --format '{{.Id}}'
```

Pi 版本以 `runtime/agent/Containerfile` 為準，不自行升級；主機執行一般使用者請求時不需安裝 npm／Pi 到 root PATH。記錄 Agent image ID，保留對應來源及舊版，不在更新時刪除使用者資料。

由於 LiteLLM 只聽主機 loopback，**Agent 不能用 `127.0.0.1:4000` 或直接照抄 `host.containers.internal` 連它**。安裝已有的 Unix bridge＋雙網路 proxy：

```bash
sudo install -m 644 deploy/runtime-host/litellm-proxy/ymir-litellm-bridge.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now ymir-litellm-bridge.service
sudo install -d -o ymir -g ymir -m 700 /home/ymir/.config/ymir /home/ymir/.config/containers/systemd
sudo install -o ymir -g ymir -m 600 deploy/runtime-host/litellm-proxy/litellm-nginx.conf /home/ymir/.config/ymir/litellm-nginx.conf
sudo install -o ymir -g ymir -m 600 deploy/runtime-host/litellm-proxy/ymir-litellm-proxy.container /home/ymir/.config/containers/systemd/
ymir_run podman pull docker.io/library/nginx:stable-alpine
ymir_run podman image inspect docker.io/library/nginx:stable-alpine --format '{{.Id}}'
sudoedit /home/ymir/.config/containers/systemd/ymir-litellm-proxy.container
```

將 proxy unit 的 `<nginx image>` 換成剛取得的 **ymir store image ID**，再：

```bash
ymir_run systemctl --user daemon-reload
ymir_run systemctl --user start ymir-litellm-proxy.service
ymir_run podman exec ymir-litellm-proxy nginx -t
```

保留 nginx 的 `client_max_body_size 100m`、串流／request buffering 設定。不要發布 proxy port 到主機公網；它只接 `ymir-agents-net`／`ymir-agents`，名稱 `litellm`。詳見 [RuntimeHost 的 bridge 說明](../../deploy/runtime-host/README.md#litellm-在其他帳號執行unix-socket-bridge--proxy-container)。

## 7. 發布與設定 RuntimeHost

```bash
dotnet publish src/Ymir.RuntimeHost -c Release -o "out/runtime-host/$RELEASE"
sudo install -d -m 755 "/opt/ymir/releases/$RELEASE/runtime-host"
sudo cp -a "out/runtime-host/$RELEASE/." "/opt/ymir/releases/$RELEASE/runtime-host/"
sudo chown -R root:root "/opt/ymir/releases/$RELEASE/runtime-host"
sudo chmod -R a+rX "/opt/ymir/releases/$RELEASE/runtime-host"
sudo install -m 600 deploy/runtime-host/runtime-host.env.example /etc/ymir/runtime-host.env
sudoedit /etc/ymir/runtime-host.env
```

必填／核對：

```dotenv
RuntimeHost__Token=<至少32字元，與API的RemoteToken相同>
RuntimeHost__Listen=unix:/run/ymir-runtime/runtime.sock
RuntimeHost__SocketMode=660
XDG_RUNTIME_DIR=/run/user/<id -u ymir 的實際數字>
VibeMaker__Runtime__Provider=Podman
VibeMaker__Runtime__WorkspaceRoot=/srv/ymir/workspaces
VibeMaker__Runtime__Image=localhost/ymir/agent-runtime:1.0.0
VibeMaker__Runtime__ContainerExecutable=/usr/local/libexec/ymir-podman-scope
VibeMaker__Runtime__Network=ymir-agents-net
VibeMaker__Runtime__RestrictedNetwork=ymir-agents
VibeMaker__Runtime__LifecycleLockDirectory=/run/ymir-lifecycle
RuntimeHost__Tunnel__Mode=SystemdUser
```

沿用範本 CPU／memory 設定。token 由密碼管理器或本機安全工具產生；不把值放到 unit、`ps`、Git 或對話。`XDG_RUNTIME_DIR` **不會**替你執行 `$(id -u ymir)`。

Ubuntu 24.04 的 system 層 RuntimeHost 與 user 層 rootless Podman 採不同 cgroup subtree；使用已存在的 scope wrapper 保留 stdin／stdout／argv／env／exit code，不停用 cgroups 或增加容器權限。先確認 user manager scope 可用，再安裝 root-owned wrapper；`--expand-environment=no` 不可移除，否則附件寫入腳本中的 `$1` 等會被 systemd 誤展開。

```bash
ymir_run systemd-run --user --scope --quiet --expand-environment=no -- /usr/bin/true
sudo install -d -m 755 /usr/local/libexec
sudo install -o root -g root -m 755 deploy/runtime-host/ymir-podman-scope /usr/local/libexec/ymir-podman-scope
```

安裝一般 systemd unit，使用 drop-in 指向固定 release，並明確等待 `ymir` 的 user manager：

```bash
sudo install -m 644 deploy/runtime-host/ymir-runtime-host.service /etc/systemd/system/
sudo install -d -m 755 /etc/systemd/system/ymir-runtime-host.service.d
printf '[Unit]\nWants=user@%s.service\nAfter=user@%s.service\n[Service]\nWorkingDirectory=/opt/ymir/releases/%s/runtime-host\nExecStart=\nExecStart=/usr/bin/dotnet /opt/ymir/releases/%s/runtime-host/Ymir.RuntimeHost.dll\n' \
  "$(id -u ymir)" "$(id -u ymir)" "$RELEASE" "$RELEASE" | sudo tee /etc/systemd/system/ymir-runtime-host.service.d/release.conf >/dev/null
sudo systemctl daemon-reload
sudo systemctl enable --now ymir-runtime-host.service
ymir_run curl --fail --unix-socket /run/ymir-runtime/runtime.sock http://localhost/health
```

預期 `ok`；socket owner `ymir:ymir-runtime`、mode 660。保留 unit 的 `RuntimeDirectoryPreserve=yes`，避免 API 掛到重啟前的舊 inode。

## 8. 建置 API／前端、設定正式環境

```bash
sudo podman build -f src/Ymir.Api/Containerfile -t "localhost/ymir/api:$RELEASE" .
API_IMAGE=$(sudo podman image inspect "localhost/ymir/api:$RELEASE" --format '{{.Id}}')
sudo install -m 600 deploy/api/.env.example /etc/ymir/api.env
sudoedit /etc/ymir/api.env
```

**範本不是可直接上線的預設值**：刪除 compose 才用的 `YMIR_RUNTIME_GID`／`YMIR_API_DATA`，替換範本 tenant、client、hostname、模型、所有 `<...>`；若先用本機帳號，移除未配置的 OIDC keys，不要連到範本的企業 tenant。

最少核對：

| API 設定 | 本文取值／來源 |
|---|---|
| `ConnectionStrings__ymir` | `Server=127.0.0.1,1433;Database=ymir;User Id=ymir_api;Password=...;TrustServerCertificate=True` |
| `VibeMaker__Runtime__Provider` | `Remote`（image 已設預設） |
| `VibeMaker__Runtime__Remote__Endpoint` | `unix:/run/ymir-runtime/runtime.sock`（image 已設預設） |
| `VibeMaker__Runtime__Remote__Token` | 與 RuntimeHost 完全相同 |
| `VibeMaker__Harness` | **`Pi`**，不要把 Scripted 當真 Agent 上線 |
| `VibeMaker__LiteLlm__BaseUrl` | `http://127.0.0.1:4000`（API 管理／發 key 路徑） |
| `VibeMaker__LiteLlm__MasterKey` | 與 LiteLLM master key 相同，只到 API，不到 Agent |
| `VibeMaker__Pi__ModelBaseUrl` | **`http://litellm:4000/v1`**（Agent 路徑，與 API 管理路徑不同） |
| `VibeMaker__Pi__ModelId`／`VibeMaker__Models__N__Id` | 和 LiteLLM `model_name` 完全相同；只列準備好的模型 |
| `VibeMaker__LiteLlm__AllowedModels__N` | 若有明確設定，必須同步上面相同的 alias；不要留下舊模型或 fake-model |
| `Ymir__Auth__LocalAccounts__Enabled` | 首次本機管理員 bootstrap 時 `true` |
| `Ymir__PublicEdge__Enabled` | **`true`** |
| `Ymir__PublicEdge__PublicHostname` | 例如 `ymir.example.com`，不是完整 URL |

API image 內含 Angular build、Production 環境、loopback listen、keyring 路徑；不要啟動 `ng serve`／`dotnet run` 供公網使用。模型的圖片、thinking、provider 與價格需與實際上游一致；額外模型不會自動取得管理端開放權限。

```bash
runtime_gid=$(getent group ymir-runtime | cut -d: -f3)
test -n "$runtime_gid"
sed -e "s/<ymir-runtime gid>/$runtime_gid/" -e "s|^Image=.*|Image=$API_IMAGE|" \
  deploy/api/ymir-api.container | sudo tee /etc/containers/systemd/ymir-api.container >/dev/null
sudo systemctl daemon-reload
sudo systemctl start ymir-api.service
curl --fail http://127.0.0.1:5080/health
curl --fail http://127.0.0.1:5080/api/auth/providers
```

預期 health 為 `Healthy`。API 必須保留非 root uid 1654、唯讀 rootfs、drop capabilities、no-new-privileges 與 socket 群組；不能掛 Podman socket 或整個 workspace。不要使用完整 `podman inspect` 或 `systemctl show ... Environment` 輸出來分享除錯，裡面可能有 secret。

## 9. 首次管理員與 SSO

若先用本機帳號，以 stdin 建立管理員，避免程式目前的 `Console.ReadLine` 在互動 TTY 顯示密碼：

```bash
sudo -v
read -r -s -p '首次管理員密碼（至少12字元）: ' ADMIN_PASSWORD
printf '\n'
read -r -s -p '再次輸入: ' ADMIN_CONFIRM
printf '\n'
printf '%s\n%s\n' "$ADMIN_PASSWORD" "$ADMIN_CONFIRM" | \
  sudo podman exec -i ymir-api dotnet Ymir.Api.dll create-local-admin admin
admin_result=$?
unset ADMIN_PASSWORD ADMIN_CONFIRM
test "$admin_result" -eq 0
```

只在首次建立且成功後繼續；密碼不寫 shell history 或命令列參數。先用 SSH port forwarding 在管理者自己的瀏覽器開啟 `http://127.0.0.1:5080` 登入，完成首次強制改密碼，確認角色是 Admin。不要為此把 API 綁到 `0.0.0.0` 或公開 dev login。

Entra 設定依 [Entra 指南](entra-id.md)：

- 以你的 tenant／client id／secret 取代範本；管理員 role 依 app role 指派，不用 email 字尾判斷。
- Web redirect URI 必須是 `https://ymir.example.com/signin-oidc`，不能是 localhost 或 HTTP。
- 可透過部署 secret 或管理介面加密保存的設定配置。備份 keyring；不要把 client secret 放進前端。
- Google SSO 如需要再依 [Google 指南](google-login.md) 配置 client 及 `AllowedDomains`／`AllowedEmails` 白名單；沒有白名單不可啟用。

## 10. 最後才開啟 Cloudflare Tunnel

在 Cloudflare 建立 Named Tunnel，Public Hostname 設為你的 hostname，Service **`http://127.0.0.1:5080`**。這只適用本指南的新部署；已有 nginx 的主機保留原來源，不套這個設定。

```bash
sudo install -o ymir -g ymir -m 644 deploy/cloudflared/ymir-cloudflared.container /home/ymir/.config/containers/systemd/
ymir_run systemctl --user daemon-reload
```

以本機登入的 Admin 到「管理 → 系統設定 → 對外連線」套用 Tunnel token；RuntimeHost 把 token 寫到 `/home/ymir/.config/ymir/cloudflared.env`（600），並啟動 user Quadlet。也可由管理者安全編輯此檔、填入 `TUNNEL_TOKEN=...`，設定 owner ymir／mode 600 後啟動 `ymir_run systemctl --user start ymir-cloudflared.service`。兩種方式擇一，勿啟動重複 connector。

token 不能經 `--token 值` 放入程序參數，也不能放入 API env、Agent 或 SQL。等到 Tunnel 確實註冊成功再測公開頁：unit active 不代表 edge 已就緒，啟動瞬間 530 可在有限時間內重試，但持續錯誤不能算安裝成功。

```bash
ymir_run systemctl --user status ymir-cloudflared.service
curl --fail --retry 10 --retry-all-errors --retry-delay 3 https://ymir.example.com/login
```

請先替換測試網域。公開登入頁應 200、HTTPS forwarding 與 HSTS 正確；公開 `/health`、`/alive`、`/api/dev/login` 不得變成可用的開發／健康入口。登入 challenge 的 callback 必須是你的 HTTPS 網域。完整檢查見 [Tunnel 指南](cloudflare-tunnel.md)。

## 11. 必做驗收與可選功能

### 不可只看服務綠燈

- [ ] SQL／PostgreSQL 資料落在 named volume，不在會隨 container 消失的 writable layer；只有 1433／4000／5080 的 loopback listen，PostgreSQL 不公開。
- [ ] RuntimeHost health `ok`，API health `Healthy`；公開登入頁 200，沒有公開 Development 登入。
- [ ] 真實本機／SSO 管理員與一般使用者登入成功、角色正確，未授權人無法進管理功能。
- [ ] 一次真 Pi 對話：串流文字、工具執行、產生檔案、下載、附件寫入皆正常；第一次真模型測試可能計費。
- [ ] Agent 在 `ymir` rootless store，具預期 CPU／RAM／pids／唯讀限制，不拿到 LiteLLM master key、DB／SSO／Tunnel secret。
- [ ] 關閉測試使用者 internet 能力後，受限 network 仍能使用 LiteLLM，但不能連外；權限更新在下次執行生效，不以管理 UI 狀態取代網路驗收。
- [ ] API 重建後登入及加密設定仍可用；工作檔、Pi session、LiteLLM 用量保留。
- [ ] 經批准的維護時段重開機，SQL、PostgreSQL、LiteLLM、bridge、rootless proxy、RuntimeHost、API、Tunnel 都恢復；user Quadlet 依 linger 恢復。
- [ ] 完成一次異機／測試機還原演練，記錄 RPO／RTO。備份不是成功建立檔案就等於可還原。

有 `cgroup.procs Permission denied`／exec 126 時，先看 [RuntimeHost cgroup 說明](../../deploy/runtime-host/README.md#ubuntuagent-exec-的-cgroup-權限錯誤)，確認主機／工具適用條件並在測試環境驗證；禁止 privileged、全面 chmod cgroup、停用隔離或使用舊正式主機專用修復器直接硬套。

### 基本對話通過後再逐一開放

| 功能 | 額外前置與指南 |
|---|---|
| OneDrive | 管理員開放能力、Microsoft delegated permissions／callback、個人授權；見 [OneDrive](onedrive.md)。連結帳號不等於讓 Agent 存取：使用者要在外掛市集自己打開「讓 Agent 使用」（ADR-0030），且需要平台 MCP gateway 設定 `McpGateway__ApiUrl` |
| 個人 MCP | 管理員開放 MCP／internet、個人註冊與最小權限 token；見 [連接與工具](personal-connections.md)，自己的 MCP token 會送入個人程序環境，不等同 OneDrive 授權只留 API |
| 平台 MCP | [獨立 Gateway](../../deploy/mcp/README.md)、服務目錄、簽章 key、存取清單及受限網路可達性；OneDrive 外掛另需 gateway 的 `McpGateway__ApiUrl`（gateway 連得到的 API 位址）；不要直接把內網服務憑證交給 Agent |
| 知識庫 | 設定真實 embedding 模型、API 自己的持久化 Knowledge root／volume 與模型使用權限；原始文件不放入 Agent mount |
| 小工具／網站託管 | [SiteHost](../../deploy/sites/README.md)、獨立於登入站的 hostname／wildcard DNS、持久化 sites volume、私人網站 ticket；不要共用登入 origin 或省略存取控制 |
| 系統資源／安全回收 | [MaintenanceHost](../../deploy/maintenance/README.md)、專用 socket／token／鎖目錄，先 dry run 與保護分類，再決定是否啟用回收；不可用全 store prune 代替 |

選用服務未配置時不應宣稱功能已可用；不要提前把不存在的 socket／資料目錄加到 API mount。參數及 mount 以各服務文件為準。

## 12. 更新與回復

這節取代首次安裝的覆蓋操作。**不要重新 useradd、重建資料 volume、複製空的 keyring 或以範本覆蓋原 secret。**

1. 在乾淨部署 checkout fetch、選定 CI 通過的 commit；保留當前 SHA、API image ID、Agent image ID、RuntimeHost release、Quadlet／drop-in／有效配置的私有備份。不要把本機未提交內容打進部署。
2. 在維護時段前建置新 API image、RuntimeHost 到新的版本目錄；記錄 hashes。只有 Agent image 本身異動才另外規劃換版，保留現有 workspace／agent-state，不刪除舊 image。
3. 盤點所有模組待套 migration，先在隔離測試 DB 驗證。若資料庫版本／history／schema 與預期不符就停止，不用「直接插 history」跳過 schema gate。
4. 取 `/run/ymir-lifecycle/deploy.lock` 的獨佔 `flock`，涵蓋備份、migration、切換、驗證與失敗回復，避免和維護回收互相干擾。
5. 暫停本架構的 Tunnel／公開入口，排空 Queued／Running；不能只停止入口就假設工作已結束。停止 API／RuntimeHost 後再做一致性備份。現有 nginx 拓樸依自己的 service 清單處理，不照抄本文服務名稱。
6. SQL 做新的 `COPY_ONLY, CHECKSUM` 備份並 `RESTORE VERIFYONLY ... WITH CHECKSUM`，匯出到容器外；同時保存 keyring、workspace、knowledge／sites、LiteLLM PostgreSQL，以及加密保管的部署 secret／salt。每次獨立命名，不覆蓋上次備份；權限私有，產生 checksum，離機加密保存。詳見 [備份還原指南](backup-restore.md)。
7. 套用已檢閱的兩模組 migration，檢查 exit code、history 與 schema，再切 API `Image=` 及 RuntimeHost drop-in 到固定版本，`daemon-reload` 後依依賴啟動。
8. 確認 runtime／API／模型路徑、本機 health、精確 image ID、安全設定及原有效 secret 都保留。掛載清單按 Destination 排序後逐欄比較，不能因 Podman 清單順序變了就當設定變更，也不能因此忽略 Source／RW。再啟動公開入口，等待有限的 Tunnel 就緒重試、驗證登入頁及真對話。
9. **失敗回復有兩種**：若新增 schema 與舊程式相容，可恢復舊 image／RuntimeHost／配置，保留已套的 additive migration 與資料；若不相容，維持入口停止，依事前審查的還原計畫恢復成套 SQL＋keyring＋必要檔案。不要盲目執行 EF down migration、刪表或 `RESTORE WITH REPLACE`；這會丟失備份後的資料，必須先取得維運決策。
10. 做重開機／登入／舊對話／檔案／用量／真 Pi SSE 驗收，記錄結果與尚待人工驗收的項目。只部署成功不代表 OneDrive、SSO 或第三方 MCP 也已驗收。

## 13. 排錯順序與文件索引

| 現象 | 先查哪裡；不要怎麼做 |
|---|---|
| unit 不存在 | Podman／Quadlet 版本、檔名、root 與 user store、`daemon-reload` 及 generator 訊息；不要 enable generated unit |
| image／network 找不到 | 確認是在 root 還是 `ymir` store 建立；不要把 API 移到 `ymir` store |
| runtime socket 401／permission denied | token 是否相同、GID、目錄／socket 權限、API supplementary group、RuntimeDirectory inode；不要 chmod 777 |
| SQL Msg 1934 | sqlcmd `-I`／必要 SET options 與 filtered index；不要略過錯誤或停用 index |
| health 失敗／模型無法用 | 依序 SQL readiness、LiteLLM／PostgreSQL、master key、API 管理 URL、Agent proxy URL、alias／模型權限；不要把 provider key 塞入 Agent |
| 公開 530／502 | 本機 health、Tunnel 註冊、來源 port／hostname、必要代理 headers；不要改成公開 Development API |
| 工具／附件 exec 126 | RuntimeHost cgroup／rootless user scope；依對應說明驗證 stdin 與 env，不擴權 |
| 長對話 413 | 模型 proxy body-size／buffering，保持 100m 設定；單檔 bind mount 換 inode 後需重建／重啟才能讀到新檔 |
| API native crash | 保留 journal、容器退出碼、dump／版本與資源證據；不要把重啟成功當根因修復，dump 可能含機密 |

日誌也可能含個人資料，僅在受控位置查看；分享前遮蔽 secret、URL token 與檔案內容。不要分享整份 env、SQL 備份、keyring、Tunnel token 或完整 container inspect。

- [API 詳細部署／設定](../../deploy/api/README.md)
- [RuntimeHost／rootless／模型 bridge](../../deploy/runtime-host/README.md)
- [LiteLLM／路由／virtual key／用量](../../deploy/litellm/README.md)
- [Entra](entra-id.md)／[Google](google-login.md)／[Cloudflare](cloudflare-tunnel.md)
- [備份與還原](backup-restore.md)
- [系統架構](../../architecture.md)／[ADR 索引](../adr/README.md)／[開發及驗證命令](../../CLAUDE.md)
