# ADR-0002：以 BFF + Cookie 做認證，讓 SSE 可直接使用

- 狀態：已採納
- 日期：2026-10-02

## 背景

SA §4 規劃 Angular 取得 Access Token 後呼叫 API（SPA + Bearer）。但 SA §10 的 Agent 串流使用 SSE，而瀏覽器原生 `EventSource` **無法設定 `Authorization` header**。可行方案：

| 方案 | 問題 |
|---|---|
| Token 放 query string | 會出現在反向代理 / 存取 log、瀏覽器歷史 |
| 改用 `fetch` 自行解析 SSE | 失去 `EventSource` 的自動重連與 `Last-Event-ID`，要自己實作 |
| **BFF + HttpOnly Cookie** | 需要 CSRF 防護（SameSite + antiforgery） |

此外 SPA 持有 Access / Refresh Token 會增加 XSS 竊取風險。

## 決策

1. 採 **Backend-for-Frontend**：`Ymir.Api` 擔任 OIDC Client（Authorization Code + PKCE），登入成功後發 **HttpOnly、Secure、SameSite=Lax** 的 Cookie；Angular 不接觸任何 token。
2. Angular 與 API **同源部署**（反向代理把 `/api` 轉給 API），SSE 直接用 `EventSource`，自動帶 Cookie。
3. 狀態變更的 REST 端點（POST / PUT / DELETE）啟用 antiforgery token。
4. 開發與測試環境提供 **Dev Authentication Handler**（只在 `Development` 環境註冊），可指定測試使用者與角色，不依賴企業 IdP。
5. USER 的外部識別以 **`(issuer, subject)`** 為唯一鍵（Entra ID 用 `oid`），不使用 UPN / 帳號名稱（可能更名）。SA 的 `external_account` 欄位保留作顯示用途。
6. 傳統 AD / LDAP 現場由 `IIdentityProvider` adapter 封裝（SA §4.1 已要求）。

## 影響

- API 需要 session/cookie 設定與 antiforgery；多實例部署時 Data Protection key 要共用（存 DB 或共用目錄）。
- 若未來有非瀏覽器用戶端（CLI、其他服務），另外開 Bearer token 驗證方案，與 Cookie 方案並存。
