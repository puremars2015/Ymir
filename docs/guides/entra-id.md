# 企業帳號（Entra ID）與本機帳號登入

架構決策見 [ADR-0009](../adr/0009-entra-id-and-local-accounts.md)。

## 你們的設定值

| 項目 | 值 |
|---|---|
| Tenant ID（目錄識別碼） | `e333846a-0ee2-4e2d-a13b-efc97851b892` |
| Client ID（應用程式識別碼） | `5e85a804-c308-4402-b621-0be6d023a2ac` |
| Authority | `https://login.microsoftonline.com/e333846a-0ee2-4e2d-a13b-efc97851b892/v2.0` |
| 對外網域 | `ymir.thetainformation.com` |
| Admin app role | `Ymir.Admin` |

> Client secret **不放在這裡**，也不要貼到對話或 issue 裡。只填在部署主機的 `deploy/api/.env`（`.gitignore` 已排除）或 `/etc/ymir/api.env`。

## 1. Entra 應用程式檢查清單（已註冊，確認以下項目）

到 https://entra.microsoft.com → 身分識別 → 應用程式 → 應用程式註冊 → `Ymir`：

1. **驗證** → 平台「Web」的重新導向 URI 兩個都要有（✅ 已完成，2026-10-06 使用者確認）：
   - `https://ymir.thetainformation.com/signin-oidc`（正式）
   - `http://localhost:5080/signin-oidc`（本機開發）
2. **憑證及祕密**：已建立用戶端密碼，記下**到期日**，到期前要換新（見第 4 節）。
3. **應用程式角色**：值為 `Ymir.Admin` 的角色，允許的成員類型包含使用者 / 群組。
4. **企業應用程式** → `Ymir`：
   - 內容 → 「需要指派使用者」設為**是**（只有被指派的人能登入）。
   - 使用者和群組 → 指派可以使用 Ymir 的人或群組；管理員指派 `Admin` 角色。
5. **API 權限**：預設的 `User.Read`（委派）即可，不需要其他權限。

## 2. 設定 Ymir

在 `deploy/api/.env`（Linux）或 `deploy/api/.env.windows`（Windows）填入：

```
Ymir__Auth__Oidc__Authority=https://login.microsoftonline.com/e333846a-0ee2-4e2d-a13b-efc97851b892/v2.0
Ymir__Auth__Oidc__ClientId=5e85a804-c308-4402-b621-0be6d023a2ac
Ymir__Auth__Oidc__ClientSecret=<Entra 上產生的用戶端密碼「值」>
```

| 設定 | 預設 | 說明 |
|---|---|---|
| `Ymir__Auth__Oidc__AdminRole` | `Ymir.Admin` | token 的 `roles` 含此值就是 Admin，每次登入同步 |
| `Ymir__Auth__Oidc__DisplayName` | `公司帳號` | 登入按鈕顯示「以公司帳號登入」 |
| `Ymir__Auth__LocalAccounts__Enabled` | `true` | 本機帳號密碼登入；設為 `false` 則只能用企業帳號 |
| `Ymir__Auth__LocalAccounts__LoginAttemptsPerMinute` | `10` | 每個來源 IP 每分鐘可嘗試帳號密碼登入的次數 |

- 正式環境經 Cloudflare Tunnel 對外時，`/signin-oidc` 會透過同一個網域進來。API 依可信任的 `X-Forwarded-Proto` 產生 `https://ymir.thetainformation.com/signin-oidc`（ADR-0006）。
- 本機開發（`dotnet run --project src/Ymir.Api`，http://localhost:5080）可以直接用你們的 Entra 登入：
  1. 設定上面三個環境變數；
  2. 前端用 `npm start`（4200），`/signin-oidc` 已由 dev proxy 轉給 API。

## 3. 本機帳號（沒有公司帳號的人）

1. Admin 在 Ymir 左下角「管理」→ 使用者管理 →「新增本機帳號」，輸入帳號、顯示名稱、角色、初始密碼（至少 12 字元）。
2. 把帳號與初始密碼交給對方。對方第一次登入時必須改成自己的密碼。
3. 忘記密碼：Admin 在使用者管理按「重設密碼」，對方下次登入時必須再改一次。
4. 連續輸錯 5 次會鎖定 15 分鐘。

**還沒有任何 Admin 時**，例如還沒設定 Entra，用管理指令建立第一個本機 Admin。密碼從鍵盤輸入，不會出現在指令列：

```bash
# API 在容器內（Linux，rootful Podman）
sudo podman exec -it ymir-api dotnet Ymir.Api.dll create-local-admin admin "系統管理員"
# 或直接在主機上
dotnet out/api/Ymir.Api.dll create-local-admin admin "系統管理員"
```

## 4. 更換 client secret（到期前）

1. Entra → 應用程式註冊 → `Ymir` → 憑證及祕密 → 新增用戶端密碼。
2. 更新 `deploy/api/.env` 的 `Ymir__Auth__Oidc__ClientSecret`，重新啟動 API：
   - Linux：`sudo systemctl restart ymir-api`
   - Windows：`docker compose -f compose.windows.yml up -d`
3. 確認可以登入後，刪除舊的用戶端密碼。

## 5. 驗證清單

> 雲端開發沙箱連不到 Microsoft，下面這些需要在你的環境實際操作。Ymir 的登入流程已用模擬 Entra 的 Fake OIDC 完整測試過（`npm run e2e:auth`）。

| # | 操作 | 預期結果 |
|---|---|---|
| 1 | 開啟 `https://ymir.thetainformation.com/` | 導向登入頁，有「以公司帳號登入」按鈕與帳號密碼欄位 |
| 2 | 按「以公司帳號登入」，用已指派的帳號登入 | 回到 Ymir 主畫面，左下角顯示你的名字 |
| 3 | 用有 `Ymir.Admin` 角色的帳號登入 | 左下角出現「管理」 |
| 4 | 用**沒有被指派**的帳號登入 | Entra 顯示沒有存取權限（不會進到 Ymir） |
| 5 | 在使用者管理建立本機帳號，用無痕視窗登入 | 要求先變更密碼，改完進入主畫面 |
| 6 | 停用第 5 步的帳號 | 對方下一個操作就被登出，之後無法登入 |
| 7 | 瀏覽器開發者工具 → Application → Cookies | 只有 `ymir.auth`（HttpOnly、Secure）等 Ymir 自己的 cookie，沒有 IdP token |

## 疑難排解

| 症狀 | 原因 / 處理 |
|---|---|
| Entra 顯示 `AADSTS50011`（redirect URI 不符） | 第 1 節的重新導向 URI 沒有加，或網域 / http、https 不一致 |
| Entra 顯示 `AADSTS7000215`（client secret 無效） | `ClientSecret` 填成「祕密識別碼」而不是「值」，或已過期 |
| 登入後回到 `/login?error=failed` | API log 有 `OIDC sign-in failed` 與原因（state / nonce / token 驗證）；常見原因是 cookie 被擋，或伺服器時間不準 |
| 有 `Ymir.Admin` 卻沒有「管理」 | 角色是在登入時同步的：登出後再登入；確認角色是在**企業應用程式 → 使用者和群組**指派的 |
| API 啟動失敗：`Ymir:Auth:Oidc:ClientSecret is required` | 設定了 Authority / ClientId 但沒有 secret |
