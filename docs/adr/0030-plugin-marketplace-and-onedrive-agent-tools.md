# ADR-0030：外掛市集與 OneDrive Agent 工具

- 狀態：已採納（使用者 2026-10-16 要求實作）
- 日期：2026-10-16
- 取代：[ADR-0022](0022-onedrive-linked-files.md) 的「使用者手動指定連結項目」（O3a 瀏覽入口、O3b 連結與執行前下載；O4、O5 不再實作）
- 修訂：[ADR-0028](0028-personal-connections-and-tools.md) §7（連接帳號後，啟用外掛的使用者讓 Agent 自行存取自己的 OneDrive）、[ADR-0012](0012-agent-extensions-and-platform-mcp.md) B（gateway 新增內建服務 `onedrive`，後端是 API）
- 不變：[ADR-0013](0013-onedrive-connector.md) 的同步資料夾（OneDrive 上的一個資料夾和 Agent 工作目錄雙向同步）是另一個功能，保留原樣
- 2026-10-29 修訂：同步資料夾已由 [ADR-0033](0033-persistent-runtime-and-onedrive-plugin-only.md) 移除，OneDrive 只剩本 ADR 的 Agent 工具；下文提到同步資料夾的部分僅供追溯

## 背景

使用者回饋：ADR-0022 的「使用者在檔案面板逐一挑選 OneDrive 項目」不是要的效果。要的是比照 ChatGPT 的外掛（apps / connectors）：

1. 有一個**外掛市集**，列出可用的外掛；
2. 使用者在市集找到 OneDrive，**安裝**（連結帳號）並**啟用**；
3. 之後 Agent 在對話中需要時，**自己**搜尋、讀取、寫入使用者的 OneDrive。

使用者 2026-10-16 的決定：

| # | 項目 | 決定 |
|---|---|---|
| 1 | Agent 權限 | **讀 + 寫**：搜尋、列出、下載、上傳、建立資料夾、移動 / 改名、刪除（刪除進 OneDrive 回收筒） |
| 2 | ADR-0022 手動連結 | **移除** UI、API 與執行前下載；資料表 `onedrive_linked_items`、`onedrive_linked_files` 保留不動 |
| 3 | ADR-0013 同步資料夾 | **保留**（另一個功能；設定移到外掛的 OneDrive 卡片） |
| 4 | 市集第一版 | OneDrive + 管理員開放的平台 MCP 服務 + 個人 MCP（ADR-0028） |

ADR-0022 當時不採用「Agent 經 MCP 自己操作 OneDrive」（做法 B）的兩個理由，這次的處理：

- 「gateway 不帶使用者身分給後端，而 refresh token 只在 API」→ 後端改成 **API 自己**（見決策 2），gateway 把 Agent 的每人 token 原樣轉給 API，由 API 驗證並以該使用者的 OneDrive 授權呼叫 Graph。refresh token 仍然只在 API。
- 「被 prompt injection 時影響範圍是整個 OneDrive」→ 使用者明確接受（決定 1）。緩解：預設不啟用、需要使用者自己在市集啟用、每次寫入稽核、刪除只進回收筒、市集卡片明確告知風險（決策 5）。

## 決策

### 1. 外掛市集

- 新頁面 `/plugins`（左側選單「外掛市集」），取代個人設定的「帳號連接」「MCP 與工具」分頁（舊網址導向 `/plugins`）。
- 卡片三類：
  - **OneDrive**（平台內建）：需要管理員開放 `oneDrive` 能力（ADR-0012 擴充政策，沿用）；「安裝」= 連結帳號（ADR-0013 OAuth 流程不變）；「讓 Agent 使用」開關（決策 3）；同步資料夾設定（ADR-0013）放在卡片的進階設定。
  - **平台 MCP 服務**（ADR-0012 B）：只列管理員開放給這位使用者的服務；使用者可以自己停用。
  - **個人 MCP**（ADR-0028）：沿用原本的新增 / 測試 / 啟停 / 移除。
- 管理員控制「誰能用」，使用者控制「要不要用」；兩者都允許時 Agent 才看得到。

### 2. OneDrive 工具由 API 提供，經 MCP gateway 轉送

```
Agent ──Bearer YMIR_MCP_TOKEN──▶ MCP Gateway /mcp/onedrive
         （驗證 token、每人 rate limit、稽核 mcp.tool.call）
                 │  Authorization: Bearer <由簽章金鑰衍生的後端憑證>
                 │  X-Ymir-Mcp-Token: <原 token>
                 ▼
        API /internal/mcp/onedrive  ──使用者自己的 access token──▶ Microsoft Graph
         （再驗一次 token、execution、能力、外掛開關、連線狀態）
```

- `onedrive` 是**內建服務名稱**：`deploy/mcp/servers.json` 不得使用（載入時拒絕）；gateway 設定 `McpGateway:ApiUrl`（gateway 連得到的 API 位址）時才提供。
- gateway → API 的後端憑證不另設 secret：以既有的 token 簽章金鑰衍生（`HMAC(key, "ymir-mcp-builtin-backend:v1")`），API 與 gateway 都算得出來，Agent 算不出來。Agent 即使直接連到 API，沒有這個憑證也會被拒。
- token 新增 `exe`（execution id）。API 以 execution 推導工作目錄（`RuntimePaths`，不接受外部路徑）；只有**執行中**、屬於 token 使用者、**不是群組執行**的 execution 可以使用。執行結束後同一個 token 立即失效（不必等 token 過期）。
- 每次工具呼叫都重新檢查：帳號有效、管理員仍允許 `oneDrive`、使用者仍啟用外掛、OneDrive 連線正常。任何一項不成立就回錯誤摘要。
- 端點不在 OpenAPI、不接受 cookie；只給主機上的 gateway 使用：經公開網域進來的 `/internal/*` 由 public edge 回 404（同 `/health`），帶有 Cloudflare 轉送標頭（`Cf-Connecting-Ip`）的請求也一律拒絕。
- 沒有設定平台 MCP（`Ymir:Mcp:GatewayUrl`）時，市集的 OneDrive 卡片顯示「Agent 存取尚未啟用（需要管理員設定 MCP gateway）」；連結帳號與同步資料夾不受影響。

### 3. 使用者的外掛開關

- 新資料表 `vibemaker.user_plugin_settings`（user id、外掛 key、enabled、更新時間；主鍵 user id + key）。key：`onedrive`、`mcp:<服務名稱>`。
- 預設值：`onedrive` **關閉**（讀寫整個 OneDrive，必須使用者自己開）；平台 MCP **開啟**（維持現有行為，管理員開放就能用）。
- API：`GET /api/plugins`（市集清單與狀態）、`PUT /api/plugins/{key}`（啟用 / 停用；只能改自己的、只能是清單內的 key）。異動寫稽核 `plugin.enable` / `plugin.disable`。

### 4. 工具

| 工具 | 說明 |
|---|---|
| `search` | 以名稱搜尋自己的 OneDrive（最多 50 筆） |
| `list_folder` | 列出資料夾（省略 `folderId` 為根目錄） |
| `download` | 下載**檔案**到工作目錄 `onedrive/<檔名>`，回傳相對路徑；單檔 100 MB |
| `upload` | 把工作目錄內的檔案上傳到指定資料夾；同名檔案預設不覆蓋，`overwrite=true` 才覆蓋 |
| `create_folder` | 建立資料夾 |
| `move` | 移動及 / 或改名 |
| `delete` | 刪除（進 OneDrive 回收筒，使用者可以從回收筒還原） |

- 所有 item id 都以使用者自己的 access token 重新查詢，必須屬於自己的 drive（沿用 ADR-0022 §3 的檢查：拒絕「與我共用」、SharePoint、remoteItem）；不能刪除、移動根目錄。
- 回傳只有名稱、id、類型、大小、修改時間與相對路徑；不回傳 token、eTag 或預授權下載網址。
- 工作目錄內的檔案經 `IWorkspaceFileReader` / `IWorkspaceFileWriter` 在 runtime 內讀寫（realpath 檢查、拒絕 symlink 逃逸），路徑規則同 `WorkspacePathRules`。
- 寫入類工具（upload、create_folder、move、delete）每次寫稽核 `onedrive.agent.<動作>`，target 是 Graph item id，不記檔名或路徑；gateway 另外稽核工具名稱。
- `onedrive/` 仍是 ADR-0013 同步資料夾排除的頂層目錄，Agent 下載的檔案不會被重複同步。

### 5. 風險與告知

- 啟用時的確認訊息、卡片說明都要寫明：「Agent 可以讀取、修改、刪除你 OneDrive 上的任何檔案；對話中的檔案或網頁內容可能誘導 Agent 做出你沒要求的操作。刪除的檔案會在 OneDrive 回收筒保留一段時間。」
- 群組執行一律不提供（群組 workspace 是成員共用的，ADR-0026）。
- Agent 拿不到 Microsoft 的任何 token（ADR-0004、ADR-0013 不變）；拿到的是期限 ≤ 1 小時、且只在執行中有效的 gateway token。

## 影響

- 移除：`OneDriveLinks`、`OneDriveLinkLocks`、`/api/conversations/{id}/onedrive/links*`、`/api/connectors/onedrive/browse`、`/search`，檔案面板的「OneDrive 連結檔案」、設定頁的「瀏覽整個自己的 OneDrive」。`OneDriveBrowser` 的檢查邏輯保留給工具使用。資料表與 migration 保留（不刪資料）。
- 新增 migration（`user_plugin_settings`）；部署需更新 API、前端與 **MCP gateway**（`McpGateway__ApiUrl`）。
- 平台 MCP 的 token 格式多一個選填欄位，舊版 gateway 會忽略它；新版 gateway 與 API 要一起部署。
- **未驗證、待使用者環境確認**：真實 Microsoft Graph 的 move / delete 行為、正式主機上 gateway 連到 API 的位址。沙箱以 FakeGraph 驗證。
