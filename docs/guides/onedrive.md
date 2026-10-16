# OneDrive 同步指南

決策見 [ADR-0013](../adr/0013-onedrive-connector.md)。使用者在**外掛市集**（`/plugins`，[ADR-0030](../adr/0030-plugin-marketplace-and-onedrive-agent-tools.md)）把自己的公司 OneDrive 連到 Ymir 後，有兩個彼此獨立的功能：

- **讓 Agent 使用（OneDrive 外掛）**：Agent 在對話中自己搜尋、下載、上傳、移動、刪除 OneDrive 上的檔案。
- **同步資料夾**（選用）：Agent 每次執行前從 OneDrive 的一個資料夾下載檔案，執行後把新增或修改的檔案上傳回去。

兩者都由 Ymir 後端經 Microsoft Graph 進行；Agent container 拿不到任何 Microsoft 憑證，也不需要連到 Microsoft 的網路。

## 1. Entra 應用程式（管理員，一次）

OneDrive 沿用 Ymir 登入用的同一個 Entra 應用程式註冊（[entra-id.md](entra-id.md)），另外需要：

1. **API 權限** → 新增 Microsoft Graph 的**委派**權限：`Files.ReadWrite`、`offline_access`、`User.Read`。
   租戶如果不允許使用者自行同意，請 Entra 管理員按「代表 <租戶> 授與管理員同意」。
2. **驗證** → Web 平台的重新導向 URI 加上 `https://<Ymir 網域>/api/connectors/onedrive/callback`（與登入的 `/signin-oidc` 並列）。

`Files.ReadWrite` 讓 Ymir 可以存取使用者的整個 OneDrive。使用者在外掛市集打開「讓 Agent 使用」後，Agent 就能讀寫整個自己的 OneDrive（見 §4）；沒打開時只有同步資料夾會被存取。

## 2. 開放能力（管理員）

「管理 → 系統設定 → Agent 擴充能力」勾選 **OneDrive 連結**（全域預設，預設關閉），或在「使用者」頁為個別使用者設定「允許 / 不允許」。關閉後同步立即停止，使用者仍可以解除連結。

## 3. 安裝與啟用（使用者）

1. 左下角「外掛」→ OneDrive 卡片按「連結 OneDrive」，在 Microsoft 頁面同意。企業帳號只能連結與 Ymir 登入相同的帳號。
2. 要讓 Agent 存取 OneDrive：打開卡片上的「讓 Agent 使用」（會先確認風險）。需要管理員設定 MCP gateway（`deploy/mcp/README.md` 第 5 點）；沒有設定時卡片會顯示原因。
3. 要用同步資料夾：設定「同步資料夾」（預設 `/Ymir`），Ymir 會在 OneDrive 建立它。
3. 之後每個專案對應 `<同步資料夾>/projects/<專案名稱>-<id 前 8 碼>`，未分組的對話對應 `<同步資料夾>/chats/<標題>-<id 前 8 碼>`。第一次同步建立後，改名不影響對應。

## 4. 同步規則

### Agent 使用 OneDrive（外掛，ADR-0030）

打開「讓 Agent 使用」後，每次**個人**對話執行時 Agent 會看到 `onedrive` 服務的工具：

| 工具 | 說明 |
|---|---|
| `search` / `list_folder` | 以名稱搜尋、列出資料夾（只回名稱、id、類型、大小、修改時間） |
| `download` | 下載檔案到工作目錄 `onedrive/<檔名>`（單檔 100 MB；資料夾要逐一下載） |
| `upload` | 把工作目錄的檔案上傳到指定資料夾；同名檔案預設不覆蓋，`overwrite=true` 才覆蓋 |
| `create_folder` / `move` | 建立資料夾、移動或改名（目標已有同名項目時不覆蓋） |
| `delete` | 刪除到 OneDrive 回收筒，可以從回收筒還原 |

- 只限使用者自己的 drive；「與我共用」、SharePoint、remoteItem 一律拒絕。不能移動或刪除根目錄。
- 路徑：Agent → MCP gateway（`/mcp/onedrive`，每人 rate limit、稽核工具名稱）→ API `/internal/mcp/onedrive` → Graph。Agent 只有一個短期 gateway token，執行結束後立即失效。
- 每次呼叫都重新檢查：管理員仍開放、使用者仍啟用、OneDrive 連線正常、這次執行還在進行；關掉任何一項都立即生效。
- 寫入類工具（upload、create_folder、move、delete）寫稽核 `onedrive.agent.<動作>`（target 是 Graph item id，不記檔名）。
- 群組對話（@bot）不提供：群組的工作目錄是成員共用的。
- 風險：啟用後 Agent 可以修改、刪除任何檔案；對話中的檔案或網頁內容可能誘導 Agent 做出使用者沒要求的操作。

ADR-0022 的「在檔案面板手動連結 OneDrive 項目」已移除（資料表 `onedrive_linked_items`、`onedrive_linked_files` 保留，不再使用）。`onedrive/` 仍是同步資料夾排除的頂層目錄，Agent 下載的檔案不會被重複同步。

**部署前必做**：套用 `UserPluginSettings` migration；API、前端與 MCP gateway 一起更新（gateway 加上 `McpGateway__ApiUrl`）。正式資料庫先備份。

**未驗證、待使用者環境確認**：真實 Microsoft Graph 的 move（PATCH）與 delete；沙箱以 Fake Graph 驗證。

### 既有同步資料夾（ADR-0013）

| 時機 | 做什麼 |
|---|---|
| Agent 執行前 | 下載雲端新增或修改的檔案；失敗時對話顯示「OneDrive 同步失敗，使用本機檔案」，執行照常進行 |
| Agent 執行後 | 背景工作先下載、再上傳本機新增或修改的檔案；狀態顯示在檔案面板的「雲端保存狀態」 |
| 手動 | 檔案面板按「立即同步」或失敗後的「重試」 |

- **衝突**：雲端與本機在上次同步後都改了同一個檔案，原檔名保留雲端版本，本機版本另存 `名稱 (OneDrive 衝突 yyyyMMdd-HHmmss).副檔名`，兩邊都有兩份，不會互相覆蓋。
- **不同步**：刪除與改名（首版）、隱藏檔（`.git`、`.env`…）、`node_modules`、名稱含 OneDrive 不接受字元（`" * : < > ? \ |`）的檔案、超過大小上限的檔案。
- **交付成果**（`deliverables/`）只上傳、不從雲端下載。
- **限制**（`Ymir:Connectors:OneDrive:*`）：`MaxFileBytes`（預設 100 MB）、`MaxFiles`（每個資料夾預設 5000 個）、`PollInterval`（背景工作保底輪詢，預設 30 秒）。大於 4 MB 的檔案以 upload session 分段上傳。
- **失敗重試**：背景同步失敗後依 1、5、15、60 分鐘自動重試，共 5 次；授權失效（密碼變更、權限被撤回）時停止並顯示「請重新連結」，本機檔案保留。同步工作存在資料庫，API 重新啟動後會繼續。

## 5. 安全

- 只保存 Data Protection 加密的 refresh token（`vibemaker.onedrive_connections`）；access token 只在 API 記憶體；兩者都不出現在 API 回應、log、稽核或 Agent container。
- 檔案內容經 runtime 內的程序讀寫，路徑由後端產生並在 runtime 內以 realpath 檢查，不會寫出工作目錄。
- 解除連結只刪除 token；OneDrive 與 Ymir 內的檔案都保留。

## 6. 本機驗證

沙箱連不到 Microsoft；Fake OIDC 內含 Fake Graph（`/graph/v1.0`）。設定方式見 `web/e2e/onedrive-flow.mjs` 開頭，然後 `npm run e2e:onedrive -- <截圖目錄>`。

**未驗證、待使用者環境確認**：真實 Entra 的權限同意流程、Microsoft Graph 與 OneDrive for Business 的實際行為（eTag 變動時機、upload session、節流）。
