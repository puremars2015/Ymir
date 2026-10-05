# ADR-0006：以 Cloudflare Tunnel 作為 Ymir 平台的對外入口

- 狀態：已採納
- 日期：2026-10-04

## 背景

Ymir 需要能從網際網路連到（使用者決定：**對外的是 Ymir 平台本身**，使用 **Named tunnel + 自有網域**，網址**完全公開**，
不加 Cloudflare Access；存取控制由平台自己的登入負責）。目前沒有固定 IP、也不想在防火牆開 inbound port。

SA §16 的部署拓樸在 API 前面有一層 Reverse Proxy / HTTPS；ADR-0002 要求 Angular 與 API 同源，讓 SSE 的 `EventSource` 自動帶 cookie。

## 決策

1. **cloudflared 取代 SA §16 的 Reverse Proxy / HTTPS**：cloudflared 主動向 Cloudflare 建立 outbound 連線，HTTPS 憑證在 Cloudflare edge 終結，
   主機不需要開任何 inbound port。API 只綁在 `127.0.0.1`，外部只能經由 tunnel 進來。
2. **同源、單一 ingress**：API 直接提供 Angular build（`Ymir:Web:RootPath`），tunnel 只有一條規則 `ymir.example.com → http://127.0.0.1:5080`；
   `/health`、`/alive` 在 ingress 回 404，不對外。設定範本：[`deploy/cloudflared/config.example.yml`](../../deploy/cloudflared/config.example.yml)。
3. **API 端模組 `Ymir.Edge`**（`src/Ymir.Edge/`），以 `Ymir:PublicEdge` 設定開啟：
   - 只信任來自 cloudflared（預設 loopback，可用 `KnownProxies` 指定）的 `X-Forwarded-For` / `X-Forwarded-Proto`，且只採用最後一個值（`ForwardLimit = 1`），
     讓 `Request.IsHttps`（cookie / XSRF 的 `Secure`）與稽核看到的用戶端 IP 正確，又不被用戶端偽造。
   - Host header 只接受 `PublicHostname` 與 localhost，擋掉直接以其他網域（例如 `*.trycloudflare.com`）連進來的請求。
   - 回應加上 HSTS。
   - **Development 環境拒絕開啟**：Development 有免密碼的 `/api/dev/login`（任何人都能以 Admin 登入）與沒有隔離的 Local runtime，對外等於把主機交出去。
4. Tunnel 憑證（credentials JSON / token）不進版控（`.gitignore`），也不放進 API 設定；只有 cloudflared 讀得到。
5. **兩種 tunnel 管理方式都支援**：本機 `config.yml`（ingress 可版控、可 `ingress validate`），或 dashboard 管理的 **token 模式**
   加 container（[`deploy/cloudflared/compose.yml`](../../deploy/cloudflared/compose.yml)：token 從 `.env` 以環境變數傳入、固定版本、唯讀、drop 全部 capabilities）。
   因為 token 模式的 ingress 不在版控裡，`/health`、`/alive` 改由 API 擋：經由公開網域進來的一律 404，只有本機 Host 可用。

## 影響

- **在 Sprint 2（OIDC 登入）完成前，對外的站台只能顯示「請先登入」**：正式環境沒有 dev 登入，也還沒有企業帳號登入。tunnel 與 API 端防護可以先建好並驗證。
- 正式環境的 Agent runtime 仍只支援 Rootless Podman（ADR-0005）；用 Windows + Docker 對外屬於開發驗證，不可長期公開。
- Cloudflare 對閒置超過約 100 秒的 HTTP 連線會中斷（524）。SSE 有 `Last-Event-ID` 斷線續傳，`EventSource` 會自動重連、不遺失事件；
  之後加入 SSE 心跳（開發規劃 §5）可減少重連。
- 完全公開代表登入頁、靜態檔、匿名端點都會被掃描；授權預設拒絕（fallback policy）與授權矩陣測試仍是主要防線。
  需要時可再加 Cloudflare WAF / rate limiting，或改成 Cloudflare Access（需新 ADR）。
- 多實例部署時，cloudflared 可以同時跑多個 replica 連到同一個 tunnel；Data Protection key 共用的要求不變（ADR-0002）。
