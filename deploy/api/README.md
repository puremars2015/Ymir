# Ymir API container

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

前提：
- runtime host 已依 [`deploy/runtime-host/README.md`](../runtime-host/README.md) 安裝並啟動；
- SQL Server 與 LiteLLM（[`deploy/litellm`](../litellm/README.md)）已在主機上執行；
- Podman 4.4 以上（支援 Quadlet）。

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
3. **Quadlet unit**：把 gid 換成 `ymir-runtime` 的實際值。
   ```bash
   GID=$(getent group ymir-runtime | cut -d: -f3)
   sed "s/<ymir-runtime gid>/$GID/" deploy/api/ymir-api.container | sudo tee /etc/containers/systemd/ymir-api.container
   sudo /usr/libexec/podman/quadlet -dryrun     # 檢查產生的 podman run 參數
   sudo systemctl daemon-reload && sudo systemctl start ymir-api
   journalctl -u ymir-api -f
   curl -I http://127.0.0.1:5080/               # 200，Angular 首頁
   ```
   - Quadlet 產生的 unit 不需要 `systemctl enable`，`[Install]` 區段會讓它開機自動啟動。
4. **Migration**：Production 不會自動 migrate。第一次啟動前，先在 repo 執行 `dotnet ef database update`，兩個 DbContext 都要跑。
5. **更新**：重新 `podman build`，再 `sudo systemctl restart ymir-api`。

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

若目前使用「主機上的 .NET API + Docker 網頁／資料庫／模型閘道／Tunnel」部署，可在 PowerShell 執行：

```powershell
& "C:\Users\sean.ma\Documents\Ymir\start-ymir.ps1"
```

若 PowerShell 的執行原則禁止直接執行腳本，可只對本次程序使用：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\Users\sean.ma\Documents\Ymir\start-ymir.ps1"
```

腳本預設讀取 `%LOCALAPPDATA%\Ymir\deploy\run-api.ps1`，沿用已部署版本的登入、模型與資料設定，不包含憑證，也不重新建置。其他部署目錄可透過 `-DeployDirectory` 指定；`-ApiPort` 須與該啟動檔的 API 埠一致。

它會在必要時啟動 Docker Desktop、檢查所有容器存在、依序啟動 SQL Server／LiteLLM PostgreSQL／LiteLLM／網頁／Tunnel，並在 API 未執行時以背景程序啟動。已執行的服務直接保留；其他程式占用 API 埠時報錯，不會停止它或再啟動 API。完成後可關閉執行腳本的視窗，API 紀錄保存在部署目錄的 `logs\startup-*.log`。

此腳本只啟動既有部署，未部署或容器不存在時顯示錯誤；不啟動使用者 Agent 容器。它不是上一節 API／runtime host 容器拓樸的啟動方式。對外網址仍需要原有 Cloudflare Tunnel／DNS 設定與網路可用。

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
