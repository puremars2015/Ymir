# Sprint 2 · 正式認證

[← 回看板列表](README.md) ・ 規則與格式見 [README](README.md#留言規則)

---

## 📌 置頂：狀態總覽

> 最後更新：2026-10-06 13:30 ・ 狀態：**🚧 開發完成，待使用者以 Entra 實際登入驗證**

**目標**：以企業帳號登入（OIDC / Entra ID，經由 BFF，ADR-0002），完成 Admin / User 權限與帳號停用流程。

**Done Definition**：使用者以企業帳號登入、瀏覽器不持有任何 token；Admin 可以停用帳號，被停用的帳號立即無法使用 API 與建立 execution（SA 驗收條件 #1）。

| 工作項目 | 狀態 | 備註 |
|---|---|---|
| OIDC 登入（Authorization Code + PKCE，後端換 token、只發 cookie） | ✅ | Entra ID（已確認、已註冊）；見 [#015](#015--企業帳號entra-id與本機帳號密碼登入使用者管理) |
| `IIdentityProvider`：IdP claims → `ExternalIdentity`（issuer + subject / oid） | ✅ | `EntraIdentityProvider`：`iss` + `oid`，角色取自 app role `Ymir.Admin` |
| 每個請求驗證使用者狀態（停用後既有 cookie 立即失效） | ✅ | Cookie `OnValidatePrincipal` |
| Admin API：使用者列表、停用 / 啟用、本機帳號建立 / 重設密碼（寫 audit） | ✅ | 企業帳號角色以 Entra 為準，不在 Ymir 修改 |
| Angular：企業帳號登入按鈕、本機帳號登入、強制改密碼、Admin 使用者管理頁 | ✅ | |
| 授權矩陣加入角色維度（User 不能呼叫 Admin API） | ✅ | `AdminOnlyRequests` |
| Docker 作為開發 / 驗證用 runtime（Windows Docker Desktop） | ✅ | ADR-0005；見 [#003](#003--新增-docker-runtime可用-windows-docker-desktop-開發與驗證) |
| 使用者在 Windows 上依指南實機驗證 | ⏳ | [docs/guides/windows-docker.md](../guides/windows-docker.md) 第 6 節驗證清單 |
| Cloudflare Tunnel 對外入口（`Ymir.Edge` 模組） | ✅ | ADR-0006；見 [#004](#004--新增-cloudflare-tunnel-對外入口模組ymiredge) |
| 使用者建立 Named tunnel 並依指南驗證 | 🚧 | 已建立 token 模式 tunnel（token 存於本機 `deploy/cloudflared/.env`，未提交）；待在使用者主機 `docker compose up` 並跑 [指南](../guides/cloudflare-tunnel.md) 第 6 節 |
| 一個使用者一個 container + 專案（檔案群組）（ADR-0007） | ✅ | 見 [#006](#006--架構改為一人一-container專案chatgpt-式介面) |
| ChatGPT 式介面：登入即主畫面、側邊欄專案 / 聊天、直接開聊 | ✅ | 見 [#006](#006--架構改為一人一-container專案chatgpt-式介面) |
| LiteLLM sample（MiniMax 國際站） | ✅ | 設定與 proxy 已用 Fake LLM 驗證；MiniMax 實連依使用者決定在沙箱**跳過**，待使用者環境確認（[#010](#010--沙箱無法使用的外部資源驗證先跳過)） |
| Ymir 接上 LiteLLM：每位使用者的 virtual key（ADR-0004） | ✅ | 以 Fake LLM 模擬的 LiteLLM 驗證；真正的 LiteLLM + PostgreSQL 在沙箱**跳過**（image 拉不下來），見 [#011](#011--ymir-接上-litellm每位使用者的-virtual-key) |
| 對話選模型、個人 global / 專案 system prompt | ✅ | 見 [#012](#012--對話選模型個人-global-與專案-system-prompt) |
| API 放進容器 + 主機 runtime host（ADR-0008） | ✅ | 見 [#013](#013--api-放進容器agent-runtime-改由主機上的-runtime-host-管理)；沙箱以 Docker 驗證完整流程 |
| API 容器的 engine：Linux 用 rootful Podman（Quadlet）、Windows 用 Docker Desktop | ✅ | 使用者決定；見 [#014](#014--api-容器linux-用-rootful-podmanwindows-用-docker-desktop) |
| 在 Linux 主機安裝 runtime host（rootless Podman、systemd、`ymir-runtime` group）並以 Quadlet 啟動 API 容器 | ⏳ | 依 [deploy/runtime-host](../../deploy/runtime-host/README.md)、[deploy/api](../../deploy/api/README.md)；**待使用者環境確認** |
| Windows：runtime host + Docker Desktop 跑 API 容器 | ⏳ | 依 [deploy/api 的 Windows 一節](../../deploy/api/README.md#windowsdocker-desktop開發--驗證)；**待使用者環境確認** |
| 本機帳號密碼登入（使用者追加需求） | ✅ | Admin 建立、第一次登入強制改密碼、鎖定、rate limit |
| 使用者以 Entra 實際登入驗證 | ⏳ | 依 [entra-id.md 第 5 節](../guides/entra-id.md#5-驗證清單)；client secret 由使用者自行填入 `.env`；**待使用者環境確認** |
| 正式主機用完整 Containerfile 重跑 **Rootless Podman** 驗證 | ⏳ | 目前沒有 Linux 主機；可先在 WSL 2 Ubuntu 裝 Podman 驗證（見指南「效能建議」） |

**IdP 決定**：Entra ID（tenant `e333846a-0ee2-4e2d-a13b-efc97851b892`、client `5e85a804-c308-4402-b621-0be6d023a2ac`、網域 `ymir.thetainformation.com`），見 [ADR-0009](../adr/0009-entra-id-and-local-accounts.md)。

---

## 💬 留言區

### #015 · 企業帳號（Entra ID）與本機帳號密碼登入、使用者管理

> 👤 **Claude（AI）** · 🕒 2026-10-06 13:30 · `✅完成`

Sprint 2 的主要內容完成，架構決策見 [ADR-0009](../adr/0009-entra-id-and-local-accounts.md)，設定與驗證步驟見 [docs/guides/entra-id.md](../guides/entra-id.md)。

- **判斷 IdP**：和使用者一起用 `getuserrealm`（`NameSpaceType: Managed`）與 `dsregcmd /status` 確認是 **Entra ID**；地端 AD `webpro.com` 同步到雲端。使用者已完成應用程式註冊。
- **企業帳號登入**：
  - Authorization Code + PKCE，後端換 token、不保存 IdP token，瀏覽器只拿到 Ymir 的 HttpOnly cookie。
  - 使用者以 `iss` + `oid` 識別，Admin 由 Entra app role `Ymir.Admin` 決定、每次登入同步。
  - 登入後的導回位址只接受站內路徑。
- **本機帳號密碼**（使用者追加需求）：
  - Admin 在「使用者管理」建立，對方第一次登入必須先改密碼；改密碼前其他 API 一律 403。
  - PBKDF2 雜湊；連錯 5 次鎖 15 分鐘；每個 IP 每分鐘 10 次。
  - 帳號不存在與密碼錯誤的回應相同。
  - 第一個 Admin 可以用 `create-local-admin` 指令建立，密碼從 stdin 讀取。
- **停用立即生效**：每個請求都檢查帳號狀態，停用後對方下一個請求就是 401。同時取消執行中的工作、撤銷 LiteLLM virtual key、停止 runtime。
- **Admin API 與前端**：
  - 使用者列表與搜尋、停用 / 啟用（不能停用自己）、建立本機帳號、重設密碼；
  - 前端有登入頁（公司帳號按鈕 + 帳號密碼）、強制改密碼頁、使用者管理頁，設定頁可以改密碼。
- **Fake OIDC**（`tests/Ymir.Testing.FakeOidc`）：模擬 Entra v2 的 claims、RS256、PKCE、client secret。CI 與沙箱可以完整測試登入流程，不需要連 Microsoft。
- **設定範本**：`deploy/api/.env.example` 已填入 tenant / client id 與網域 `ymir.thetainformation.com`。client secret 留空，由使用者自己填。

截圖（Fake OIDC 模擬 Entra）：[登入頁](screenshots/auth/01-login.png) · [模擬 Entra 登入](screenshots/auth/02-fake-entra.png) · [使用者管理](screenshots/auth/03-admin-users.png) · [強制改密碼](screenshots/auth/04-change-password.png) · [停用帳號](screenshots/auth/05-disabled.png)

**驗證（實際跑過）**
- **後端**：`dotnet format` 通過；`dotnet test --solution Ymir.slnx` **301 項全部通過**（新增 48 項）。
  - 整合測試：
    - OIDC 完整流程、同一 `oid` 是同一個使用者、Entra 拿掉角色後降級（原本的 Admin cookie 也立即失去權限）；
    - 偽造的 state 被拒、不會導到外部網站、停用的帳號不能登入且既有 cookie 立即失效；
    - 本機帳號的強制改密碼、鎖定、rate limit、antiforgery、密碼只存雜湊、不能停用自己；
    - 授權矩陣的 Admin 維度。
  - 單元測試：Entra claim 對應、導回位址、帳號正規化、鎖定、啟動檢查。
- **前端**：`npm run lint`、`npm test`（31 項）、`npm run build` 都通過。
- **端對端**：API 直接提供 Angular build，加上 Fake OIDC。
  - `npm run e2e:auth` **6 個步驟全部通過**：公司帳號（Admin）登入 → 建立本機帳號 → 對方強制改密碼 → 停用後對方下一個操作就被登出 → 停用不能登入、重新啟用後可以。
  - `npm run e2e`（原本的 12 步驟）也全部通過。
- **未驗證、待使用者環境確認**：用你們真正的 Entra 登入（沙箱連不到 Microsoft）。請依 [entra-id.md](../guides/entra-id.md) 在 `deploy/api/.env` 填入 client secret，並在 Entra 的重新導向 URI 加入 `https://ymir.thetainformation.com/signin-oidc` 與 `http://localhost:5080/signin-oidc`，再跑第 5 節的驗證清單。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #014 · API 容器：Linux 用 rootful Podman，Windows 用 Docker Desktop

> 👤 **Claude（AI）** · 🕒 2026-10-06 10:50 · `✅完成`

依使用者決定（「先用 rootful podman，windows 上可以用 docker」）調整部署方式，ADR-0008 已記錄這個決定。

- **Linux 正式主機**：新增 Quadlet unit [`deploy/api/ymir-api.container`](../../deploy/api/ymir-api.container)。
  - 由 rootful Podman + systemd 管理 API 容器，設定為 host network、唯讀、drop ALL、`no-new-privileges`，只掛 socket 目錄與金鑰目錄。
  - rootful 時 `--group-add <ymir-runtime gid>` 直接有效，不會被 rootless 的 user namespace 對應掉。
  - Agent container 仍由 `ymir` 帳號的 rootless Podman（runtime host）執行，兩者分開。
- **Windows 開發機**：新增 [`compose.windows.yml`](../../deploy/api/compose.windows.yml) 與 `.env.windows.example`。
  - Docker Desktop 不能把 Windows 的 Unix socket 掛進 Linux 容器，所以改成：runtime host 聽 Windows 主機的 `127.0.0.1:5090`，API 容器經 `host.docker.internal:5090` 連線，仍然需要 token。
  - 程式改動：`host.docker.internal` / `host.containers.internal` **只允許**用在 API（client）端的設定，使用時會記錄「只限開發」的警告；runtime host 監聽的位址仍然只能是 loopback。
- `compose.yml` 改為備用（Linux + Docker）；`.gitignore` 加入 `deploy/api/.env`、`.env.windows`、`data/`。
- 文件：`deploy/api/README.md` 改寫成 Linux / Windows 兩段；runtime host README、Windows 指南（新增第 7 節）、CLAUDE.md 同步更新。

**驗證（實際跑過）**
- `dotnet format` 通過；`dotnet test --solution Ymir.slnx` **253 項全部通過**（新增 5 項：主機別名只允許 client 端、必須完全符合、只能用 http）。
- **Quadlet**：用 `quadlet -dryrun` 驗證 unit 檔，產生的 `podman run` 參數正確（`--network=host --read-only --cap-drop=all --security-opt=no-new-privileges --group-add=<gid>`，兩個 `-v`）。
- **沙箱以 rootful Podman 實際跑 Quadlet 產生的 `podman run` 參數**（沙箱沒有 systemd，所以只去掉 `--sdnotify` / `--cgroups=split` / `--cidfile`）：
  - 容器內 `id` 包含 socket 的 group；
  - `npm run e2e` **12 個步驟全部通過**，Agent container 由 runtime host 建立；
  - 重新啟動容器後沒有產生新的金鑰，原本的登入 cookie 仍然有效；
  - runtime host 的 log 中沒有 token 或模型金鑰。
- **未驗證、待使用者環境確認**：
  - 真正的 systemd 啟動 Quadlet；
  - SELinux 主機上容器連 runtime host socket 的 policy；
  - Windows Docker Desktop 經 `host.docker.internal` 連到 `127.0.0.1:5090`（沙箱沒有 Docker Desktop）。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #013 · API 放進容器，Agent runtime 改由主機上的 runtime host 管理

> 👤 **Claude（AI）** · 🕒 2026-10-06 09:58 · `✅完成`

依使用者選擇的 **B 方案** 完成，架構決策見 [ADR-0008](../adr/0008-containerized-api-runtime-host.md)。

- **為什麼不直接把 Podman socket 掛進 API 容器**：API 被攻破時，攻擊者就能建立任意 container、掛載主機路徑。
- **改成主機上的小服務 `Ymir.RuntimeHost`**，以 rootless Podman 專用帳號 `ymir` 執行：
  - 聽 Unix socket `/run/ymir-runtime/runtime.sock`（660，group `ymir-runtime`），每個請求都要 bearer token。
  - **只接受 user id**：確保 runtime、在 runtime 內執行程序。
  - image、掛載、資源限制、host 路徑全部由 runtime host 自己的設定決定，沒有任何端點能指定。
  - 程序規格在 client 與 server 都會檢查：以 `-` 開頭的執行檔、環境變數名稱、NUL、工作目錄允許清單。
  - stdin / stdout 經 WebSocket 轉送；API 斷線時程序一定會被結束。
- **API 端**新增 `VibeMaker:Runtime:Provider=Remote`。`PiAgentHarness`、`ExecutionRunner` 都不用改。
- **API image**（`src/Ymir.Api/Containerfile`）：
  - 內容：Angular build + API，非 root（uid 1654），預設 Production + Remote。
  - `deploy/api/compose.yml`：host network（仍只綁 `127.0.0.1:5080`，ADR-0006 不變）、唯讀、drop 全部 capabilities。
  - **只掛兩個主機資料夾**：
    - socket 目錄；
    - Data Protection 金鑰：新設定 `Ymir:DataProtection:KeysPath`，容器重建後登入仍有效。
- **部署檔**：`deploy/runtime-host/`（systemd unit、設定範本、安裝步驟）、`deploy/api/`（compose、`.env.example`、README）。
- **開發方式不變**：Development 的 API 仍在主機上跑 Local runtime；Windows 仍用 Docker provider。

截圖（API 在容器內時的完整流程）：[未分組對話](screenshots/api-in-container/02-chat.png) · [專案內對話](screenshots/api-in-container/03-project-chat.png)

**驗證（實際跑過）**
- 後端：
  - `dotnet format` 通過；`dotnet test --solution Ymir.slnx` **248 項全部通過**（新增 57 項）。
  - 單元測試：位址只接受 Unix socket 或 loopback、token 強度與比對、程序規格驗證、socket 權限不允許 other。
  - 整合測試：用真正的 runtime host（Kestrel + Unix socket）驗證以下項目：
    - stdio（約 100 KB 中文，跨多個 frame）、exit code、stderr、環境變數、工作目錄、kill；
    - API 異常斷線後主機上的程序被結束；
    - 錯誤或沒有 token 時回 401；
    - 不合法的規格在 server 端也會被拒絕；
    - 真實 Pi 經 runtime host 建檔與取消；
    - 正式 API 流程在 `Provider=Remote` 下完成對話，檔案只出現在 runtime host 的目錄。
- 沙箱實測，接近正式部署：
  1. `podman build` 建出 API image（321 MB）。
  2. runtime host 以 `Provider=Docker` 執行，socket 為 `srw-rw---- root:ymir-runtime`。
  3. API 容器：唯讀、drop ALL、`--group-add ymir-runtime`，只掛 socket 目錄與金鑰目錄；容器內沒有任何 container CLI 或 runtime socket。
  4. `npm run e2e` 對 `http://127.0.0.1:5080`（容器內的 API 直接提供 Angular）**12 個步驟全部通過**。Agent container `ymir-user-*` 由 runtime host 建立，檔案寫進主機的 workspace 目錄。
  5. runtime host 的 log 中沒有 token 或模型金鑰。
  6. 反向測試：沒有 `ymir-runtime` group 的容器連 socket 目錄得到 `Permission denied`。
- **未驗證、待使用者環境確認**：
  - Linux 主機上的 rootless Podman + systemd 安裝流程（沙箱是 rootful 環境，Agent container 改用 Docker）。
  - API 容器以 rootless Podman 執行時 `--group-add keep-groups` 的行為。
  - 沙箱建置 image 時為了通過代理，額外帶了 CA 憑證，這只用於驗證；`Containerfile` 本身沒有改。

**待決定**：~~正式主機要用哪個 engine 跑 API 容器~~（已決定，見 #014）；runtime host 用 framework-dependent（主機裝 .NET 10 runtime）還是 self-contained 發行。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #012 · 對話選模型、個人 global 與專案 system prompt

> 👤 **Claude（AI）** · 🕒 2026-10-05 18:12 · `✅完成`

依使用者要求完成三項功能：

1. **對話可選模型**：
   - 輸入框上方有模型下拉選單（首頁、專案頁、對話頁）。
   - 模型清單由 `VibeMaker:Models` 設定，`GET /api/models` 提供給前端。
   - 選擇以對話為單位記住；預設依序取：對話上次用的模型 → 個人上次選的 → 系統預設。
   - Pi 以 `--model` 帶入；選了清單外的模型回 `400 MODEL_NOT_AVAILABLE`。
   - LiteLLM virtual key 預設只允許清單內的模型。
2. **個人 global system prompt**：側邊欄底部「設定」進入 `/settings` 編輯，API 為 `GET/PUT /api/me/settings`，資料存 `vibemaker.user_settings`。
3. **專案 system prompt**：專案頁的「專案設定」可以改名稱與 prompt（`PATCH /api/projects/{id}`，會驗證擁有者）。
- **prompt 怎麼送進 Agent**：
  - 依序附加「個人 → 專案」，**保留 Pi 預設的 coding prompt**，以免影響工具使用。
  - 內容經 stdin 寫成 runtime 內的檔案，再用 `--append-system-prompt <檔案>` 帶入，所以不會出現在 host 的程序參數；執行結束後刪除檔案。
  - 開工前已實測：Pi 1.0.0 會讀取檔案內容，模型收到的是內容而不是路徑。
- 每段 prompt 上限 10,000 字；migration `ModelsAndSystemPrompts`。

截圖：[個人設定](screenshots/models-prompts/07-settings.png) · [專案設定](screenshots/models-prompts/08-project-settings.png) · [專案內對話與模型選單](screenshots/models-prompts/03-project-chat.png) · [首頁](screenshots/models-prompts/01-home.png)

**驗證（實際跑過）**
- 後端：
  - `dotnet format` 通過；`dotnet test --solution Ymir.slnx` **191 項全部通過**（新增 16 項）。
  - 整合測試（真實 Pi + Fake LLM）確認：選的模型確實送到模型端；個人與專案 prompt 依序出現在 system message；prompt 檔案執行後已刪除。
  - 授權矩陣：別人改不了你的專案。
- 前端：`npm run lint`、`npm test`（24 項）、`npm run build` 都通過。
- 端對端：`npm run e2e` 12 個步驟全部通過，包含：
  - 設定個人 prompt，重新整理後仍在；
  - 選模型，重新整理後對話仍記得；
  - 設定專案 prompt。
- MiniMax 實連依規則跳過，請在你的環境確認。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #011 · Ymir 接上 LiteLLM：每位使用者的 virtual key

> 👤 **Claude（AI）** · 🕒 2026-10-05 16:50 · `✅完成`

依使用者選擇（LiteLLM virtual key + PostgreSQL，[ADR-0004](../adr/0004-litellm-virtual-keys.md) 主方案），Agent 改為透過 LiteLLM 呼叫模型，而且**只拿得到自己的短效 virtual key**。

- **API 發 key**：API 以 master key 向 LiteLLM 為每位使用者發一把 key。
  - 只能用指定模型；
  - 24 小時有效，到期前 1 小時換發，並撤銷舊 key；
  - 可設預算上限；
  - metadata 記錄 user id，方便追蹤用量。
- **key 不落地**：key 只快取在 API 記憶體，不寫資料庫、不寫 log。
- **注入方式**：以 `exec --env LITELLM_API_KEY`（只傳名稱）注入 Pi。master key 與 MiniMax key 都不會進 container。
- **失敗處理**：拿不到 key 時，execution 以 `MODEL_PROVIDER_ERROR` 結束，錯誤訊息不含內部位址。
- **啟動檢查**：非 Development 環境沒設定 `VibeMaker:LiteLlm:MasterKey` 時拒絕啟動；Development 才能退回固定的開發用 key。
- **`deploy/litellm` 調整**：
  - 加入 LiteLLM 專用的 PostgreSQL；
  - image 修正為 `ghcr.io/berriai/litellm:v1.103.2`（原本的 `main-v1.103.2` 不存在，已從 registry 查到正確 tag）；
  - `.env` 新增 `LITELLM_SALT_KEY` 與 DB 密碼，仍被 gitignore 排除；
  - README 列出 `VibeMaker__LiteLlm__*` 設定。
- **Fake LLM 擴充**：設定 `FAKE_LLM_MASTER_KEY` 後，可模擬 LiteLLM 的 `/key/generate`、`/key/delete`，並拒絕 master key 與未發放的 key，可用來抓出金鑰外洩。

**驗證（實際跑過）**
- `dotnet format` 通過；`dotnet test --solution Ymir.slnx` **175 項全部通過**，新增 24 項。
  - **單元測試**涵蓋：
    - gateway 的 request 格式、master key header、錯誤與無法連線的處理；
    - 每人一把 key 的快取、換發、撤銷，以及並行時只發一把；
    - DI 檢查（非 Development 環境拒絕啟動、預設限定模型）；
    - Pi 程序只透過環境變數拿到 virtual key；
    - `ToString` 不洩漏金鑰。
  - **整合測試**（真實 Pi 搭配模擬的 LiteLLM）：
    - Agent 全程使用 virtual key，從未用 master key；
    - 同一使用者兩次執行共用一把 key，不同使用者的 key 不同；
    - LiteLLM 連不上時 execution 以 `MODEL_PROVIDER_ERROR` 結束。

**未驗證（依 [#010](#010--沙箱無法使用的外部資源驗證先跳過) 規則跳過）**
- 真正的 LiteLLM + PostgreSQL：沙箱拉不到 image（ghcr 的 blob 主機被網路 policy 擋、Docker Hub 回 429）。
- MiniMax 實連。

以上兩項請在你的環境用 `deploy/litellm` 的 `docker compose up -d` 搭配 Ymir API 設定確認。

`⚠️發現`
- 使用者被停用時撤銷 key：已有 `RevokeAsync`，等 Admin 停用流程完成後接上；runtime idle stop 時撤銷也還沒做。
- `/key/generate` 也會套用 ServiceDefaults 的 HTTP 重試；萬一重試造成多發 key，多出的 key 會在有效期後自動失效。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #010 · 沙箱無法使用的外部資源，驗證先跳過

> 👤 **Claude（AI）** · 🕒 2026-10-05 16:09 · `📢公告`

回覆 [#009](#009--litellm-sampleminimax-國際站沙箱網路尚未放行) 的卡關。使用者決定：目前在 Claude Code 雲端模式開發，**沙箱網路不允許的外部資源可以先跳過**。

- MiniMax 實連驗證**跳過**，改由使用者在自己的環境跑 `deploy/litellm/smoke-test.sh minimax` 確認。LiteLLM sample 標為完成。
- 這條規則已寫入 [CLAUDE.md](../../CLAUDE.md#每次修改後的流程必做) 的驗證步驟：
  - 改用 Fake LLM 等本機方式做替代驗證；
  - 在看板註明「未驗證、待使用者環境確認」；
  - 不要為了驗證去繞過網路限制。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #009 · LiteLLM sample（MiniMax 國際站），沙箱網路尚未放行

> 👤 **Claude（AI）** · 🕒 2026-10-05 16:04 · `🚧進度` `⛔卡關`

依使用者要求，新增 [`deploy/litellm/`](../../deploy/litellm/README.md)：
- `config.yaml` 有兩個模型：
  - `minimax`：走 MiniMax 國際站的 OpenAI 相容端點 `https://api.minimax.io/v1`，預設模型 `MiniMax-M2`，可依訂閱方案修改；
  - `fake-model`：轉給 Fake LLM，用於離線驗證。
- `compose.yml`：LiteLLM `main-v1.103.2`，只綁 `127.0.0.1:4000`；另有 `smoke-test.sh` 可送一則測試訊息。
- 金鑰只放在本機的 `deploy/litellm/.env`（權限 600）。
  - `.gitignore` 加了明確規則；commit 前檢查過，staged diff 不含 token。
  - CLAUDE.md 安全紅線補上模型金鑰的規則。

**驗證（實際跑過）**
- 在本機以 `pip install litellm[proxy]`（1.103.2）啟動 proxy，搭配 Fake LLM：
  - `fake-model` 正常回覆；
  - 沒帶 master key、或帶錯 master key 都被拒絕。
- 直接 `curl https://api.minimax.io/v1/...`：**沙箱的網路 policy 擋下（CONNECT 403）**。經 LiteLLM 走 `minimax` 也是 Connection error。**MiniMax 實際能否連通尚未驗證**。

`⛔卡關`
- 需要使用者在雲端環境設定把 `api.minimax.io` 加入允許的網域（Network access → Custom → Allowed domains）。加入後，我在新 session 跑 `deploy/litellm/smoke-test.sh minimax` 即可驗證。
- 或者使用者在自己電腦上照 README `docker compose up -d` 後跑 smoke test。

`⚠️發現`
- 本機 pip 版 LiteLLM 處理「沒帶金鑰」時，因缺少一個選用模組，回 500 而不是 401；請求仍然被拒絕。官方 image 含完整套件，未驗證是否有同樣情況。
- `.env` 只存在這個 session 的沙箱裡，不會同步到使用者電腦；使用者要自己依 `.env.example` 建立。
- token 已出現在對話紀錄中；擔心外洩可以到 MiniMax 後台重新產生。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #008 · 流程變更：每次做完都直接合併回 main

> 👤 **Claude（AI）** · 🕒 2026-10-05 15:51 · `📢公告`

依使用者要求，「每次修改後的流程」新增第 5 步，寫在 [CLAUDE.md](../../CLAUDE.md#每次修改後的流程必做)：

1. push 後開 PR → `main`。
2. 等 CI 全部通過，再以 merge commit 合併，並指定 head SHA。CI 失敗就修正後重跑，直到通過，不得跳過或停用測試。
3. 合併後把工作分支重設到最新的 `main`，再開始下一件工作。

這次就照新流程，把 #006（一人一 container、專案、ChatGPT 式介面）與 #007（品牌）一起合併回 main。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #007 · 左上角品牌改為「Web-Pro Ymir」

> 👤 **Claude（AI）** · 🕒 2026-10-05 15:36 · `✅完成`

依使用者要求，左上角原本顯示「Ymir · Vibe Maker」，現在改為 **Web-Pro Ymir**（Web-Pro 用強調色）。側邊欄與手機版頂列兩處都已更新。瀏覽器分頁標題與登入頁標題這次**沒有改**，仍是 Vibe Maker。

**驗證（實際跑過）**
- 前端：`npm run lint`、`npm test`（20 項）、`npm run build` 都通過。
- 啟動 API（Development）與 ng serve，用 Playwright 登入後讀取畫面，側邊欄與手機頂列都顯示 `Web-Pro Ymir`。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #006 · 架構改為一人一 container、專案、ChatGPT 式介面

> 👤 **Claude（AI）** · 🕒 2026-10-05 14:22 · `📢公告` `✅完成`

依使用者要求修改架構與 UI/UX flow，決策見 [ADR-0007](../adr/0007-one-runtime-per-user.md)。

**架構（取代 SA §6.1「一個 Workspace 一個 container」）**
- **一個使用者一個 container**：
  - container 名稱為 `ymir-user-{userId}`；
  - host 目錄為 `{WorkspaceRoot}/users/{userId}/workspace` 與 `agent-state`；
  - `agent_runtimes` 以 `user_id` 加唯一索引，每人一筆。
- **Workspace 改名為 Project（專案）**：專案就是 container 內的檔案群組 `/workspace/projects/{id}`，同一專案的對話共用檔案。
- **對話可以不分組**：未分組的對話在 `/workspace/chats/{conversationId}` 工作。
- **工作目錄**：
  - 每次執行以 `exec --workdir` 進入工作目錄；
  - 只接受由 Guid 產生的路徑，其他一律拒絕；
  - 目錄在 container 內用 `mkdir -p` 建立，所以擁有者是 agent 使用者。
- **依序執行**：同一使用者的 execution 依序執行，因為共用同一個 container 的資源限制。
- **API**：
  - `/api/workspaces` 改為 `/api/projects`；
  - 新增 `GET /api/runtime`；
  - `/api/conversations` 的 `projectId` 可省略。
- **Migration `OneRuntimePerUser`**：
  - 舊的 workspace 變成專案，原本的對話保留在該專案；
  - 舊的 runtime 紀錄清空；
  - 舊的 `ymir-ws-*` container 要手動移除，方法見 Windows 指南。

**UI/UX（ChatGPT 式）**
- 登入後直接進主畫面。左側欄依序是：新對話、**專案**（可新增、可展開看底下的對話）、**聊天**（未分組的對話）、使用者 / 登出。窄螢幕時側欄變成抽屜。
- **直接在輸入框開聊**：首頁或專案頁送出第一則訊息時，自動建立對話，標題取自第一則訊息。
- 輸入框：Enter 送出、Shift+Enter 換行；中文輸入法選字中按 Enter 不會送出。

截圖：[首頁](screenshots/chat-layout/01-home.png) · [未分組對話](screenshots/chat-layout/02-chat.png) · [專案內對話](screenshots/chat-layout/03-project-chat.png) · [專案頁](screenshots/chat-layout/04-project.png) · [手機](screenshots/chat-layout/05-mobile.png) · [手機抽屜](screenshots/chat-layout/06-mobile-drawer.png)

**驗證（實際跑過）**
- 後端：
  - `dotnet format` 通過；
  - `dotnet test --solution Ymir.slnx` **151 項全部通過**，包含真實 Pi、授權矩陣、OpenAPI 快照；
  - 新增的測試涵蓋：專案 / 未分組對話 API、工作目錄白名單、container 指令（Podman / Docker）、Pi 檔案落點。
- Migration：
  - 先用舊版 migration 建庫並寫入 workspace / 對話 / runtime；
  - 升級後，專案名稱與對話的 `project_id` 正確，runtime 紀錄清空，`project_id` 可為 null。
- 前端：`npm run lint`、`npm test`（20 項）、`npm run build` 都通過。
- 端對端：
  - 環境是 SQL Server + Fake LLM + API（Pi harness、**Docker runtime**）+ ng serve；
  - `npm run e2e` 跑完 9 個步驟全部通過：登入 → 直接開聊 → 重新整理後歷史仍在 → 建立專案 → 專案內對話 → 停止 → session 續接 → 專案頁 / 切換對話 → 手機抽屜。
- 執行後檢查 container 與檔案：
  - 每個帳號只有一個 `ymir-user-*` container，兩個帳號共兩個；
  - `chats/{id}/hello.txt` 與 `projects/{id}/hello.txt` 都在正確位置，擁有者是 uid 1000。

`⚠️發現`
1. 沙箱網路擋 `deb.debian.org`（http 403），**正式的 `runtime/agent/Containerfile` 在這裡無法 build**。端對端驗證改用只在沙箱用的 image：從 host 複製 Pi 1.0.0，使用者與目錄設定相同，**沒有提交**。CI 的「Agent runtime image」job 仍會 build 正式 Containerfile。
2. 這個沙箱的 Podman 是 rootful（root），`keep-id` 對應不到可寫的 host 目錄，而且 `keep-id` 加 host network 無法啟動，所以端對端改用 Docker provider 驗證。**正式的 Rootless Podman 仍需在 Linux / WSL2 主機上驗證**，這一項已列在置頂表。
3. 同一使用者的多個對話**無法同時執行**，第二個會排隊等第一個結束。要並行的話，需要先評估 container 資源與檔案鎖定。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

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
