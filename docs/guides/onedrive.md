# OneDrive 同步指南

決策見 [ADR-0013](../adr/0013-onedrive-connector.md)。使用者把自己的公司 OneDrive 連到 Ymir 後，Agent 每次執行前會從 OneDrive 下載檔案，執行後把新增或修改的檔案上傳回去。同步由 Ymir 後端經 Microsoft Graph 進行；Agent container 拿不到任何 Microsoft 憑證，也不需要連到 Microsoft 的網路。

## 1. Entra 應用程式（管理員，一次）

OneDrive 沿用 Ymir 登入用的同一個 Entra 應用程式註冊（[entra-id.md](entra-id.md)），另外需要：

1. **API 權限** → 新增 Microsoft Graph 的**委派**權限：`Files.ReadWrite`、`offline_access`、`User.Read`。
   租戶如果不允許使用者自行同意，請 Entra 管理員按「代表 <租戶> 授與管理員同意」。
2. **驗證** → Web 平台的重新導向 URI 加上 `https://<Ymir 網域>/api/connectors/onedrive/callback`（與登入的 `/signin-oidc` 並列）。

`Files.ReadWrite` 讓 Ymir 可以存取使用者的整個 OneDrive。除了設定頁的同步資料夾，ADR-0022 現已提供整個自己 drive 的瀏覽／搜尋，以及明確連結項目的執行前下載；**不是**把整個 drive 自動交給 Agent。連結項目的雲端寫回仍未啟用。

## 2. 開放能力（管理員）

「管理 → 系統設定 → Agent 擴充能力」勾選 **OneDrive 連結**（全域預設，預設關閉），或在「使用者」頁為個別使用者設定「允許 / 不允許」。關閉後同步立即停止，使用者仍可以解除連結。

## 3. 連結與選擇資料夾（使用者）

1. 「設定 → OneDrive」按「連結 OneDrive」，在 Microsoft 頁面同意。企業帳號只能連結與 Ymir 登入相同的帳號。
2. 設定「同步資料夾」（預設 `/Ymir`），Ymir 會在 OneDrive 建立它。
3. 之後每個專案對應 `<同步資料夾>/projects/<專案名稱>-<id 前 8 碼>`，未分組的對話對應 `<同步資料夾>/chats/<標題>-<id 前 8 碼>`。第一次同步建立後，改名不影響對應。

## 4. 同步規則

### 瀏覽自己的整個 OneDrive（ADR-0022 O3a）

新版本的「設定 → OneDrive」在已連結且管理員允許時，提供「瀏覽整個自己的 OneDrive」。可開啟資料夾、回根目錄／上一層，以及搜尋檔案名稱（最多 50 筆）；不需要先設定同步根資料夾。瀏覽只取得名稱、item id、類型、大小與修改時間，不下載內容、不回傳 token 或預授權 URL。

只允許目前使用者自己的 drive，跨 drive／remoteItem 捷徑會被拒絕或過濾，不涵蓋「與我共用」、SharePoint／Teams。管理員關閉能力、使用者解除連結或授權失效後無法繼續瀏覽。

### 選取檔案交給 Agent（ADR-0022 O3b）

1. 在已有對話的「檔案」面板開啟「OneDrive 連結檔案 → 從 OneDrive 加入」。
2. 瀏覽或搜尋，選「連結檔案」／「連結資料夾」，可設定唯讀。此時只保存連結，不立即啟動 runtime 或修改雲端。
3. 下次送出訊息前，後端把項目下載到工作目錄的 `onedrive/<連結 id 前 8 碼>-<安全名稱>`，再把路徑與限制附加到 Agent prompt。專案內的對話共用同一組連結；獨立對話各自隔離。
4. 面板可查看下載時間、錯誤、本機衝突副本數、切換唯讀或取消連結。取消連結只刪 metadata 與基準，不刪雲端或本機副本；管理員關閉能力後仍可取消連結。

- 不要求選擇 ADR-0013 的同步根資料夾，但必須先連結帳號，且管理員允許 OneDrive 能力。
- 不能連結 drive 根目錄、原同步根目錄或其內的項目；也拒絕包含同步根的祖先資料夾與相互重疊的連結，避免兩套同步／多個連結互相覆蓋。同一項目重複選取會回傳既有連結。
- 預設每個工作目錄 20 個連結、每個連結資料夾最多下載 500 個檔案、單檔 100 MB；隱藏檔、`node_modules`、不安全路徑、外部 drive／remoteItem、過深或過大的資料夾會略過並提示。
- 每次以目前使用者 token 重新驗證 drive 與項目；憑證不交給 Agent。管理員關閉能力或使用者解除授權後停止下載，已存在的本機副本保留。
- 基準使用獨立 `onedrive_linked_files` 表，記錄 item id、eTag 與本機 SHA-256；雲端與本機同時改過時先保留本機衝突副本，再更新雲端版本的本機副本。現階段衝突副本**僅在本機**，尚未上傳到雲端。
- 唯讀連結不寫回雲端；下次執行時恢復雲端對應檔案的內容，恢復前保留已有本機修改。這不是 Agent 容器內的 OS 唯讀掛載。
- `onedrive/` 保留給連結項目；原同步資料夾的上傳／下載一律排除這個頂層 namespace（不分大小寫），避免重複同步。

**部署前必做**：套用 `OneDriveLinkedItems` migration，再部署 API／前端；本切片不需更新 RuntimeHost 或 Agent image。正式資料庫先備份，勿把開發環境的自動 migration 當正式更新程序。

**階段限制**：目前只下載，不自動寫回連結項目的原位置，不傳播刪除／移動／改名。雲端項目失效時保留本機檔案；後續 O4～O5 才加入寫回、雙向變更與大量刪除確認。輸入區附件選單整合仍待後續；目前入口在檔案面板。程式合併不表示正式站已部署。

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
