# OneDrive 同步指南

決策見 [ADR-0013](../adr/0013-onedrive-connector.md)。使用者把自己的公司 OneDrive 連到 Ymir 後，Agent 每次執行前會從 OneDrive 下載檔案，執行後把新增或修改的檔案上傳回去。同步由 Ymir 後端經 Microsoft Graph 進行；Agent container 拿不到任何 Microsoft 憑證，也不需要連到 Microsoft 的網路。

## 1. Entra 應用程式（管理員，一次）

OneDrive 沿用 Ymir 登入用的同一個 Entra 應用程式註冊（[entra-id.md](entra-id.md)），另外需要：

1. **API 權限** → 新增 Microsoft Graph 的**委派**權限：`Files.ReadWrite`、`offline_access`、`User.Read`。
   租戶如果不允許使用者自行同意，請 Entra 管理員按「代表 <租戶> 授與管理員同意」。
2. **驗證** → Web 平台的重新導向 URI 加上 `https://<Ymir 網域>/api/connectors/onedrive/callback`（與登入的 `/signin-oidc` 並列）。

`Files.ReadWrite` 讓 Ymir 可以存取使用者的整個 OneDrive；實際只讀寫使用者在設定頁選的資料夾。

## 2. 開放能力（管理員）

「管理 → 系統設定 → Agent 擴充能力」勾選 **OneDrive 連結**（全域預設，預設關閉），或在「使用者」頁為個別使用者設定「允許 / 不允許」。關閉後同步立即停止，使用者仍可以解除連結。

## 3. 連結與選擇資料夾（使用者）

1. 「設定 → OneDrive」按「連結 OneDrive」，在 Microsoft 頁面同意。企業帳號只能連結與 Ymir 登入相同的帳號。
2. 設定「同步資料夾」（預設 `/Ymir`），Ymir 會在 OneDrive 建立它。
3. 之後每個專案對應 `<同步資料夾>/projects/<專案名稱>-<id 前 8 碼>`，未分組的對話對應 `<同步資料夾>/chats/<標題>-<id 前 8 碼>`。第一次同步建立後，改名不影響對應。

## 4. 同步規則

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
