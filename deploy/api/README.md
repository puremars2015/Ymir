# Ymir API container

各 container 所需 script、首次安裝／日常更新順序及實際操作紀錄，見[部署操作索引](../../docs/guides/container-deployment.md)與[部署流水帳](../../docs/deployments/README.md)。

架構決策見 [ADR-0008](../../docs/adr/0008-containerized-api-runtime-host.md)。Image 由 [`src/Ymir.Api/Containerfile`](../../src/Ymir.Api/Containerfile) 建置，內容是 API 加上 Angular build（同源，ADR-0002）。

**用哪個 container engine 跑 API**（使用者決定）：

| 環境 | Engine | 設定檔 |
|---|---|---|
| Linux 正式主機 | **rootful Podman** + systemd Quadlet | [`ymir-api.container`](ymir-api.container)、`/etc/ymir/api.env`（範本 [`.env.example`](.env.example)） |
| Windows 開發機 | **Docker Desktop**（只用於開發與驗證） | [`compose.windows.yml`](compose.windows.yml)、`.env.windows`（範本 [`.env.windows.example`](.env.windows.example)） |
| （備用）Linux + Docker | Docker Compose | [`compose.yml`](compose.yml)、`.env` |

- Agent container **不在**這裡：它們由主機上的 runtime host 管理（[`deploy/runtime-host`](../runtime-host/README.md)）。
  - Linux 上，runtime host 用 `ymir` 帳號的 **rootless** Podman 執行。
  - API 容器用 rootful Podman，兩者互不相干。
- **不要用 `ymir` 帳號跑 API 容器**：API 被攻破時，等於直接拿到 rootless Podman。

## 容器能碰到的主機資源（Linux）

| 主機 | 容器內 | 用途 |
|---|---|---|
| `/run/ymir-runtime/` | `/run/ymir-runtime/` | runtime host 的 Unix socket。連線需要同時具備 socket 的 group 權限（`--group-add`）與 token |
| `/srv/ymir/api/keys/` | `/var/lib/ymir/keys/` | Data Protection 金鑰：容器重建後，登入 cookie 仍然有效 |
| `127.0.0.1:5080`（host network） | 同左 | 只綁主機的 loopback，外部只能經由 cloudflared 進來（ADR-0006） |

- **不掛載**：
  - container runtime socket（`podman.sock` / `docker.sock`）；
  - 使用者的 workspace；
  - cloudflared 憑證。
- **容器本身的限制**：
  - 檔案系統唯讀、drop 全部 capabilities、`no-new-privileges`；
  - 以 .NET image 內建的非 root 使用者（uid 1654）執行。

## Linux：rootful Podman + Quadlet

### 首次部署前置準備

以下指令在 Linux 的 repo 根目錄執行。這是 ADR-0008 的正式部署方式：API 與 Angular 在 rootful Podman 容器內，API 程序使用非 root 的 uid 1654；RuntimeHost 是主機服務，以 `ymir` 帳號管理 rootless Agent 容器。

以下流程已對照程式與部署範本；完整 rootful API＋rootless Agent 拓樸仍需在目標 Linux 主機驗收，包含 socket 群組、模型連線與重開機恢復。

- Linux 主機需有運作中的 systemd、支援 Quadlet 的 Podman（專案最低要求 4.4）、.NET 10 SDK 與 ASP.NET Core 10 runtime。SDK 用於 publish 與 migration，RuntimeHost 執行時需要 ASP.NET Core runtime。
- 先依 [RuntimeHost 安裝指南](../runtime-host/README.md#安裝linux需要-root) 建立 `ymir` 帳號、`ymir-runtime` 群組、subuid/subgid、linger 與 `/srv/ymir/workspaces`，以 `ymir` 帳號建置 Agent image，發布並啟動 RuntimeHost。API image 與 Agent image 分別位於 root 與 `ymir` 的 Podman 儲存區。
- SQL Server、[LiteLLM 與其 PostgreSQL](../litellm/README.md) 必須先啟動。依實際部署填入連線埠；API 的 loopback 位址與 Agent 容器內的模型位址不同，兩邊都需能連線。
- 從既有 Windows 部署遷移時，先依 [備份與還原指南](../../docs/guides/backup-restore.md) 搬移資料庫、workspace、agent-state 及與資料庫配套的 Data Protection 金鑰；新建空金鑰目錄不能取代原金鑰。
- 首次安裝才複製 `.env.example`；更新既有部署時編輯原設定，保留 token、登入與模型設定。

1. **建置 image**：在 repo 根目錄執行，image 會放在 root 的儲存區。
   ```bash
   sudo podman build -f src/Ymir.Api/Containerfile -t localhost/ymir/api:latest .
   ```
2. **設定與金鑰目錄**：
   ```bash
   sudo install -d -o 1654 -g 1654 -m 700 /srv/ymir/api/keys
   sudo install -d -m 755 /etc/ymir
   sudo install -m 600 deploy/api/.env.example /etc/ymir/api.env
   sudoedit /etc/ymir/api.env   # 填入連線字串、runtime host token、LiteLLM master key；YMIR_* 兩行只有 compose 用得到，可刪除
   ```
   - `VibeMaker__Runtime__Remote__Token` 必須與 `/etc/ymir/runtime-host.env` 的 `RuntimeHost__Token` 相同。
   - 填入 SQL Server 連線字串、LiteLLM master key／模型路由、企業登入與公開網域設定，替換所有 `<...>` 範例值。
   - API image 預設 `Provider=Remote`；RuntimeHost 設定 `Provider=Podman`。
3. **安裝 Quadlet unit**：把 gid 換成 `ymir-runtime` 的實際值；此步只產生 unit，先不要啟動 API。
   ```bash
   sudo install -d /etc/containers/systemd
   runtime_gid=$(getent group ymir-runtime | cut -d: -f3)
   test -n "$runtime_gid" || { echo '請先建立 ymir-runtime 群組'; exit 1; }
   sed "s/<ymir-runtime gid>/$runtime_gid/" deploy/api/ymir-api.container \
     | sudo tee /etc/containers/systemd/ymir-api.container >/dev/null
   sudo systemctl daemon-reload
   ```
4. **Migration（必須在 API 啟動前完成）**：Production 不會自動 migrate。兩個設計階段 Factory 使用 `ymir_design` 開發連線，因此必須以 `--connection` 指定正式資料庫。以下使用 Bash 的隱藏輸入；輸入與 `/etc/ymir/api.env` 相同的目標資料庫連線字串（migration 帳號需具備 schema 修改權限）。既有資料庫先備份；兩個指令都成功後再啟動 API。
   ```bash
   dotnet tool restore
   read -r -s -p '正式 SQL Server 連線字串: ' YMIR_MIGRATION_CONNECTION
   echo
   dotnet ef database update \
     --project src/Platform/Ymir.Platform.Infrastructure \
     --context PlatformDbContext --connection "$YMIR_MIGRATION_CONNECTION"
   dotnet ef database update \
     --project src/Modules/VibeMaker/Ymir.VibeMaker.Infrastructure \
     --context VibeMakerDbContext --connection "$YMIR_MIGRATION_CONNECTION"
   unset YMIR_MIGRATION_CONNECTION
   ```
5. **啟動與驗證**：RuntimeHost 的 systemd unit 已安裝，SQL Server 與 LiteLLM 已就緒後執行：
   ```bash
   sudo systemctl enable --now ymir-runtime-host.service
   sudo systemctl start ymir-api.service
   sudo systemctl status ymir-runtime-host.service ymir-api.service --no-pager
   sudo -u ymir curl -f --unix-socket /run/ymir-runtime/runtime.sock http://localhost/health
   curl -f http://127.0.0.1:5080/health
   curl -f http://127.0.0.1:5080/api/auth/providers
   curl -f -o /dev/null http://127.0.0.1:5080/login
   ```
   預期 RuntimeHost 回 `ok`、API health 回 `Healthy`、登入方式與登入頁回 HTTP 200。接著以實際使用者登入並送出訊息，驗證 Agent 建立、模型連線、SSE、檔案保存與取消。

### 日常啟動、開機與更新

已完成上述安裝時，手動啟動只需：

```bash
sudo systemctl start ymir-runtime-host.service
sudo systemctl start ymir-api.service
```

服務交由 systemd 管理，關閉終端不會停止服務。RuntimeHost 使用 `enable --now` 設定開機啟動；API Quadlet 的 `[Install] WantedBy=multi-user.target` 會在 unit 產生時建立開機依賴，**不要對 Quadlet 產生的 `ymir-api.service` 執行 `systemctl enable`**。詳見 [Podman Quadlet 官方說明](https://docs.podman.io/en/latest/markdown/podman-systemd.unit.5.html#enabling-unit-files)。SQL Server、LiteLLM／PostgreSQL 與 Tunnel 的開機啟動需各自設定，API unit 不會替它們安裝或啟動服務。

更新 API 時，在 repo 根目錄重新建置；若有新 migration，先備份資料庫、停止 API 並執行上方第 4 步，再啟動新版：

```bash
sudo podman build -f src/Ymir.Api/Containerfile -t localhost/ymir/api:latest .
sudo systemctl restart ymir-api.service
curl -f http://127.0.0.1:5080/health
```

安裝主機維護服務（ADR-0025，[deploy/maintenance](../maintenance/README.md#主機維護服務ymir-maintenance-hostadr-0025)）後，建置與切換 image 的步驟要以部署鎖包住（`sudo flock /run/ymir-lifecycle/deploy.lock …`），維護服務在這段期間不會刪除 image。

RuntimeHost 的更新需另外 publish 至 `/opt/ymir/runtime-host` 並重啟 `ymir-runtime-host.service`；先確認沒有執行中的 Agent 工作。

更新時的注意事項（2026-10-14 實際部署時遇到）：

- **Quadlet 以 image ID 固定版本時**（`Image=<64 碼 ID>`），重建 tag 不會換版；以新 commit 為 tag 建置後，把 `Image=` 換成 `sudo podman image inspect localhost/ymir/api:<commit> --format '{{.Id}}'` 的值，再 `sudo systemctl daemon-reload` 並重啟。先備份原 Quadlet，失敗時還原即可回到舊版。
- **主機上沒有 .NET SDK 可用於 root 時**，可改以 `dotnet ef migrations script --idempotent --project <模組 Infrastructure 專案> --context <DbContext> -o <檔名>.sql` 產生 SQL（不連資料庫），在 SQL Server container 內用 `sqlcmd ... -b -I -d ymir -i <檔名>.sql` 套用。**必須加 `-I`**（`QUOTED_IDENTIFIER ON`）：資料表有 filtered index，少了它 `CREATE INDEX`、`INSERT` / `DELETE` 會失敗（Msg 1934）。部署器另明確設定 `ANSI_NULLS`、`ANSI_PADDING`、`ANSI_WARNINGS`、`ARITHABORT`、`CONCAT_NULL_YIELDS_NULL` 為 ON，`NUMERIC_ROUNDABORT` 為 OFF。history 資料表是 `platform.__ef_migrations_history` 與 `vibemaker.__ef_migrations_history`。
- **容器安全設定比對**：目前 Quadlet 明確使用 `1654:1654`，image 使用 `1654`；兩者均為預期的非 root 身分，不能把指定 GID 誤判為 root。Podman 的 `inspect.Mounts` 清單順序不保證穩定，應按 `Destination` 排序後逐欄比較；不得忽略 Source、RW 或其他掛載欄位，也不得因順序差異直接略過檢查。
- **Tunnel 就緒不等於 unit active**：重啟 cloudflared 後，edge 註冊尚未完成時公開網址可能暫時回 530。先驗證本機 API health 與服務狀態，再以有上限的重試確認公開登入頁 200；逾時才回復舊版，不得把持續錯誤當成功。保留每次嘗試的資料庫／金鑰／工作目錄備份及診斷，勿覆蓋上一份備份。

### 公開入口與故障檢查

API 與前端同源，由容器提供，不需另外啟動 Angular dev server 或 nginx。API 預設只監聽 `127.0.0.1:5080`；外部瀏覽器需經 [Cloudflare Tunnel](../../docs/guides/cloudflare-tunnel.md)。Linux 的 Tunnel origin 設為 `http://127.0.0.1:5080`；使用 rootless cloudflared Quadlet 時，依 [RuntimeHost Tunnel 安裝步驟](../runtime-host/README.md#cloudflare-tunnel-由管理介面設定adr-0010) 設定 user service、token 與 hostname。

```bash
sudo journalctl -u ymir-runtime-host.service -u ymir-api.service -n 100 --no-pager
sudo podman ps -a
sudo -u ymir ls -l /run/ymir-runtime/runtime.sock
```

- API unit 找不到：確認 `/etc/containers/systemd/ymir-api.container` 已安裝、GID 已替換，且執行過 `daemon-reload`；檢查 Podman 版本與 Quadlet generator 的錯誤。
- API health 不健康：查看 journal 中 SQL Server、LiteLLM 與 RuntimeHost 的檢查結果。
- RuntimeHost 401：核對兩份 env 的共用 token。
- socket `Permission denied`：核對 socket／目錄權限與 API supplementary group；可用 `sudo podman exec ymir-api id` 檢查。
- rootful API 與 rootless Agent 使用不同的 Podman 儲存區；`sudo podman ps` 不會列出 `ymir` 的 Agent。

`start-ymir.sh` 只適用下節的既有 **Docker** 部署，不能用來啟動此 Podman Quadlet 拓樸；Windows 的 `run-api.ps1` 與 AppData 部署目錄也不適用 Linux。

> **SELinux 主機（Fedora / RHEL）**：
> - 容器（`container_t`）連到 runtime host（`unconfined_service_t`）的 Unix socket 可能被 policy 拒絕。
> - 如果 API log 出現 `Permission denied`，先用 `sudo ausearch -m avc -ts recent` 確認原因，再決定要用自訂 policy 模組或 `SecurityLabelType=`。
> - 這一段在雲端沙箱無法驗證，**待使用者環境確認**。

## Ubuntu：既有 Docker 部署的一鍵啟動

若 Ubuntu 使用本專案的 Linux Docker API 部署（`compose.yml`，API 容器內包含前端）且已安裝 runtime host、SQL Server、LiteLLM 與 Cloudflare Tunnel，可在 repo 根目錄執行：

```bash
sudo bash ./start-ymir.sh
```

前置條件是 Ubuntu 的 systemd、Docker Engine、`curl`、`flock`，已存在的 API／SQL／LiteLLM PostgreSQL／LiteLLM／Tunnel 容器，以及已設定且可用的 `ymir-runtime-host.service`。首次安裝、API image 建置、憑證、資料遷移與 Linux Tunnel host networking 設定需先按各部署文件完成；Windows 的部署路徑與 `run-api.ps1` 不適用於 Ubuntu。

腳本啟動必要服務並等待 runtime host、API readiness 及登入頁；已運行的服務不重啟，不建立容器、不更新程式碼、不寫入憑證，也不啟動 Agent runtime。缺少部署、名稱有歧義、容器 paused／restarting 或健康檢查逾時會回非零結束碼。服務由 Docker／systemd 持續運行，腳本成功後可關閉終端。

預設識別 `ymir-sql`、`ymir-api` 與目前的 LiteLLM／Tunnel 名稱；若預設名稱不存在，除 SQL 外會依唯一的 Compose service label 尋找容器。多套部署或自訂名稱請明確指定，例如：

```bash
sudo env YMIR_SQL_CONTAINER=my-sql YMIR_API_CONTAINER=my-api \
  YMIR_PG_CONTAINER=my-litellm-db YMIR_LITELLM_CONTAINER=my-litellm \
  YMIR_TUNNEL_CONTAINER=my-cloudflared bash ./start-ymir.sh
```

預設 API 埠 `5080`、每階段等待 `120` 秒；可用 `YMIR_API_PORT`／`YMIR_START_TIMEOUT` 調整。自訂 runtime host 可用 `YMIR_RUNTIME_SERVICE`／`YMIR_RUNTIME_SOCKET`，詳見 `bash ./start-ymir.sh --help`。容器仍必須配置 Production／Remote provider 與正確的 loopback 埠及 socket 掛載。

此腳本是 **Docker 版**，不會把既有 Podman 部署改成 Docker。正式 Linux 的 Podman Quadlet 部署仍按上一節與 runtime host 文件使用 systemd。腳本採 LF 行尾；可直接 `bash` 執行，不需要 `chmod`。

## Windows：Docker Desktop（開發 / 驗證）

Docker Desktop 無法把 Windows 上的 Unix socket 掛進 Linux 容器，所以 Windows 的連線方式不同：
- runtime host 在 Windows 上聽 `127.0.0.1:5090`；
- API 容器經 `host.docker.internal:5090` 連線（Docker Desktop 轉送後是 loopback），每個請求仍要 token；
- API 容器只發佈到主機的 `127.0.0.1:5080`。

```powershell
# 1) runtime host（Windows 主機上；Agent container 由 Docker Desktop 執行）。token 用環境變數，避免出現在程序列表
$env:RuntimeHost__Token = "<32 字元以上，例如 openssl rand -hex 32>"
dotnet run --project src\Ymir.RuntimeHost -- --environment Development `
  --RuntimeHost:Listen=http://127.0.0.1:5090 `
  --VibeMaker:Runtime:Provider=Docker --VibeMaker:Runtime:WorkspaceRoot=C:\ymir\workspaces

# 2) API container
cd deploy\api
copy .env.windows.example .env.windows      # 填入同一個 token
docker compose -f compose.windows.yml up -d --build
docker compose -f compose.windows.yml logs -f api
# 瀏覽 http://localhost:5080
```

- SQL Server 與 Fake LLM 照 [Windows 指南](../../docs/guides/windows-docker.md)第 1、3 節在主機上啟動。容器內經 `host.docker.internal` 連線，`.env.windows.example` 已經設定好。
- 範本使用 Development（有免密碼的 dev 登入），**不可對外公開**。在 Windows 驗證 tunnel 時，請改用指南第 4 節的主機上 API。
- 這一段在雲端沙箱無法驗證（沒有 Docker Desktop），**待使用者環境確認**。

## Windows：既有主機 API 部署的一鍵啟動

若目前使用「主機上的 .NET API + Docker 資料庫／模型閘道」部署，可在 PowerShell 執行（只啟動本機服務）：

```powershell
& "C:\Users\sean.ma\Documents\Ymir\start-ymir.ps1"
```

若 PowerShell 的執行原則禁止直接執行腳本，可只對本次程序使用：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\Users\sean.ma\Documents\Ymir\start-ymir.ps1"
```

腳本預設讀取 `%LOCALAPPDATA%\Ymir\deploy\run-api-local-only.ps1`，沿用已部署版本的登入、模型與資料設定，不包含憑證，也不重新建置。其他部署目錄可透過 `-DeployDirectory` 指定；`-ApiPort` 須與該啟動檔的 HTTP 健康檢查埠一致，`-WebUrl` 預設為 `https://localhost:5443`。

本機 launcher 放在部署目錄，不進版控；從既有 launcher 複製並保留帳號、Data Protection、資料庫及模型設定，在所有既有設定之後、啟動 API 之前加入：

```powershell
$env:Ymir__PublicEdge__Enabled = 'false'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5081;https://localhost:5443'
$env:AllowedHosts = 'localhost;127.0.0.1;[::1]'
$env:Ymir__Web__RootPath = Join-Path $PSScriptRoot 'web-local/browser'
```

`web-local/browser` 須含完整 Angular build（含 `index.html`），API 直接提供同源網頁與 SSE。Production 登入 Cookie 需要 HTTPS；先以 `dotnet dev-certs https --check --trust` 確認受信任的 localhost 憑證，沒有憑證時用 `dotnet dev-certs https --trust` 建立。維持 Production 與 Docker runtime，不啟用免密碼 dev 登入。

它會在必要時啟動 Docker Desktop、檢查必要容器存在、依序啟動 SQL Server／LiteLLM PostgreSQL／LiteLLM，並在 API 未執行時以背景程序啟動。**不啟動 nginx 或 Tunnel，也不需要 Tunnel 容器存在**。已執行的服務直接保留；其他程式占用 API 埠時報錯，不會停止它或再啟動 API。完成後可關閉執行腳本的視窗，API 紀錄保存在部署目錄的 `logs\startup-*.log`。

此腳本只啟動既有部署，未部署或容器不存在時顯示錯誤；使用者 Agent 容器由聊天請求建立。確認 launcher 的 `VibeMaker__Runtime__Image` 指定映像存在；若本機映像已清除，在 repo 根目錄執行 `docker build -t <設定中的映像名稱> -f runtime/agent/Containerfile runtime/agent` 補回。它不是上一節 API／runtime host 容器拓樸的啟動方式。

## 維運

- **健康檢查**：
  - `curl http://127.0.0.1:5080/health` 包含資料庫、執行環境（runtime host）、LiteLLM，只回 `Healthy` / `Unhealthy`；
  - 各項細節在「管理 → 總覽 → 服務狀態」；
  - `/alive` 只檢查程序本身。
  - 經由公開網域連進來的 `/health`、`/alive` 一律 404（ADR-0006）。
- **監控指標**（SA §18）：設定 `OTEL_EXPORTER_OTLP_ENDPOINT` 後，以 OpenTelemetry 匯出以下指標，以及每個 execution 的 trace：
  - `ymir.runtimes.active` / `ymir.runtimes.busy`；
  - `ymir.executions.started` / `ymir.executions.finished`；
  - `ymir.execution.duration`；
  - `ymir.runtime.start_failures`。
- **追查一次執行**：
  - log 都帶 `ExecutionId`、`ConversationId`、`UserId`；
  - 稽核紀錄的 correlation id 是該次執行的 trace id。
- **安全標頭**：所有回應都帶 CSP（script 只允許同源）、`X-Frame-Options: DENY`、`Referrer-Policy`、`Permissions-Policy`。
- **備份與保存期限**：見 [docs/guides/backup-restore.md](../../docs/guides/backup-restore.md)。

## 疑難排解

| 症狀 | 原因 / 處理 |
|---|---|
| 送出訊息後顯示「無法啟動執行環境」，API log 有 `Permission denied` / `Connection refused` | socket 不存在，或容器沒有 `ymir-runtime` 權限：確認 runtime host 正在執行（`systemctl status ymir-runtime-host`），且 Quadlet 的 `--group-add` gid 正確（`podman exec ymir-api id` 應包含該 gid） |
| Windows：API log 有 `Connection refused (host.docker.internal:5090)` | runtime host 沒有在 Windows 上執行，或 `Listen` 不是 `http://127.0.0.1:5090` |
| API log 出現 `Runtime host ensure failed with status 401` | `VibeMaker__Runtime__Remote__Token` 與 runtime host 的 `RuntimeHost__Token` 不一致 |
| 容器重建後所有人都被登出 | 金鑰目錄沒有掛載，或不能寫入（擁有者應為 1654） |
| 啟動失敗：`VibeMaker:LiteLlm:MasterKey and BaseUrl are required` | Production 必須設定 LiteLLM（ADR-0004） |
