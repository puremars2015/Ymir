# ADR-0010：管理介面與可由網頁修改的系統設定

- 狀態：已採納（修訂 ADR-0006 第 4 點、ADR-0009 的設定來源）
- 日期：2026-10-06

## 背景

使用者要求補齊管理介面，範圍有四項：

- 總覽儀表板；
- 稽核紀錄；
- 在網頁上設定 **Entra ID**（Tenant / Client ID / client secret 等）；
- 在網頁上設定 **Cloudflare Tunnel**（token、對外網域）。

目前的限制：

- **Entra 設定只能放在部署主機的 `.env`**，且 API 啟動時只讀一次：
  - OIDC scheme 只在設定完整時註冊；
  - `EntraIdentityProvider` 是 singleton。
- **Tunnel token 不得進版控或任何 container**（ADR-0006 第 4 點）。API 本身跑在容器內（ADR-0008）。
- **runtime host 以無特權帳號 `ymir` 執行**：systemd 的 `ProtectSystem=full` 讓 `/etc` 唯讀，不能寫 root 擁有的檔案或重啟系統服務。
- **token 模式的 tunnel**：網域與 ingress 設定在 Cloudflare dashboard，token 本身不含網域。

使用者的決定：

- Entra client secret 加密存資料庫；
- Tunnel token 交給主機上的 runtime host 套用，API 與資料庫都不保存。

## 決策

### 1. 管理介面

- **頁面**：`/admin` 底下有五個分頁：總覽、使用者、Make 主題、系統設定、稽核紀錄。所有端點都使用 `AuthSetup.AdminPolicy`，並加入授權矩陣的 `AdminOnlyRequests`。
- **總覽**（`GET /api/admin/overview`）：
  - 使用者數（總數、啟用、停用、Admin、24 小時內登入）；
  - 執行中與排隊中的 execution 數；
  - 今日的完成 / 失敗 / 取消數；
  - 近 7 天每日執行數；
  - 各使用者 runtime 的狀態，可停止：`POST /api/admin/runtimes/{userId}/stop`，寫入稽核 `admin.runtime.stop`。
  - 「今天」依瀏覽器傳入的 UTC 位移切日，伺服器不依賴時區資料庫。
- **跨模組組合**：使用者名稱在 Platform、執行與 runtime 在 Vibe Maker，由 Api 層組合，模組之間不互相參考（ADR-0001）。
- **稽核紀錄**（`GET /api/admin/audit`）：
  - Platform 新增唯讀查詢 `IAuditLogQuery`，寫入仍只經由 `IAuditLog`。
  - 可依動作前綴、使用者（操作者或目標）、結果、時間篩選。
  - 依 Id 做 keyset 分頁，每頁 50 筆。
  - `audit_log` 新增 `action`、`actor` 的 index。
  - 稽核內容本來就只有摘要（動作、目標 id），不含機密。

### 2. 系統設定的存放

- **資料表**：新的 `platform.system_settings`，欄位為 key / value / 更新時間 / 更新者。
- **機密加密**：機密值（Entra client secret）以 ASP.NET Core Data Protection 加密：
  - purpose 為 `Ymir.SystemSettings.v1`；
  - 沿用 cookie 已在使用的金鑰，部署時以 `Ymir:DataProtection:KeysPath` 持久化（ADR-0008）。
  - 資料庫外洩時，沒有金鑰無法解密。
- **生效值**：**資料庫 > 部署設定（`.env`）**。「還原為部署設定」會刪除資料庫中的值。
- **機密不外流**：
  - 機密只能寫入、不回傳：API 只回「已設定 / 更新時間 / 到期日」。
  - 稽核只記錄改了哪些欄位名稱，不記錄值。

### 3. Entra ID 設定

- **Authority 不開放任意輸入**：
  - 由 `{AuthorityHost}/{tenantId}/v2.0` 組成；
  - `AuthorityHost` 只能用部署設定 `Ymir:Auth:Oidc:AuthorityHost`，預設 `https://login.microsoftonline.com`。
  - 管理員只填 Tenant ID（GUID），所以「測試設定」時伺服器讀取 metadata 不會被拿來打任意網址（SSRF）。
  - https 規則不變；只有 Development 的 loopback 可以用 http（Fake OIDC）。
- **不重啟就生效**：
  - `oidc` scheme 一律註冊；
  - `OpenIdConnectOptions` 由 `IConfigureNamedOptions` 從目前的生效值產生；
  - 存檔時清掉 options 快取，下一個登入請求就用新設定。
  - `EntraIdentityProvider`、`/api/auth/providers`、`/api/auth/login` 都改讀生效值。
- **防止鎖死**：
  - 本機帳號登入不在網頁設定；
  - 停用或還原 Entra 時，如果會變成沒有任何登入方式，或沒有任何啟用中的本機 Admin，就拒絕。

### 4. Cloudflare Tunnel

- **cloudflared 的執行方式**：改成以 `ymir` 帳號執行的 **rootless Podman Quadlet user service**（`ymir-cloudflared.service`），讀取 `~ymir/.config/ymir/cloudflared.env`（600）。
  - runtime host 已經以 `ymir` 執行，可以寫這個檔案，並用 `systemctl --user` 重啟服務，**不需要提升權限**（維持 ADR-0008 的最小權限）。
- **token 的流向**：瀏覽器 → API → runtime host（Unix socket + token）→ 檔案。
  - API 不保存，也不寫入資料庫或 log。
  - token 只存在那個 600 的檔案和 cloudflared container 內。
  - Agent container 不會掛載這個路徑。
- **ADR-0006 第 4 點修訂為**：tunnel 憑證不進版控、不進 API 容器與 Agent container，只有 cloudflared 與 runtime host 讀得到。
- **runtime host 新增端點**：
  - `PUT /v1/edge/tunnel-token`：設定 token；
  - `GET /v1/edge/tunnel`：回傳是否已設定、是否在執行、更新時間。
  - token 只允許 `[A-Za-z0-9+/=_.-]`、長度 100～4096，防止寫入 env 檔時注入多行內容。
  - 寫檔採 atomic rename。
  - `RuntimeHost:Tunnel:Mode` 預設 `Disabled`（例如 Windows 開發），這時網頁只顯示手動設定步驟。
- **對外網域**：
  - 存在 `system_settings`，生效值為「資料庫 > `Ymir:PublicEdge:PublicHostname`」。
  - Host 限制改成每個請求讀目前值，不必重啟。
  - `Ymir:PublicEdge:Enabled` 仍然只能在部署設定開啟，Development 照舊拒絕。
  - 變更網域時，畫面提示要同步修改 Entra 的 redirect URI 與 Cloudflare dashboard 的 Public Hostname。

## 影響

- **安全邊界改變**：
  - Admin 帳號可以改登入設定與 tunnel，所以 Admin 帳號本身的保護更重要（Entra 的 MFA、本機 Admin 的強密碼）。
  - 每個變更都寫入稽核。
- **Data Protection 金鑰**：遺失時，資料庫裡的 client secret 無法解密，Entra 登入會失敗；這時用本機 Admin 登入，在網頁上重新輸入 secret 即可。
- **部署方式**：
  - cloudflared 從 repo 目錄的 compose 改成 Quadlet user service；原本的 compose 方式保留給 Windows 開發。
  - 部署指南與 runtime host 的 systemd 設定要同步更新。
- **未驗證項目**：沙箱無法驗證真實 Entra 與 Cloudflare，以 Fake OIDC 與 fake 的服務控制器替代。
