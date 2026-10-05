# Sprint 2 · 正式認證

[← 回看板列表](README.md) ・ 規則與格式見 [README](README.md#留言規則)

---

## 📌 置頂：狀態總覽

> 最後更新：2026-10-05 11:13 ・ 狀態：**⏳ 尚未開始（等待決定）**

**目標**：以企業帳號登入（OIDC / Entra ID，經由 BFF，ADR-0002），完成 Admin / User 權限與帳號停用流程。

**Done Definition**：使用者以企業帳號登入、瀏覽器不持有任何 token；Admin 可以停用帳號，被停用的帳號立即無法使用 API 與建立 execution（SA 驗收條件 #1）。

| 工作項目 | 狀態 | 備註 |
|---|---|---|
| OIDC 登入（Authorization Code + PKCE，後端換 token、只發 cookie） | ⏳ | **需先確認 IdP 類型** |
| `IIdentityProvider`：IdP claims → `ExternalIdentity`（issuer + subject / oid） | ⏳ | 傳統 AD / LDAP 改用 adapter |
| 每個請求驗證使用者狀態（停用後既有 cookie 立即失效） | ⏳ | Sprint 1 只在建立 execution 時檢查 |
| Admin API：使用者列表、停用 / 啟用、角色設定（寫 audit） | ⏳ | SA §4 |
| Angular：企業帳號登入按鈕、Admin 使用者管理頁 | ⏳ | |
| 授權矩陣加入角色維度（User 不能呼叫 Admin API） | ⏳ | |
| Docker 作為開發 / 驗證用 runtime（Windows Docker Desktop） | ✅ | ADR-0005；見 [#003](#003--新增-docker-runtime可用-windows-docker-desktop-開發與驗證) |
| 使用者在 Windows 上依指南實機驗證 | ⏳ | [docs/guides/windows-docker.md](../guides/windows-docker.md) 第 6 節驗證清單 |
| Cloudflare Tunnel 對外入口（`Ymir.Edge` 模組） | ✅ | ADR-0006；見 [#004](#004--新增-cloudflare-tunnel-對外入口模組ymiredge) |
| 使用者建立 Named tunnel 並依指南驗證 | 🚧 | 已建立 token 模式 tunnel（token 存於本機 `deploy/cloudflared/.env`，未提交）；待在使用者主機 `docker compose up` 並跑 [指南](../guides/cloudflare-tunnel.md) 第 6 節 |
| 正式主機用完整 Containerfile 重跑 **Rootless Podman** 驗證 | ⏳ | 目前沒有 Linux 主機；可先在 WSL 2 Ubuntu 裝 Podman 驗證（見指南「效能建議」） |

**開工前要先有的決定**：企業 IdP 類型（Entra ID / ADFS / 純 LDAP）與測試用的 App 註冊資訊（client id、redirect URI）。

---

## 💬 留言區

### #005 · Tunnel 改用 token 模式 + Docker；API 端擋 health check

> 👤 **Claude（AI）** · 🕒 2026-10-05 11:13 · `🚧進度`

使用者提供了 dashboard 建立的 tunnel（token 模式），並以 Docker 執行 cloudflared。

- **[`deploy/cloudflared/compose.yml`](../../deploy/cloudflared/compose.yml)**：cloudflared container 的設定。
  - 版本固定為 `2026.9.3`，不用 `latest`。
  - 唯讀、drop 全部 capabilities、`no-new-privileges`。
  - `TUNNEL_TOKEN` 從同目錄的 `.env` 以環境變數傳入，不寫在 `docker run --token`，所以不會出現在 shell 歷史與 `ps` 中。
- **Token**：依使用者要求寫進本機 `deploy/cloudflared/.env`，檔案權限 600。
  - `.gitignore` 原本的 `.env` 規則已涵蓋，另外加了明確的 `deploy/cloudflared/.env`。
  - 範本為 `.env.example`。
  - 提交前檢查過：所有 commit 都不含 token。
- **API 端擋 `/health`、`/alive`**：token 模式的 ingress 在 dashboard，不在版控裡，因此改由 API 擋。經由公開網域進來的一律回 404，用 localhost 監控仍可使用（ADR-0006 第 5 點）。
- 指南新增「2A. Token 模式 + Docker」，並補上兩點：
  - Docker Desktop 用 `host.docker.internal:5080`，Linux 用 host network。
  - **警告不可把 Public Hostname 指向 `ng serve` 或 Development 的 API**。tunnel 本身擋不住，API 端的防護只在開啟 `Ymir:PublicEdge` 時生效。

**驗證（實際跑過）**
- `dotnet format` 通過；Release build 通過。
- `dotnet test --solution Ymir.slnx`：**121 項全部通過**，新增 5 項 health 隱藏測試。
- `docker compose config`：有 `.env` 時解析成功；沒有 token 時明確報錯。
- 確認映像檔標籤 `cloudflare/cloudflared:2026.9.3` 存在。
- `git check-ignore` 確認 `.env` 被排除。

**沒有驗證的**：
- 沒有在沙箱啟動這個 tunnel。啟動會把使用者的公開網域接到沙箱，必須在使用者自己的主機上執行。
- Docker Desktop 經 `host.docker.internal` 轉送後，API 看到的來源是否為 loopback，需要用驗證清單第 4 項（HSTS header）確認。

`⚠️發現`
1. 沙箱是暫時的環境，**`.env` 只存在這個 session 的 container 裡**，不會同步到使用者電腦。使用者需要在自己的 clone 依 `.env.example` 建立 `.env`。
2. Token 已出現在對話紀錄中。若擔心外洩，可在 dashboard 對該 tunnel 重新產生 token，再更新 `.env`。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #004 · 新增 Cloudflare Tunnel 對外入口模組（Ymir.Edge）

> 👤 **Claude（AI）** · 🕒 2026-10-05 00:26 · `✅完成`

依使用者的決定：**對外的是 Ymir 平台本身**，使用 **Named tunnel + 自有網域**，**完全公開**（不加 Cloudflare Access，靠平台自己的登入）。決策見 [ADR-0006](../adr/0006-cloudflare-tunnel-public-edge.md)。

- **新模組 `src/Ymir.Edge/`**，以 `Ymir:PublicEdge` 開啟：
  - 只信任 cloudflared（預設 loopback）送來的 `X-Forwarded-For` / `X-Forwarded-Proto`，只取最後一個值，避免用戶端偽造。
  - Host header 只接受公開網域與 localhost。
  - 加上 HSTS。
  - **Development 環境直接拒絕啟動**，因為 dev 登入與 Local runtime 不能對外。
- **`Ymir:Web:RootPath`**：API 直接提供 Angular build，同源（ADR-0002），所以 tunnel 只需要一條 ingress 規則；沒有對應端點的 `/api` 網址不會回 HTML。
- 設定範本 [`deploy/cloudflared/config.example.yml`](../../deploy/cloudflared/config.example.yml)：`/health`、`/alive` 不對外；其他網域一律回 404。
- 操作指南 [`docs/guides/cloudflare-tunnel.md`](../guides/cloudflare-tunnel.md)：Windows 步驟、設定表、8 項驗證清單、疑難排解。
- tunnel 憑證已加入 `.gitignore`；CLAUDE.md 安全紅線補上對外規則。

**驗證（實際跑過）**
- `dotnet format --verify-no-changes` 通過；Release build 通過。
- `dotnet test --solution Ymir.slnx`：**116 項全部通過**（新增 22 項 Edge 測試）。測試涵蓋：
  - 可信任 / 不可信任的 proxy
  - 偽造 `X-Forwarded-For`
  - 自訂 KnownProxies
  - Host 限制
  - Development 拒絕啟動
  - 無效設定
  - SPA 路由
  - `/api` 不回 HTML 且仍需登入
- 以 **Production** 環境實際啟動 Ymir.Api，帶 Angular build，結果：
  - 公開網域加 `X-Forwarded-Proto: https`：`/` 回 200 HTML，且有 HSTS。
  - 前端路由回 index.html。
  - 未登入呼叫 `/api/me` 回 401。
  - `/api/dev/login` 不存在，回 401。
  - Host 不對的請求回 400。
  - Development 加 PublicEdge 時拒絕啟動。
- 用 `cloudflared 2026.9.3 tunnel ingress validate` / `ingress rule` 驗證範本：規則合法；`/health` 和其他網域回 404；其他請求轉到 `127.0.0.1:5080`。

**沒有驗證的**：沒有實際連上 Cloudflare 建立 tunnel，因為需要使用者的帳號與網域。SSE 經 Cloudflare 的長連線、約 100 秒閒置逾時後的重連，要等 Sprint 2 有登入後才能驗證。

`⚠️發現`
1. **在 Sprint 2（OIDC）完成前，對外網站只能顯示首頁與「請先登入」**：正式環境沒有 dev 登入，也還沒有企業帳號登入。
2. 正式 runtime 仍只支援 Rootless Podman（ADR-0005）；Windows 加 Docker 對外只能短期驗證。
3. 完全公開會被掃描；目前的防線是授權預設拒絕與授權矩陣測試。需要時可以加 Cloudflare WAF / rate limit，或改用 Cloudflare Access（需要新 ADR）。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #003 · 新增 Docker runtime，可用 Windows Docker Desktop 開發與驗證

> 👤 **Claude（AI）** · 🕒 2026-10-04 18:02 · `✅完成`

回覆 [#001](#001--sprint-2-看板開張) 第 3 點：目前沒有 Linux 主機，改用 **Windows 上的 Docker** 代替做開發與驗證；**正式環境仍是 Rootless Podman**（[ADR-0005](../adr/0005-docker-for-development.md)）。

- `VibeMaker:Runtime:Provider` 新增 `Docker`（`Podman` / `Docker` / `Local`），兩種 container engine 共用同一套程式與安全設定。
- 差異只有使用者對應：Podman 用 `keep-id`；Docker 用 `--user 1000:1000`，建立 container 前以一次性 container（不連網路、只掛兩個目錄）把掛載目錄改成 uid 1000。
- 掛載改用 `--mount`，Windows 路徑（`C:\...`）不會被冒號誤判。
- Windows 上不能用 `Local` runtime（需要 `sh`），API 會拒絕啟動並提示改用 `Docker`。
- 新增 [Windows 指南](../guides/windows-docker.md)：SQL Server、image、Fake LLM、API 設定、驗證清單、疑難排解。

**沙箱實測（Docker 29.6.2，PoC 走 Docker provider）**：
| 項目 | 結果 |
|---|---|
| Agent 在 Workspace 建立檔案 | ✅ 檔案擁有者 uid 1000 |
| 同一對話續接 | ✅ 收到第 2 則使用者訊息 |
| Container 刪除重建後續接 | ✅ 收到第 3 則 |
| 安全設定 | ✅ `User=1000:1000`、唯讀 root fs、`CapDrop=[ALL]`、`no-new-privileges`、`--init` |
| 資源限制 | ✅ `PidsLimit=512`、`Memory=2GiB`、`NanoCpus=1e9`（root daemon 有套用） |
| Workspace 隔離 | ✅ 另一個 Workspace 看不到檔案 |
| Podman 回歸（改用 `--mount` 後） | ✅ rootless Podman 仍可建檔 |

⚠️ 實測中發現並修正：Docker 沒有 `keep-id`，第一次跑時 container 內的 agent 無法寫入 `/agent-state`（Permission denied）→ 加上 chown 步驟後通過。
⚠️ 另一個發現：雲端 session 重啟後，podman 仍顯示 SQL Server container 為 Up 但程序已不在，造成整合測試全部連不上 → 啟動 hook 改為以 1433 port 判斷並重啟。

驗證：全部 94 個測試 ✅（單元 55：兩種 engine 的安全旗標、chown 輔助 container；整合 39）、`dotnet format` ✅。

📝 沒驗證到（Docker 本質上做不到）：rootless、`keep-id`、SELinux、正式主機 cgroups delegation。
💡 建議：在 WSL 2 Ubuntu 裡裝 Podman，就能在不需要額外主機的情況下驗證 rootless Podman。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #002 · Sprint 1 合併到 main，GitHub CI 全綠

> 👤 **Claude（AI）** · 🕒 2026-10-04 10:14 · `📢公告`

- Sprint 1 經 [PR #2](https://github.com/puremars2015/Ymir/pull/2) 合併到 main。
- 合併前 GitHub CI 三個 job 全部通過，包含**第一次在 GitHub 上跑**的步驟：
  - .NET build & test：SQL Server service container + 真實 Pi 整合測試 ✅
  - Angular：API 型別漂移檢查、lint、test、build ✅
  - Agent runtime image：完整 Containerfile 建置與冒煙測試 ✅

**問答紀錄：每個使用者的上下文存在哪？**
- 給人看的對話紀錄：SQL Server `vibemaker.messages`（另有 `execution_events` 供 SSE 續傳），每筆都有擁有者。
- 給 Agent 用的上下文：Pi session 檔 `{WorkspaceRoot}/{workspaceId}/agent-state/sessions/*.jsonl`，一個對話一個檔，以 `agent_sessions.id` 作為 Pi 的 `--session-id` 續接。
- 以 Workspace 為隔離單位；LiteLLM / 模型端不保存上下文。

⚠️ 兩個缺口（建議排進 Sprint 4）：
1. 同一 Workspace 的多個對話共用 `agent-state/`，Agent 技術上讀得到同 Workspace 其他對話的 session 檔（同一使用者，不算越權；若要對話之間也隔離需改設計）。
2. Pi session 檔只存在檔案系統：遺失時「從 `messages` 重建上下文」尚未實作；Workspace 存放位置、備份、保留政策仍待確認（Sprint 0 #005）。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #001 · Sprint 2 看板開張

> 👤 **Claude（AI）** · 🕒 2026-10-03 23:13 · `❓待決定`

Sprint 1 已完成（[Sprint 1 看板](board-sprint-1.md)）。Sprint 2 的主要工作依賴企業 IdP 的資訊，請回覆：

1. IdP 類型：Entra ID（建議）/ ADFS / 純 LDAP？
2. 能否提供測試用的 App 註冊（client id、tenant、允許的 redirect URI，例如 `https://<host>/signin-oidc`）？
3. 有沒有可以實際跑 Podman 的 Linux 主機（cgroups v2）來完成 Podman 驗證？

不擋開工的部分（每個請求驗證使用者狀態、Admin API、授權矩陣角色維度）可以先做。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---
