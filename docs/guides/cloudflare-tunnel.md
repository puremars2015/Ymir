# 用 Cloudflare Tunnel 對外公開 Ymir

架構決策見 [ADR-0006](../adr/0006-cloudflare-tunnel-public-edge.md)。本指南以 Windows 開發機為例，Linux 指令另外標示。

```
瀏覽器 ──HTTPS──> Cloudflare edge（ymir.example.com）
                     ▲  （cloudflared 主動建立的 outbound 連線，主機不開 inbound port）
                     │
               cloudflared ──HTTP──> 127.0.0.1:5080  Ymir.Api（/api + Angular build）
```

> ⚠️ **目前的限制**
> - 正式環境還沒有登入方式（企業帳號登入在 Sprint 2）。照本指南做完，對外的網站會顯示首頁，但 `/api/*` 一律回「請先登入」。
> - API **不能用 Development 環境對外**（有免密碼的 dev 登入），開啟 `Ymir:PublicEdge` 時會直接拒絕啟動。
> - 正式的 Agent runtime 只支援 Rootless Podman（ADR-0005）；Windows + Docker 只能拿來驗證。

## 1. 事前準備

- Cloudflare 帳號，以及一個已加入 Cloudflare 的網域（DNS 由 Cloudflare 管理），以下以 `ymir.example.com` 為例。
- 安裝 cloudflared：
  - Windows：`winget install --id Cloudflare.cloudflared`
  - Linux：依 Cloudflare 官方文件安裝套件
- 版本要固定，不要自動更新；升級前先在測試環境跑過第 6 節的驗證清單。

## 2. 建立 Named tunnel（只需要做一次）

有兩種方式，擇一即可：

- **方式 A：Token 模式 + container（建議，目前使用中）**：在 Cloudflare dashboard 建立 tunnel 與 Public Hostname，用 Docker 跑 cloudflared。見 [2A](#2a-token-模式--docker)。
- **方式 B：本機設定檔**：用 `cloudflared` CLI 建立 tunnel，ingress 寫在 `config.yml`（下方步驟與第 3 節）。

### 2A. Token 模式 + Docker

1. Cloudflare dashboard → Zero Trust → Networks → Tunnels → 建立 tunnel（Cloudflared），複製畫面上指令中的 token。
2. 在該 tunnel 的 **Public Hostname** 新增一筆：
   - Hostname：`ymir.example.com`
   - Service：`HTTP` / `host.docker.internal:5080`（Windows / macOS Docker Desktop）；Linux 改成 `127.0.0.1:5080` 並在 compose 使用 `network_mode: host`
3. 把 token 寫進 `deploy/cloudflared/.env`（範本 `.env.example`；`.env` 已被 `.gitignore` 排除）：
   ```
   TUNNEL_TOKEN=<token>
   ```
4. 啟動（設定見 [`deploy/cloudflared/compose.yml`](../../deploy/cloudflared/compose.yml)：固定版本、唯讀、drop 全部 capabilities）：
   ```powershell
   cd deploy\cloudflared
   docker compose up -d
   docker compose logs -f      # 看到 "Registered tunnel connection" 即連線成功
   ```

> ⚠️ Token 等同 tunnel 的密碼：
> - 不要直接寫在 `docker run ... --token <token>`：會留在 shell 歷史與 `ps` 輸出中。
> - 不要提交到 repo，也不要放進 API 或 Agent container。
> - 外洩時，到 dashboard 對該 tunnel 重新產生 token，再更新 `.env`。

> ⚠️ Public Hostname 只能指向以 **Production** 啟動的 API（第 4 節）。不要指向 `ng serve`（4200）或 Development 的 API：
> 那等於讓網路上任何人用 dev 登入成為 Admin 並執行 Agent。API 端的防護只在 API 開啟 `Ymir:PublicEdge` 時生效，tunnel 本身擋不住。

`/health`、`/alive`：API 開啟 `Ymir:PublicEdge` 後，經由公開網域進來的請求一律回 404，不需要在 dashboard 另外設定。

### 2B. 本機設定檔（CLI）

```powershell
cloudflared tunnel login                         # 開啟瀏覽器，選擇網域；產生 cert.pem
cloudflared tunnel create ymir                   # 產生 <TUNNEL-UUID>.json（tunnel 憑證）
cloudflared tunnel route dns ymir ymir.example.com   # 建立 CNAME ymir.example.com → <TUNNEL-UUID>.cfargotunnel.com
```

`cert.pem` 與 `<TUNNEL-UUID>.json` 預設放在 `%USERPROFILE%\.cloudflared\`（Linux 為 `~/.cloudflared/`）。
這兩個檔案等同 tunnel 的密碼：**不要放進 repo，也不要複製到 API 或 Agent container 裡**。

## 3. 設定 cloudflared（只適用方式 B）

複製 [`deploy/cloudflared/config.example.yml`](../../deploy/cloudflared/config.example.yml) 到 `%USERPROFILE%\.cloudflared\config.yml`，替換：

- `<TUNNEL-UUID>`：第 2 步 `tunnel create` 印出的 UUID
- `credentials-file`：`<TUNNEL-UUID>.json` 的完整路徑
- `ymir.example.com`：你的網域

驗證規則：

```powershell
cloudflared tunnel ingress validate
cloudflared tunnel ingress rule https://ymir.example.com/api/me   # 應對應到 http://127.0.0.1:5080
cloudflared tunnel ingress rule https://ymir.example.com/health   # 應對應到 http_status:404
```

## 4. 以 Production 設定啟動 API

```powershell
cd web; npm ci; npm run build; cd ..           # 產生 web\dist\ymir-web\browser
dotnet publish src\Ymir.Api -c Release -o out\api

$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:ASPNETCORE_URLS = "http://127.0.0.1:5080"   # 只綁本機，外部只能經由 tunnel 進來
$env:ConnectionStrings__ymir = "Server=localhost,1433;Database=ymir;User Id=...;Password=...;TrustServerCertificate=True"
$env:Ymir__PublicEdge__Enabled = "true"
$env:Ymir__PublicEdge__PublicHostname = "ymir.example.com"
$env:Ymir__Web__RootPath = "$PWD\web\dist\ymir-web\browser"
$env:VibeMaker__Runtime__Provider = "Docker"     # 只限驗證；正式主機用 Podman
dotnet out\api\Ymir.Api.dll
```

Production 不會自動 migrate；第一次啟動前先對資料庫套用 migration（`dotnet ef database update`，兩個 DbContext 都要）。

| 設定 | 說明 |
|---|---|
| `Ymir:PublicEdge:Enabled` | 開啟對外入口的防護；Development 環境會拒絕啟動 |
| `Ymir:PublicEdge:PublicHostname` | 對外主機名稱，也是唯一接受的外部 Host header |
| `Ymir:PublicEdge:KnownProxies` | cloudflared 的來源 IP；預設只信任 `127.0.0.1`、`::1`。Windows / macOS Docker Desktop 經 `host.docker.internal` 轉送後來源是 loopback，用預設即可（以驗證清單第 4 項的 HSTS 確認）；Linux 請用 host network，不要讓 API 綁到 bridge IP |
| `Ymir:Web:RootPath` | Angular build 目錄；設定了卻找不到 `index.html` 會拒絕啟動 |

## 5. 啟動 tunnel

方式 A：`docker compose up -d`（見 2A）。方式 B：

```powershell
cloudflared tunnel run ymir
```

長期執行請改成系統服務（Windows：`cloudflared service install`；Linux：`sudo cloudflared --config /etc/cloudflared/config.yml service install`），
服務模式讀取的設定檔位置依 Cloudflare 官方文件。

## 6. 驗證清單

| # | 驗證 | 預期結果 |
|---|---|---|
| 1 | 瀏覽 `https://ymir.example.com/` | 出現 Ymir 首頁，憑證有效 |
| 2 | 重新整理 `https://ymir.example.com/workspaces/任意值` | 仍是 Ymir 頁面（前端路由回 index.html），不是 404 |
| 3 | `curl -i https://ymir.example.com/api/me` | `401`，ProblemDetails，沒有 stack trace |
| 4 | `curl -I https://ymir.example.com/` | 有 `Strict-Transport-Security` header |
| 5 | `curl -i https://ymir.example.com/health` | `404`（health check 不對外） |
| 6 | `curl -i -X POST https://ymir.example.com/api/dev/login` | 不是 `200`（Production 沒有 dev 登入） |
| 7 | 從另一台機器 `curl http://<主機 IP>:5080/` | 連不上（API 只綁 127.0.0.1） |
| 8 | 在主機上 `curl -H "Host: evil.example.net" http://127.0.0.1:5080/` | `400`（Host header 限制） |

Sprint 2 完成登入後再補：登入後的 cookie 有 `Secure`、SSE 串流經 tunnel 正常、閒置超過 100 秒後 `EventSource` 自動重連且不遺失事件。

## 疑難排解

| 症狀 | 原因 / 處理 |
|---|---|
| 瀏覽器顯示 `400 Bad Request` | Host 不是 `PublicHostname`；確認 `Ymir__PublicEdge__PublicHostname` 與 DNS 名稱一致 |
| Cloudflare `502 Bad Gateway` | API 沒有在 `127.0.0.1:5080` 執行，或 ingress 的 port 不一致；container 模式確認 service 是 `host.docker.internal:5080`（Linux 用 host network） |
| 回應沒有 `Strict-Transport-Security`；或稽核紀錄的 IP 都是 `127.0.0.1`、cookie 沒有 `Secure` | API 沒收到可信任的 `X-Forwarded-*`：cloudflared 的來源 IP 不在 `KnownProxies`（例如 cloudflared 跑在 container） |
| API 啟動失敗：`not allowed in the Development environment` | 預期行為；對外必須用 Production（或其他非 Development 環境） |
| SSE 每 100 秒左右斷一次 | Cloudflare 的閒置逾時；`EventSource` 會帶 `Last-Event-ID` 自動續傳，不會遺失事件 |
