# Google 帳號登入（SSO）

架構決策見 [ADR-0024](../adr/0024-google-account-sso.md)。Google 登入可以與企業帳號（[entra-id.md](entra-id.md)）、本機帳號並存，也可以單獨使用。

## 1. 在 Google Cloud Console 建立 OAuth client

1. https://console.cloud.google.com → 選擇（或建立）專案 →「API 和服務」→「OAuth 同意畫面」：
   - 只給自己 Workspace 的人用：使用者類型選**內部**。
   - 要讓 Workspace 以外的帳號（例如個人 Gmail 的協作者）登入：選**外部**，並把應用程式**發布**（測試狀態只允許列出的測試使用者）。
   - 範圍只需要 `openid`、`email`、`profile`（不需要送審的敏感範圍）。
2. 「憑證」→「建立憑證」→「OAuth 用戶端 ID」→ 應用程式類型 **網頁應用程式**。
3. 「已授權的重新導向 URI」：
   - `https://<你的 Ymir 網域>/signin-google`（正式）
   - `http://localhost:5080/signin-google`（本機開發，選用）
4. 記下 Client ID 與 Client secret。**Client secret 只放部署主機的 `deploy/api/.env`**，不要貼到對話、issue 或版控。

## 2. 設定 Ymir（`deploy/api/.env`）

```
Ymir__Auth__Google__ClientId=<Client ID>.apps.googleusercontent.com
Ymir__Auth__Google__ClientSecret=<Client secret>
# 白名單至少一項（否則 API 拒絕啟動）
Ymir__Auth__Google__AllowedDomains__0=example.com
Ymir__Auth__Google__AllowedEmails__0=partner@gmail.com
# Admin（每次登入同步；不在名單即為 User）
Ymir__Auth__Google__AdminEmails__0=it-admin@example.com
```

| 設定 | 預設 | 說明 |
|---|---|---|
| `Ymir__Auth__Google__AllowedDomains__N` | — | Google Workspace 網域，以 id_token 的 `hd` 比對；個人 Gmail 沒有 `hd`，不會因 email 字尾而通過 |
| `Ymir__Auth__Google__AllowedEmails__N` | — | 個別允許的 Google 帳號 email（不分大小寫），必須是 Google 已驗證的 email |
| `Ymir__Auth__Google__AdminEmails__N` | — | 這些 email 為 Admin（也視為已允許） |
| `Ymir__Auth__Google__DisplayName` | `Google 帳號` | 登入按鈕顯示「以 Google 帳號登入」 |

重啟 API 後，登入頁出現「以 Google 帳號登入」。不在白名單的帳號會看到「此 Google 帳號未獲准使用」，Ymir 不會建立使用者，稽核紀錄有一筆 `auth.login.google`（Denied）。

說明：

- 同一個 Google 帳號以 `sub` 識別，改 email 仍是同一位使用者；但白名單與 Admin 名單以**目前的 email** 比對。
- 從白名單移除只會擋下之後的登入；已登入的 cookie 仍有效到過期（最長 8 小時、滑動延長）。要立即生效，請同時在「管理 → 使用者」停用該帳號。
- 經 Cloudflare Tunnel 對外時，`/signin-google` 與 `/signin-oidc` 一樣經由同一個網域進來（ADR-0006）。

## 3. 本機開發 / 驗證（Fake OIDC）

沙箱與 CI 連不到 Google，用 Fake OIDC 的 Google 模擬：

```bash
dotnet run --project tests/Ymir.Testing.FakeOidc      # Fake Google authority：http://127.0.0.1:5299/google
Ymir__Auth__Google__Authority=http://127.0.0.1:5299/google \
Ymir__Auth__Google__ClientId=ymir-dev Ymir__Auth__Google__ClientSecret=ymir-dev-secret \
Ymir__Auth__Google__AllowedDomains__0=acme.test \
  dotnet run --project src/Ymir.Api
cd web && npm start    # /signin-google 由 dev proxy 轉給 API
```

Fake 登入頁輸入任意 email：`gmail.com` 以外的網域視為 Workspace 帳號（帶 `hd`）。實際連 Google 的登入需要在使用者環境驗證。
