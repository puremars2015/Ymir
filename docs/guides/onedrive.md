# OneDrive 外掛指南

決策見 [ADR-0013](../adr/0013-onedrive-connector.md)（授權與憑證）、[ADR-0030](../adr/0030-plugin-marketplace-and-onedrive-agent-tools.md)（Agent 工具）與 [ADR-0033](../adr/0033-persistent-runtime-and-onedrive-plugin-only.md)（移除同步）。使用者在**外掛市集**（`/plugins`）把自己的公司 OneDrive 連到 Ymir，並打開「讓 Agent 使用」後，Agent 就能在對話中自己搜尋、下載、上傳、移動、刪除 OneDrive 上的檔案。

**不同步**：對話紀錄與工作目錄不會自動備份到 OneDrive，也不會從 OneDrive 下載到執行環境（ADR-0033 移除了原本的「同步資料夾」）。需要時請 Agent 用工具上傳或下載。

所有 Graph 呼叫都由 Ymir 後端進行；Agent container 拿不到任何 Microsoft 憑證，也不需要連到 Microsoft 的網路。

## 1. Entra 應用程式（管理員，一次）

OneDrive 沿用 Ymir 登入用的同一個 Entra 應用程式註冊（[entra-id.md](entra-id.md)），另外需要：

1. **API 權限** → 新增 Microsoft Graph 的**委派**權限：`Files.ReadWrite`、`offline_access`、`User.Read`。
   租戶如果不允許使用者自行同意，請 Entra 管理員按「代表 <租戶> 授與管理員同意」。
2. **驗證** → Web 平台的重新導向 URI 加上 `https://<Ymir 網域>/api/connectors/onedrive/callback`（與登入的 `/signin-oidc` 並列）。

`Files.ReadWrite` 讓 Ymir 可以存取使用者的整個 OneDrive。只有使用者在外掛市集打開「讓 Agent 使用」後，Agent 才會在執行時存取（見 §4）。

## 2. 開放能力（管理員）

「管理 → 系統設定 → Agent 擴充能力」勾選 **OneDrive 連結**（全域預設，預設關閉），或在「使用者」頁為個別使用者設定「允許 / 不允許」。關閉後 Agent 立即無法使用，使用者仍可以解除連結。

## 3. 安裝與啟用（使用者）

1. 左下角「外掛」→ OneDrive 卡片按「連結 OneDrive」，在 Microsoft 頁面同意。企業帳號只能連結與 Ymir 登入相同的帳號。
2. 打開卡片上的「讓 Agent 使用」（會先確認風險）。需要管理員設定 MCP gateway（`deploy/mcp/README.md` 第 5 點）；沒有設定時卡片會顯示原因。

## 4. Agent 工具

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

ADR-0022 的「在檔案面板手動連結 OneDrive 項目」已移除（資料表 `onedrive_linked_items`、`onedrive_linked_files` 保留，不再使用）。

**部署前必做**：套用 `UserPluginSettings` migration；API、前端與 MCP gateway 一起更新（gateway 加上 `McpGateway__ApiUrl`）。正式資料庫先備份。

**未驗證、待使用者環境確認**：真實 Microsoft Graph 的 move（PATCH）與 delete；沙箱以 Fake Graph 驗證。

## 5. 安全

- 只保存 Data Protection 加密的 refresh token（`vibemaker.onedrive_connections`）；access token 只在 API 記憶體；兩者都不出現在 API 回應、log、稽核或 Agent container。
- 檔案內容經 runtime 內的程序讀寫，路徑由後端產生並在 runtime 內以 realpath 檢查，不會寫出工作目錄。
- 解除連結只刪除 token；OneDrive 與 Ymir 內的檔案都保留。
- 升級到 ADR-0033 時，migration `RemoveOneDriveSync` 會刪除舊的同步紀錄（`onedrive_sync_scopes`、`onedrive_sync_items`）與同步資料夾設定；已經上傳到 OneDrive 的檔案不受影響。

## 6. 本機驗證

沙箱連不到 Microsoft；Fake OIDC 內含 Fake Graph（`/graph/v1.0`）。設定方式見 `web/e2e/onedrive-flow.mjs` 開頭，然後 `npm run e2e:onedrive -- <截圖目錄>`。

**未驗證、待使用者環境確認**：真實 Entra 的權限同意流程、Microsoft Graph 與 OneDrive for Business 的實際行為（upload session、節流）。
