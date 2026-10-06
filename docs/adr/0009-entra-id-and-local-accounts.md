# ADR-0009：企業帳號（Entra ID OIDC）與本機帳號密碼登入

- 狀態：已採納（實作 ADR-0002 的 BFF + HttpOnly Cookie；SA §4、驗收條件 #1）
- 日期：2026-10-06

## 背景

正式環境原本只有 Development 的免密碼 dev 登入。已確認的環境與需求：

- 公司使用 **Entra ID**：`getuserrealm` 回 `NameSpaceType: Managed`，地端 AD（`webpro.com`）同步到雲端。
- 已註冊應用程式：
  - Tenant ID：`e333846a-0ee2-4e2d-a13b-efc97851b892`
  - Client ID：`5e85a804-c308-4402-b621-0be6d023a2ac`
  - 對外網域：`ymir.thetainformation.com`
- 使用者另外要求：新增**普通帳號密碼登入**，給沒有公司帳號的人使用。
- Ymir 經 Cloudflare Tunnel 對外（ADR-0006），所以不能用 Windows 整合驗證（Kerberos）。

## 決策

### 1. 企業帳號：Entra ID OIDC（`Ymir:Auth:Oidc`）

- **流程**：
  - Authorization Code + PKCE，由後端換 token（confidential client，client secret 只放在部署 secret）。
  - `ResponseMode=query`：callback 是一般 GET 導向，correlation / nonce cookie 用 `SameSite=Lax`，http://localhost 開發也能用。
  - 登入入口是整頁導向 `GET /api/auth/login?returnUrl=`（不是 XHR）。`returnUrl` 只接受站內相對路徑，防止 open redirect。
- **不保存 IdP token**（`SaveTokens=false`）。驗證 id_token 後，principal 換成 Ymir 自己的 claims（user id、角色），瀏覽器只拿到 Ymir 的 HttpOnly cookie（ADR-0002）。
- **身分鍵**：`iss`（含 tenant）+ `oid`。
  - 不用 `sub`：Entra 的 `sub` 是每個應用程式不同的 pairwise 值。
  - 不用 UPN：UPN 可能更名，只用於顯示。
- **角色以 Entra app role 為準**：token 的 `roles` 含 `Ymir.Admin` 就是 Admin，否則是 User，每次登入同步。Entra 拿掉角色後，下次登入就降級。
- callback 的錯誤（state、nonce 或 token 驗證失敗、帳號停用）一律導回 `/login?error=failed|disabled`，細節只寫 server log（SA §12）。
- **登出只登出 Ymir**，Entra 的 SSO session 保留。

### 2. 本機帳號密碼（`Ymir:Auth:LocalAccounts`，預設開啟）

- **身分與資料表**：使用者仍是 `platform.users`，issuer 為 `urn:ymir:local`，subject 為正規化後的帳號（3～64 個英數字或 `. _ -`，不分大小寫）。密碼存在 `platform.local_credentials`。
- **密碼雜湊**：使用 ASP.NET Core Identity 的 `PasswordHasher`（PBKDF2-HMAC-SHA512，含 salt）。演算法升級時，登入成功會自動重新雜湊。
- **防猜密碼**：
  - 密碼至少 12 個字元；
  - 每個帳號連續失敗 5 次，鎖定 15 分鐘；
  - 每個來源 IP 每分鐘最多 10 次（rate limiter）；
  - 帳號不存在與密碼錯誤回應相同，回應時間也相同（帳號不存在時仍比對一次假雜湊）；
  - 登入端點要求 antiforgery token，防止 login CSRF。
- **帳號由 Admin 建立**，沒有自助註冊。Admin 設定的初始密碼或重設的密碼，使用者第一次登入必須先改：cookie 帶 `ymir:must_change_password`，改完之前其他 API 一律 403 `PASSWORD_CHANGE_REQUIRED`。
- **本機帳號的角色由 Ymir 管理**（建立時指定）。
- **第一個 Admin**：
  - 優先由 Entra 指派 `Ymir.Admin`；
  - 沒有 Entra 時，用管理指令建立：`dotnet Ymir.Api.dll create-local-admin <帳號>`。密碼從 stdin 讀取，不出現在程序參數。

### 3. 每個請求檢查帳號狀態

- Cookie 的 `OnValidatePrincipal` 以 user id 查一次使用者（主鍵查詢）：
  - 停用或已刪除：拒絕 cookie，並 sign out；
  - 角色或名稱變更：換發 cookie。
- **效果**：Admin 停用帳號後，對方的下一個請求就是 401。停用時另外 best effort 做三件事：
  - 取消該使用者執行中的 execution；
  - 撤銷 LiteLLM virtual key（ADR-0004）；
  - 停止 runtime。

### 4. Admin API（`/api/admin/users`，policy `AdminOnly`）

- **端點**：
  - 列表（可搜尋）；
  - 建立本機帳號；
  - 停用、啟用（不能停用自己）；
  - 重設本機帳號密碼。
- 每個動作都寫 audit。
- 企業帳號的角色不在 Ymir 修改。
- 授權矩陣測試加入角色維度：一般使用者呼叫回 403，匿名呼叫回 401。

### 5. 啟動檢查

- 非 Development 至少要有一種登入方式。
- OIDC authority 必須是 https。Development 例外：允許 loopback 的 http，給 Fake OIDC 使用。
- 設定 OIDC 時必須有 client secret。

### 6. 測試用 Fake OIDC（`tests/Ymir.Testing.FakeOidc`）

雲端沙箱與 CI 連不到 Microsoft，所以用 Fake OIDC 模擬 Entra v2，驗證整個流程。它的行為與 Entra 相同：

- 路徑；
- id_token 的 claims：`iss` 含 tenant、`tid`、`oid`、pairwise `sub`、`roles`；
- RS256 簽章；
- PKCE；
- client secret 驗證。

**實際連 Entra 的登入需要在使用者環境驗證。**

## 影響

- 停用立即生效的代價：每個帶 cookie 的請求多一次主鍵查詢。使用者數量級（企業內部）可以接受；之後如有需要，再加短期快取。
- 本機帳號讓沒有公司帳號的人也能使用，但密碼由 Ymir 保管，屬於另一個安全邊界：
  - 需要定期檢視帳號清單；
  - 離職時由 Admin 停用。
- **Client secret 有到期日**（Entra 最長 24 個月），到期前要更新 `deploy/api/.env`（`docs/guides/entra-id.md`）。
- Entra 應用程式的「需要指派使用者」開啟後，只有被指派的人能登入；未指派的人會在 Entra 端被拒絕，不會進到 Ymir。
