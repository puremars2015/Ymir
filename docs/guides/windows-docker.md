# Windows + Docker Desktop 開發與驗證指南

正式環境的 Agent runtime 是 Rootless Podman；沒有 Linux 主機時，可以在 Windows 上用 Docker Desktop 跑完整流程
（API → Docker container 內的 Pi → 模型）。背景與限制見 [ADR-0005](../adr/0005-docker-for-development.md)。

> Docker 上**驗證不到**：rootless、`keep-id` 檔案擁有者對應、SELinux、正式主機的 cgroups delegation。上線前仍需在 Linux 主機驗證。

## 前置

- Docker Desktop（使用 **WSL 2 backend**）
- .NET 10 SDK、Node 24 LTS、Git
- 以下指令皆在 repo 根目錄的 PowerShell 執行

## 1. 啟動 SQL Server

```powershell
docker run -d --name ymir-sql -p 1433:1433 `
  -e ACCEPT_EULA=Y -e "MSSQL_SA_PASSWORD=Ymir_Dev_Passw0rd!" `
  mcr.microsoft.com/mssql/server:2022-latest
```

## 2. 建置 Agent runtime image

```powershell
docker build -t localhost/ymir/agent-runtime:dev -f runtime/agent/Containerfile runtime/agent
```

## 3. 啟動模型端點（Fake LLM）

Container 透過 `host.docker.internal` 連回 Windows，所以要監聽所有介面：

```powershell
dotnet run --project tests/Ymir.Testing.FakeLlm -- --urls http://0.0.0.0:5199
```

接真正的 LiteLLM 時，把下一步的 `ModelBaseUrl` 換成 LiteLLM 位址即可。

## 4. 啟動 API（Docker runtime + 真實 Pi）

```powershell
$env:VibeMaker__Runtime__Provider = "Docker"
$env:VibeMaker__Runtime__WorkspaceRoot = "C:\ymir-workspaces"
$env:VibeMaker__Harness = "Pi"
$env:VibeMaker__Pi__ModelBaseUrl = "http://host.docker.internal:5199/v1"
$env:VibeMaker__Pi__ModelId = "fake-model"
dotnet run --project src/Ymir.Api
```

- 第一次送訊息時會自動建立該 Workspace 的 container（`ymir-ws-<workspaceId>`），之後重複使用。
- `Local` runtime 在 Windows 上不可用（需要 `sh`），API 會直接拒絕啟動並提示改用 `Docker`。

## 5. 啟動前端並操作

```powershell
cd web
npm ci
npm start
```

開啟 <http://localhost:4200>，以任意帳號登入 → 建立 Workspace → 建立對話 → 送出 `[create-file] 幫我建立檔案`。

## 6. 驗證清單

| # | 項目 | 怎麼確認 |
|---|---|---|
| 1 | Agent 在 Workspace 建立檔案 | `C:\ymir-workspaces\<workspaceId>\workspace\hello.txt` 出現 |
| 2 | 同一對話延續前文 | 再送一則訊息，回覆為「收到第 2 則使用者訊息」 |
| 3 | Container 重建後仍記得前文 | `docker rm -f ymir-ws-<workspaceId>` 後再送訊息，回覆的則數持續增加 |
| 4 | 取消 | 送出 `[slow] 慢慢講`，執行中按「停止」 |
| 5 | 安全設定 | `docker inspect ymir-ws-<workspaceId> --format "{{.Config.User}} {{.HostConfig.ReadonlyRootfs}} {{.HostConfig.CapDrop}} {{.HostConfig.SecurityOpt}}"` 應為 `1000:1000 true [ALL] [no-new-privileges]` |
| 6 | 資源限制 | `docker stats ymir-ws-<workspaceId>` 顯示 MEM LIMIT 2GiB |
| 7 | Workspace 隔離 | 另建一個 Workspace，`docker exec ymir-ws-<另一個id> ls /workspace` 看不到 `hello.txt` |

只想驗證 runtime（不開前端）時，可以用 PoC：

```powershell
dotnet run --project spikes/pi-rpc-poc -- --runtime Docker --workspace-root C:\ymir-workspaces `
  --llm-url http://host.docker.internal:5199/v1 --model fake-model --prompt "[create-file] 建檔" --prompt "第二句"
```

## 效能建議

`C:\` 上的目錄透過 Docker Desktop 的檔案分享掛進 container，Agent 執行大量 `npm install` 時會比較慢。
需要更好的效能、或想更接近正式環境時，可以改在 **WSL 2 的 Ubuntu 裡**開發：

- 在 WSL 裡執行 API 與 Docker CLI，`WorkspaceRoot` 放在 Linux 檔案系統（例如 `~/ymir-workspaces`）。
- 也可以在 WSL 2 Ubuntu 安裝 Podman，以 `Provider=Podman` 跑 **rootless Podman**——這是目前不需要額外主機、最接近正式環境的驗證方式
  （若要驗證資源限制，需在 `/etc/wsl.conf` 啟用 systemd，並依 [`runtime/agent/README.md`](../../runtime/agent/README.md#部署主機需求sprint-0-驗證後的發現) 設定 cgroups delegation）。

## 疑難排解

| 症狀 | 原因 / 處理 |
|---|---|
| `execution.failed`：`RUNTIME_START_FAILED` | 找不到 image：確認第 2 步已建置 `localhost/ymir/agent-runtime:dev` |
| Agent 回覆模型錯誤 | Container 連不到模型：確認 Fake LLM 監聽 `0.0.0.0`、`ModelBaseUrl` 用 `host.docker.internal` |
| `Permission denied`（`/agent-state`） | 掛載目錄擁有者不是 uid 1000；API 會在建立 container 前自動修正，若手動建立過目錄請刪除後重試 |
