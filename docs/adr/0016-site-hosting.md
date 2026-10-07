# ADR-0016：網站託管——獨立網域、SiteHost 服務、票據登入與分享

- 狀態：已採用（2026-10-08）
- 相關：[前端網站託管計畫](../planning/frontend-site-hosting-plan.md)、ADR-0002（BFF Cookie）、ADR-0006（Cloudflare Tunnel）、ADR-0007（一人一個 runtime）、ADR-0008（API 容器化、runtime host）、ADR-0015（明確交付成果）

## 背景

使用者請 Agent 做出前端網站後，目前只能下載原始碼。我們要讓網站可以發布成網址，並由擁有者決定誰可以看。

使用者 2026-10-08 的決定：

1. **網域是設定值**：網址為 `<網站代碼>.<BaseDomain>`。真實的網域、DNS、TLS 與 Tunnel 萬用字元路由，由使用者在自己的環境設定；沙箱用 `*.localhost` 驗證。
2. **可見範圍由網站擁有者（開發者）選擇**，Ymir 管理員一律可以看。

## 決策

### 1. 元件

- 新增獨立服務 **`src/Ymir.SiteHost`**（Kestrel），負責三件事：
  - 依 `Host` 找到網站；
  - 檢查訪客權限；
  - 提供目前版本的靜態檔案。
  - 首版不使用 Nginx：權限檢查、票據兌換與檔案服務放在同一個 .NET 元件，容易測試；部署時放在 Tunnel 的萬用字元路由（`*.<BaseDomain>`）後面。
- SiteHost **只讀**：網站 volume 唯讀掛載；資料庫只查網站、分享與使用者狀態，並把票據標記為已使用。它不執行任何使用者程式，不掛載 workspace 或 container runtime socket。
- 網站內容一律在**獨立網域**上提供，絕不放在 Ymir 登入網域的路徑下。每個網站有自己的 hostname（各自的來源），網站腳本拿不到 Ymir 平台的 cookie，也拿不到其他網站的 localStorage。

### 2. 發布與版本

- **發布來源**：擁有者自己對話的工作目錄中的一個相對目錄，例如 `dist` 或 `deliverables/<id>`。
  - 必須通過 `WorkspacePathRules`，而且目錄裡要有 `index.html`。
- **複製方式**：API 經 `IWorkspaceFileReader` 在 runtime 內讀取（Remote 也適用，ADR-0008），複製到網站 volume `Ymir:Sites:Root/<siteId>/<versionId>/`。
  - API **不掛載** workspace。
  - reader 不跟隨 symlink，並排除隱藏檔與 `node_modules`。
- **限制**（`Ymir:Sites:*`，可設定）：

  | 項目 | 預設 |
  |---|---|
  | 單一版本大小 | 50 MB |
  | 單一版本檔案數 | 2,000 個 |
  | 每人網站數 | 20 個 |
  | 每個網站保留的版本 | 最近 3 個（舊版本目錄刪除） |

- **版本切換**：版本目錄完整寫好後，才把網站的 `CurrentVersionId` 更新為新版本（資料庫的一次更新），訪客不會看到新舊混合的內容。
  - 失敗時刪除該版本目錄，目前版本不變。
  - 同一個網站的發布以鎖序列化。
- **取消發布**：網站狀態改為 `Unpublished`，SiteHost 立即回 404。版本檔案保留，重新發布時沿用網址；刪除網站時才刪除檔案。
- **網站代碼**：8 碼小寫英數字，由平台隨機產生，有 unique index，不接受使用者指定。

### 3. 存取模式

| 模式 | 誰可以看 |
|---|---|
| `Public` 公開 | 任何人，不需要登入 |
| `AllUsers` 所有 Ymir 使用者 | 已登入、帳號未停用的 Ymir 使用者 |
| `SelectedUsers` 指定使用者 | 擁有者與分享名單中的使用者（以 user id 保存） |

- **擁有者**與 **Ymir 管理員（Admin）**一律可以看非公開網站。
- 分享只授予瀏覽權：不能下載原始碼、不能存取 workspace、不能發布或管理分享。
- 停用帳號、移除分享、取消發布、公開改成私人後，**下一個請求**就依新權限判斷。SiteHost 對權限結果最多快取 30 秒。

### 4. 私人網站的登入（票據）

1. 訪客打開私人網站、沒有網站 cookie 時，SiteHost 導向 Ymir 前端的 `/site-access?site=<siteId>&path=<相對路徑>`。這個頁面沿用 Ymir 的登入流程（ADR-0009）。
2. 前端以 XHR 呼叫 `POST /api/sites/{siteId}/ticket`（Cookie + XSRF，ADR-0002）。API 檢查權限後：
   - 簽發 **60 秒、只能用一次**的票據（隨機 32 bytes；資料庫只存 SHA-256 雜湊，綁定網站與使用者）；
   - 回傳 `https://<site>/.ymir/auth?ticket=...&path=...`。
   - 沒有權限時回 403 摘要。
3. SiteHost 的 `/.ymir/auth` 兌換票據：
   - 以 `UPDATE … WHERE used = 0 AND expires_at > now` 原子性標記已使用；
   - 設定只限該 hostname 的 cookie：`HttpOnly`、`SameSite=Lax`、HTTPS 時 `Secure`，內容以 SiteHost 自己的 Data Protection 保護（user id + 到期時間，8 小時）；
   - 導回站內路徑（只接受以單一 `/` 開頭的相對路徑）。
- 平台 cookie 與 IdP token 不會出現在網站網域。票據被重放、過期、拿到別的網站兌換，一律失敗。

### 5. 提供檔案

- 路徑解碼後逐段檢查：拒絕 `..`、隱藏檔、反斜線、控制字元。解析成實體路徑後，必須仍在版本目錄內。
- `/` 與 `/dir/` 提供對應的 `index.html`。
- **SPA 模式**：沒有副檔名的路徑回 `index.html`；有副檔名但不存在的檔案回 404，不會誤回 HTML。
- 回應標頭：
  - `X-Content-Type-Options: nosniff`；
  - `Referrer-Policy: same-origin`；
  - 私人網站加 `Cache-Control: private, no-store`；
  - 公開網站加短期快取。
- 未知網站、未發布網站、無權限的訪客：回同一種 404 或導向登入，不洩漏網站是否存在。

### 6. API（Cookie 驗證、擁有者檢查、加入授權矩陣；狀態變更加 antiforgery 與稽核）

- 擁有者：
  - `POST /api/conversations/{id}/sites`：建立並發布；
  - `GET /api/sites`：我的網站；
  - `POST /api/sites/{id}/publish`：重新發布，可改來源；
  - `POST /api/sites/{id}/unpublish`；
  - `DELETE /api/sites/{id}`；
  - `PUT /api/sites/{id}/access`：設定模式與分享名單。
- 分享：
  - `GET /api/users/search?q=`：只回未停用帳號的顯示名稱與帳號名稱，最多 20 筆；
  - `GET /api/sites/shared-with-me`。
- 票據：`POST /api/sites/{id}/ticket`。
- 稽核動作：`site.publish`、`site.unpublish`、`site.delete`、`site.access.update`。

## 結果

- 新增：
  - `vibemaker.sites`、`site_versions`、`site_shares`、`site_tickets`；
  - `SiteService`（Application）；
  - `src/Ymir.SiteHost`；
  - 前端的「我的網站」、發布對話框、存取設定、`/site-access` 頁。
- 部署：
  - SiteHost 是新的部署單元，唯讀掛載網站 volume，以獨立的 Data Protection 金鑰目錄保護網站 cookie。
  - Tunnel 需要新增 `*.<BaseDomain>` 指向 SiteHost 的路由。
  - API 需要可寫的網站 volume。
- 未驗證、待使用者環境確認：
  - 真實網域、萬用字元 DNS、TLS 與 Tunnel 路由；
  - 正式主機的 volume 權限；
  - 網站流量下的效能。

## 實作附註（H2，2026-10-07）

- 生效時間：SiteHost 對「網站查詢」（狀態、存取模式、目前版本）快取 10 秒，對「這位訪客能不能看」（帳號狀態、管理員角色、分享名單）快取 30 秒；取消發布、改存取模式、撤銷分享與停用帳號最慢在這個時間內生效。
- 網站 cookie 內容是 `{userId}|{siteId}|{到期}`，綁定網站：拿到另一個網站等同未登入。cookie 不設 `Domain`，只限該 hostname。
- 分享名單只接受存在且未停用的使用者，最多 200 人；擁有者自己不會出現在名單中。`GET /api/users/search` 不回自己，也不回 email 等其他欄位。
- 票據 API 不限擁有者：任何登入的使用者都可以呼叫，由 `SiteAccessRules.CanView` 決定；網站不存在或未發布回 404，沒有權限回 403 摘要。
- SiteHost 以 fallback 端點（`MapFallback("{**path}")`）提供檔案，`/.ymir/*` 由一般端點處理，網站檔案不能覆蓋這些路徑。
