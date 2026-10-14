# ADR-0024：Google 帳號 SSO 登入

- 狀態：已採納（延伸 ADR-0009；沿用 ADR-0002 的 BFF + HttpOnly Cookie）
- 日期：2026-10-14

## 背景

ADR-0009 定案了兩種登入方式：企業帳號（Entra ID OIDC）與 Admin 建立的本機帳號。使用者要求新增第三種：**以 Google 帳號 SSO 登入**（例如使用 Google Workspace 的單位，或個別外部協作者），不必再由 Admin 發本機密碼。

與 Entra 不同的地方：

- Entra 有「需要指派使用者」，沒被指派的人在 IdP 端就被擋下；Google 的 OAuth client **任何 Google 帳號都能完成授權**。
- Google 的 `sub` 是帳號不變、不分應用程式的識別（不是 Entra 那種 pairwise 值），沒有 `oid`、`tid`、`roles`。
- Workspace 帳號的 id_token 有 `hd`（hosted domain）；個人 Gmail 沒有。email 網域本身不可信：任何人都能用其他網域的 email 註冊 Google 帳號。

## 決策

### 1. 流程與設定（`Ymir:Auth:Google`）

- 與 Entra 相同：Authorization Code + PKCE，由後端換 token（confidential client），不保存 IdP token（`SaveTokens=false`），principal 換成 Ymir 自己的 claims。共用設定在 `OidcSignIn.ApplyCommon`。
- Scheme `google`、callback `/signin-google`，登入入口 `GET /api/auth/login/google?returnUrl=`（整頁導向，`returnUrl` 只接受站內相對路徑）。
- 送往 Google 的授權請求省略 `response_mode`（授權碼預設就以 query 回傳），並帶 `prompt=select_account`，讓瀏覽器登入多個 Google 帳號的人能選擇正確的帳號。
- **只在部署設定**（`deploy/api/.env`）：`ClientId`、`ClientSecret`、白名單、Admin 名單、`DisplayName`。這次不做管理介面設定（ADR-0010 的動態 scheme）；之後有需要再以新 ADR 延伸。Client secret 的規則與 Entra 相同：只放部署 secret，不得進版控、回應、稽核或 log。
- `Authority` 預設 `https://accounts.google.com`，只有 Development / 測試改成 Fake OIDC（loopback http）。

### 2. 白名單（必填）

- 必須設定 `AllowedDomains`（Workspace 網域，以 `hd` claim 比對）或 `AllowedEmails`（個別 email）至少一項，否則**拒絕啟動**，避免任何 Google 帳號都能登入。
- 只信任 `hd`，不以 email 字尾判斷網域。
- `email_verified` 必須為 true。
- 不符合時**不建立使用者**，導回 `/login?error=not_allowed`；稽核 `auth.login.google`（Denied），不記錄被拒絕者的 email（可能是任何人的個人帳號）。

### 3. 身分與角色

- 身分鍵：`iss` + `sub`。issuer 一律存成 `https://accounts.google.com`（Google 的 id_token 可能是沒有 scheme 的 `accounts.google.com`，兩者都接受驗證）。email 可能變更，只用於白名單比對與顯示（`AccountName`、`Email`）。
- 角色：email 在 `AdminEmails` 為 Admin，否則 User，**每次登入同步**（與 Entra app role 的語意相同）；`AdminEmails` 中的帳號視為已在白名單內。
- `AuthMethod.Google`：前端顯示「Google 帳號」；不能變更 Ymir 密碼；不視為 Entra 帳號，所以 OneDrive connector（ADR-0013）不會要求 oid 相符。
- 停用規則與其他帳號相同（ADR-0009 §3）：每個請求檢查狀態，停用的帳號再登入時導回 `/login?error=disabled`。

### 4. 測試

雲端沙箱與 CI 連不到 Google。Fake OIDC 另提供 `{root}/google`（discovery、authorize、token、JWKS），id_token 模擬 Google：數字 `sub`、`email_verified`、非 `gmail.com` 網域帶 `hd`；`login_hint` 加 `;unverified` 時 `email_verified=false`；請求帶 `response_mode` 時拒絕。**實際連 Google 的登入需要在使用者環境驗證。**

## 影響

- 登入頁多一個「以 Google 帳號登入」按鈕（有企業帳號時是次要按鈕）。
- 部署時要在 Google Cloud Console 建立 OAuth client（Web application），redirect URI 加上 `https://<網域>/signin-google`；外部使用者類型的 consent screen 需要發布（見 `docs/guides/google-login.md`）。
- 允許外部 Google 帳號擴大了可登入的範圍：白名單由部署設定管理，需要定期檢視；離職或結束合作時由 Admin 在 Ymir 停用，並從白名單移除。
