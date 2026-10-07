# ADR-0013：OneDrive connector——後端經 Microsoft Graph 同步，Agent 不持有憑證

- 狀態：已採納（2026-10-07 使用者確認；補充 ADR-0007、ADR-0008、ADR-0009、ADR-0010、ADR-0012）
- 日期：2026-10-07

## 背景

[OneDrive Workspace 計畫](../planning/onedrive-workspace-plan.md) 已選定「方法二」：
- 使用者授權自己的 OneDrive；
- Ymir 後端經 Microsoft Graph 與使用者專屬的本機 workspace 同步；
- Agent 仍在自己的 container 操作本機檔案。

2026-10-07 使用者要求插單實作，並決定：

| 項目 | 決定 |
|---|---|
| 首版範圍 | 連結 + 自動同步：執行前下載、執行後上傳；顯示同步狀態，可手動重試 |
| 帳號與權限 | 公司帳號（與 Ymir 登入同一個 Entra 租戶），委派權限 `Files.ReadWrite`：可存取整個 OneDrive，使用者自選根資料夾 |
| 開啟方式 | 管理員開放（ADR-0012 擴充政策新增 `onedrive` 能力，全域預設 + 每人覆寫，預設關閉），使用者自己連結 |

現有限制：
- 登入不保存 IdP token（ADR-0009：`SaveTokens=false`）；
- Agent container 不得持有任何平台或第三方憑證（ADR-0004）；
- API 只經 runtime host 存取使用者檔案（ADR-0008）。

## 決策

### 1. 授權：獨立的連結流程，沿用 Ymir 的 Entra 應用程式註冊

- 連結與登入**分開**：`GET /api/connectors/onedrive/connect` 以授權碼 + PKCE 導向 Microsoft，scope 為 `offline_access Files.ReadWrite User.Read`；callback 為 `/api/connectors/onedrive/callback`。
  - state 與 PKCE verifier 放在加密的短期 cookie，只用一次。
  - callback 驗證 state，並確認 Graph `/me` 的 `id`（oid）與 Ymir 使用者的企業帳號一致；本機帳號沒有可比對的 oid，以連結當下的 Microsoft 帳號為準。
  - 完成後導回站內相對路徑（`SafeRedirect`）。
- tenant、client id、client secret 與 authority 一律取自 `OidcSettingsProvider`（ADR-0010），不另外設定。
  - Entra 應用程式需新增委派權限 `Files.ReadWrite` 與 `offline_access`，並把 callback 加入 redirect URI。
  - 租戶的同意政策可能需要 Entra 管理員先代表組織同意（`docs/guides/onedrive.md`）。
- 登入仍然 `SaveTokens=false`，這個流程不影響登入。

### 2. 憑證：只保存加密的 refresh token，只在後端使用

- refresh token 以 Data Protection 加密（purpose `Ymir.Connectors.OneDrive.v1`，沿用 cookie 的金鑰），存在 `vibemaker.onedrive_connections`。
  - access token 只放記憶體快取，到期前重新換發。
  - refresh token 換發時一併更新。
- token 不出現在任何 API 回應、稽核、log 或錯誤訊息，也不進 Agent container、瀏覽器或版控。
- 換發失敗（`invalid_grant`，例如密碼變更、權限撤回）時，狀態改為 `NeedsReauth`：
  - 停止同步，待上傳的本機檔案保留；
  - 使用者重新連結後繼續。
- 解除連結會刪除 token 與同步紀錄；雲端檔案與本機檔案都保留。

### 3. 範圍與映射

- 連結後，使用者選擇或建立一個根資料夾（預設 `/Ymir`）；API 只接受資料夾名稱或路徑，由後端在**該使用者自己的 drive** 建立或查找。
- 每個專案對應 `<根>/projects/<專案名稱>-<id 前 8 碼>`，未分組對話對應 `<根>/chats/<標題>-<id 前 8 碼>`。
  - 第一次建立後記錄 driveItem id，之後改名不影響對應。
- 映射與同步紀錄都以 server 端的擁有者為準，不接受前端傳入的 drive 或 item id（SA §12）。
- 排除：
  - 隱藏檔、`node_modules`（與 `IWorkspaceFileReader` 相同）；
  - `agent-state`（Pi session、擴充設定）不在工作目錄內，本來就不同步；
  - 單檔上限可設定，預設 100 MB。

### 4. 同步

- **下載**：`ExecutionRunner` 在 EnsureRuntime 之後、啟動 Agent 之前執行（持有使用者的執行鎖）。雲端有變更、本機沒有變更的檔案直接下載。
- **上傳**：execution 結束後（不論完成、失敗或取消），建立持久化的同步工作，由背景 worker 處理。
  - worker 先取得同一把使用者執行鎖，Agent 不會同時寫檔；
  - 服務重啟後工作會繼續。
- **版本**：
  - 以 Graph 的 eTag 與本機的大小 + 修改時間 + SHA-256 為同步基準；
  - 上傳帶 `If-Match`，雲端在這段期間被改過時不覆蓋。
- **衝突**：兩邊都改過時保留兩份，雲端版本另存為 `檔名 (OneDrive 衝突 yyyyMMdd-HHmm).ext`，衝突數顯示在畫面上。
- **首版不同步刪除與改名**：不傳播刪除，改名視為新檔。
- **檔案寫入 runtime**：新增 `IWorkspaceFileWriter`，比照 `IWorkspaceFileReader`：
  - 經 `IAgentRuntimeManager` 在 runtime 內執行，Local / Podman / Remote 都適用；
  - 路徑經 stdin 傳入，在 runtime 內以 realpath 確認不逃出工作目錄、拒絕 symlink；
  - 先寫暫存檔再 rename。
- **失敗處理**：
  - Graph 回 429 / 503 時依 `Retry-After` 退避重試，次數有上限；
  - 下載失敗不阻擋 Agent 執行，以狀態事件提示「使用本機檔案」；
  - 上傳失敗保留待同步紀錄，可手動重試；
  - 回給瀏覽器的只有摘要（SA §10、§12）。

### 5. 網路與隔離

- 只有 API（後端）連 `graph.microsoft.com` 與 Entra 的 token 端點。Agent container 不需要、也不應該連，受限網路（ADR-0012 A.8）下照常運作。
- Graph base URL 可設定（`Ymir:Connectors:OneDrive:GraphBaseUrl`），只用於測試替身；正式環境使用預設值。

### 6. 管理與稽核

- 擴充政策新增 `onedrive` 能力（預設關閉），沿用 ADR-0012 的全域預設 + 每人覆寫與稽核。
  - 沒有能力的使用者看不到連結入口，相關 API 回 403；
  - 已連結的使用者被關閉後停止同步，連結保留。
- 稽核：`connector.onedrive.connect`、`connector.onedrive.disconnect`、`connector.onedrive.root.update`、`connector.onedrive.sync`（失敗時記錄摘要，不含路徑內容與 token）。

## 分階段

| 階段 | 內容 |
|---|---|
| O1 | 能力、連結 / 解除連結、token 加密保存與換發、根資料夾、設定頁、FakeGraph 測試替身 |
| O2 | 同步：`IWorkspaceFileWriter`、下載 / 上傳、衝突、持久化工作、雲端保存狀態與重試、使用指南 |
| 後續 | delta API 增量比對、刪除與改名政策、即時同步、分享資料夾 |

## 影響

- CLAUDE.md 安全紅線新增例外：「不得保存 IdP token」仍適用於登入；OneDrive connector 的 refresh token 依本 ADR 加密保存，只在後端使用。
- 新增資料表 `onedrive_connections`、`onedrive_sync_items`、`onedrive_sync_jobs`（schema vibemaker）。跨模組只存 user id（ADR-0001）。
- 測試以 `Ymir.Testing.FakeGraph`（記憶體中的 drive）與 FakeOidc（加上 refresh token）進行。真實 Entra 同意與 Microsoft Graph 在沙箱連不到，標註「未驗證、待使用者環境確認」。
- 原本規劃的 RAG 與網站託管 ADR 編號順延為 ADR-0014、ADR-0015。
