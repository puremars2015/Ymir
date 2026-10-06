# ADR-0008：API 放進容器，Agent runtime 由主機上的 runtime host 管理

- 狀態：已採納（補充 ADR-0003 的 Runtime / Harness 分離、ADR-0005 的 runtime provider、ADR-0006 的部署拓樸）
- 日期：2026-10-06

## 背景

使用者決定把 .NET API 放進容器，只讓它存取主機上的特定資料夾。

原本的設計中，API 是主機上的程序，由 `ContainerRuntimeManager` 直接執行 `podman` / `docker` CLI，建立每位使用者的 Agent container（ADR-0007）。
API 容器化後，可行的做法有三種：

| 做法 | 評估 |
|---|---|
| 把 rootless Podman 的 socket 掛進 API 容器 | API 被攻破時，攻擊者可以用該帳號建立任意 container、掛載它能讀寫的任何路徑。違反「不得掛載 container runtime socket」的紅線 |
| 在 API 容器內再跑 Podman（巢狀） | 必須放寬容器權限（user namespace、`/dev/fuse`），不採用 |
| **把 runtime manager 拆成主機上的獨立服務（採用）** | API 只能提出受限的請求。SA §16 已預留這個方向 |

## 決策

1. **新增主機服務 `Ymir.RuntimeHost`**（`src/Ymir.RuntimeHost/`）：
   - 執行方式：以 rootless Podman 的專用帳號 `ymir` 執行（systemd，`deploy/runtime-host/`）。
   - 內部沿用既有的 `ContainerRuntimeManager`、`ContainerCommandBuilder`。安全規則與 `ContainerCommandBuilderTests` 不變。
   - **只提供兩種能力**：
     - 為 user X 確保 / 啟動 / 停止 / 刪除 runtime；
     - 在 user X 的 runtime 內執行程序。
   - **唯一的識別輸入是 user id（Guid）**，以下全部由 runtime host 自己的設定決定，沒有任何端點接受這些參數：
     - image、掛載、資源限制、network；
     - host 路徑 `{WorkspaceRoot}/users/{userId}`。
2. **傳輸與認證**：
   - **傳輸**：
     - 預設監聽 Unix socket `/run/ymir-runtime/runtime.sock`。目錄 750，socket 660（`ymir:ymir-runtime`），不允許 other 存取。
     - 也可以改用只限 loopback 的 `http://127.0.0.1:port`。非 loopback 位址或 https 一律拒絕啟動。
   - **認證**：
     - 每個請求都要 `Authorization: Bearer <token>`，token 至少 32 字元，以 SHA-256 後固定時間比較。
     - 只有 `/health` 例外，回應內容只有 `ok`。
     - token 只放在部署環境的 secret（`/etc/ymir/runtime-host.env`、`deploy/api/.env`），不得進版控。
3. **程序規格驗證**：client 與 server 都檢查（`RuntimeHostProtocol.Validate`），擋下以下輸入：
   - 以 `-` 開頭的執行檔，避免被 `podman exec` 當成選項；
   - 不合法的環境變數名稱；
   - NUL 字元；
   - 參數數量上限；
   - 不在 `RuntimePaths` 允許清單內的工作目錄。

   程序本身在使用者的 container 內執行，隔離邊界仍是 container（ADR-0007）。
4. **stdio 轉送**：
   - `GET /v1/users/{userId}/runtime/process` 升級為 WebSocket：
     - 第一個 text frame 是啟動訊息；
     - 之後 binary frame 是 stdin / stdout；
     - text frame 是控制訊息：`closeStdin`、`kill`、`exit`（含 exit code 與 stderr 尾端）。
   - API 斷線時，runtime host 先關閉 stdin，等 5 秒後強制結束程序，不留下孤兒程序。
   - 環境變數的值（例如 LiteLLM virtual key）不寫入 log，也照舊只以名稱傳給 `podman exec --env NAME`。
5. **API 端新增 `VibeMaker:Runtime:Provider=Remote`**（`RemoteRuntimeManager`）：
   - 實作同一個 `IAgentRuntimeManager`。`PiAgentHarness`、`ExecutionRunner` 不需要知道 runtime 在本機或遠端。
   - 任何環境都可以使用。runtime host 本身不允許設定成 `Remote`，避免轉給另一個 runtime host。
6. **API 容器**（`src/Ymir.Api/Containerfile`、`deploy/api/`）：
   - **網路**：host network，只綁主機的 `127.0.0.1:5080`，ADR-0006 的 cloudflared 與 `X-Forwarded-*` 信任設定不變。
   - **權限**：
     - 唯讀 root filesystem、drop 全部 capabilities、`no-new-privileges`；
     - 以 .NET image 內建的非 root 使用者（uid 1654）執行；
     - 以 `group_add` 取得 `ymir-runtime` 權限。
   - **只掛載兩個主機資料夾**：
     - `/run/ymir-runtime`（runtime host 的 socket）；
     - Data Protection 金鑰目錄（新設定 `Ymir:DataProtection:KeysPath`）。
   - 不掛載任何 container runtime socket，也不掛載使用者 workspace。
7. **開發環境不變**：
   - Development 的 API 仍在主機執行，使用 `Local` runtime。
   - Windows 上使用 `Docker` provider（ADR-0005）。
   - Runtime host 可以在本機以 Development 啟動，用來驗證整條流程。

## 影響

- **部署**：多一個主機服務（runtime host）要安裝與監控。正式主機需要 .NET 10 runtime，或改用 self-contained publish。
- **安全邊界**：API 被攻破時，攻擊者最多能在既有使用者的 Agent container 內執行程序，這本來就是 API 的職責範圍；但無法建立任意 container、掛載主機路徑或提權。
- **API 用不同帳號的 rootless Podman 執行時**：supplementary group 會被 user namespace 對應掉，需要 `--group-add keep-groups`（見 `deploy/api/README.md`）。建議以 Docker 或 rootful Podman 跑 API 容器。
- **不得用 `ymir` 帳號跑 API 容器**：等於讓 API 直接擁有 rootless Podman。
- **runtime id 的對應**：API 端只在記憶體中保存 runtime id → user id 的對應。API 或 runtime host 重新啟動後，下一次 execution 的 EnsureRuntime 會重建對應，與原本行為相同。
- **WebSocket 轉送的成本**：每個 exec 多一層轉送，相較於模型呼叫的延遲可以忽略。
- **之後的工作**：
  - runtime host 可以接手 idle stop 與 reconciliation（SA §15），也可以擴充成多台主機。
  - 目前 kill 的是 host 上的 `podman exec` 程序，container 內的程序可能殘留（既有問題，與本 ADR 無關），之後可由 runtime host 在 container 內結束對應的程序補強。
