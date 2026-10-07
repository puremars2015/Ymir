# Sprint 6 · Agent 擴充能力、RAG 知識庫、前端網站託管

[← 回看板列表](README.md) ・ 規則與格式見 [README](README.md#留言規則)

---

## 📌 置頂：狀態總覽

> 最後更新：2026-10-08 02:00 ・ 狀態：**🚧 進行中（插單：OneDrive connector）**

**目標**：
- 讓 Agent 的能力可以在管理員的管制下擴充：使用者可自建 skill / MCP，開發人員則維護平台 MCP（[ADR-0012](../adr/0012-agent-extensions-and-platform-mcp.md)）；
- 把 [RAG 知識庫](../planning/rag-knowledge-base-plan.md) 與 [前端網站託管](../planning/frontend-site-hosting-plan.md) 從計畫推進到 ADR，再進入第一階段實作。

**Done Definition**：
1. 管理員可設定全域與每人的擴充能力（`skills`、`mcp`），伺服器在啟動 Agent 時強制執行，並寫稽核。
2. Agent 只能經獨立的 MCP Gateway、以每人專屬的短期 token 呼叫平台服務；以 echo 服務驗證授權、稽核與 rate limit。
3. RAG 與網站託管的 ADR 經使用者確認；確認後完成各自的第一個實作階段（RAG 最小索引、公開網站發布）。

**使用者已決定（2026-10-07）**：
- 範圍：ADR-0012、RAG、網站託管。剩餘強化項目（rate limit、CI 掃描、壓測）不在本 Sprint。
- ADR-0012 的四項決定：

  | # | 項目 | 決定 |
  |---|---|---|
  | 1 | 權限粒度 | 全域預設 + 每人覆寫 |
  | 2 | 第一階段能力 | `skills` 與 `mcp` 都做 |
  | 3 | Gateway | 獨立服務 |
  | 4 | Egress | 管理員控制（`internet` 能力，全域預設 + 每人覆寫），**預設允許**（A0 後決定） |

| # | 工作項目 | 狀態 | 前置 / 待決定 |
|---|---|---|---|
| W2 | Ubuntu 既有 Docker 部署啟動檔 `start-ymir.sh` | ✅ | 見 [#005](#005--ubuntu-既有-docker-部署啟動檔)；語法與隔離模擬通過，實機待驗證 |
| W1 | Windows 既有部署一鍵啟動檔 `start-ymir.ps1` | ✅ | 見 [#004](#004--windows-既有部署一鍵啟動檔)；已在目前主機驗證 |
| A0 | ADR-0012 spike：實測 Pi 1.0.0 在 RPC 模式的 `--no-skills` / `--skill`、能否不讀使用者層 `mcp.json`、平台 MCP 設定能否放在 Agent 不可寫的位置，以及 rootless Podman 能否限制 egress；結果寫回 ADR-0012 | ✅ | — |
| A1a | 擴充政策（ADR-0012 第一階段）：<br>• `vibemaker.extension_policy`（`skills`、`mcp`）+ 每人覆寫資料表 `vibemaker.user_extension_grants`<br>• `IExtensionPolicy`<br>• `PiAgentHarness` 依政策組合參數<br>• `ymir-extension-builder` skill<br>• 管理介面、`GET /api/extensions`、稽核、授權矩陣<br>• 一律 `-ne`、每次執行重寫 `settings.json` / `trust.json` / `mcp.json`（A0 結果） | ✅ | — |
| A1b | 對外連線（ADR-0012 A.8）：<br>• `RuntimeNetworkAccess`、`VibeMaker:Runtime:RestrictedNetwork`<br>• container label 比對與重建、`runtime.recreate` 稽核<br>• runtime host 協定（只接受 enum）<br>• `internet` 能力加入擴充政策（全域 + 每人）<br>• 受限網路部署文件 | ✅ | — |
| A2 | MCP Gateway（ADR-0012 第二階段）：<br>• 獨立專案 `Ymir.McpGateway`<br>• 每人短期 token、`deploy/mcp/servers.json` 服務目錄、存取清單<br>• echo 服務、稽核與 rate limit、部署文件 | ⏳ | — |
| O0 | **插單** OneDrive connector ADR（[ADR-0013](../adr/0013-onedrive-connector.md)） | ✅ | — |
| O1 | OneDrive 連結 / 解除連結：<br>• `onedrive` 能力<br>• 授權碼 + PKCE 連結流程、refresh token 加密保存<br>• 根資料夾、設定頁<br>• FakeGraph 測試替身 | 🚧 | O0 |
| O2 | OneDrive 同步：<br>• `IWorkspaceFileWriter`<br>• 執行前下載、執行後上傳（持久化工作）<br>• eTag 衝突保留兩份<br>• 雲端保存狀態與重試、使用指南 | ⏳ | O1 |
| R0 | RAG ADR（ADR-0014）：服務與 volume 邊界、Embedding 抽象、向量儲存介面、SQLite（sqlite-vec）部署、權限 | ⏳ | **❓待決定**：Embedding 模型與硬體、文件格式與容量、外部回答模型的資料政策 |
| R1 | RAG 最小索引：知識庫、文件儲存、背景索引、Fake Embedding、每專案一份 SQLite | ⏳ | R0 經使用者確認 |
| R2 | RAG 問答：檢索、回答、引用、資料不足提示、UI | ⏳ | R1 |
| H0 | 網站託管 ADR（ADR-0015）：獨立網站網域、每站來源隔離、私人網站登入、經主機複製產物、容量限制 | ⏳ | **❓待決定**：網域與 DNS / Tunnel、公司模式的帳號範圍、配額 |
| H1 | 公開網站發布：網站與版本、產物檢查、Nginx 託管、原子切換、取消發布 | ⏳ | H0 經使用者確認 |
| H2 | 身分與分享：公司模式、指定使用者、分享給我的網站 | ⏳ | H1 |

**從 Sprint 2 移交、待使用者環境確認**（見 [Sprint 2 看板 #037](board-sprint-2.md)）：
- 真 Entra 登入；
- 真 LiteLLM + MiniMax 與單價；
- Linux rootless Podman + runtime host + Quadlet；
- Cloudflare Tunnel；
- Windows Docker Desktop；
- OTLP 指標匯出；
- 驗收計畫的手動項目（Codex 執行，見 [sprint5-acceptance-plan.md](../planning/sprint5-acceptance-plan.md)）。

**沙箱限制**（無法在沙箱驗證，會標「未驗證、待使用者環境確認」）：
- 真實 Embedding 模型；
- 網域、DNS、Tunnel；
- EIP / MES 等內網服務；
- 正式主機的 rootless Podman egress。

---

## 💬 留言區

### #005 · Ubuntu 既有 Docker 部署啟動檔

> 👤 **Codex（AI）** · 🕒 2026-10-07 13:26 · `✅完成`

- **需求**：使用者要求另提供 Ubuntu 啟動檔。已詢問 Docker／Podman 選擇，尚未收到回覆，先依目前 Docker 使用方式提供 Docker 版，不更改既有 Podman Quadlet 文件與部署。
- **完成**：新增 [start-ymir.sh](../../start-ymir.sh)，支援既有 Linux Docker API＋主機 runtime host 部署；檢查 systemd／Docker、辨識既有 SQL／LiteLLM PostgreSQL／LiteLLM／API／Tunnel 容器，依序啟動並等待 runtime host、API readiness 及登入頁。已運行的服務保留，名稱歧義要求明確指定；腳本不拉碼、建置、建立容器或啟動 Agent。
- **說明**：[API README](../../deploy/api/README.md) 記錄 `sudo bash ./start-ymir.sh`、首次部署前置條件及容器名稱／埠／逾時覆寫。`.gitattributes` 固定 LF，Git 保留 executable bit；Linux 的 API 容器含前端，與 Windows 主機 API 啟動檔分開。
- **驗證**：`git diff --check` 與 LF 檢查通過；Ubuntu 24.04 容器中的 `bash -n` 通過。離線且不掛 Docker socket的模擬 Docker／systemd／curl 測試七項通過：已運行服務不重啟、停止服務的啟動順序、缺少容器、Compose 名稱歧義、健康檢查逾時、非法埠及 help。
- **限制**：沒有可供操作的 Ubuntu 部署主機，**實際 systemd、runtime host、Docker 服務與 Tunnel 尚未驗證**；需要先完成各部署文件的 image／憑證／socket／Linux Tunnel network 設定。沒有變更 Windows 的服務。

---

### #004 · Windows 既有部署一鍵啟動檔

> 👤 **Codex（AI）** · 🕒 2026-10-07 13:15 · `✅完成`

- **需求**：使用者要一個 `.ps1`，日後執行即可啟動目前的 Ymir。
- **完成**：新增根目錄 [start-ymir.ps1](../../start-ymir.ps1)，於 [API 部署說明](../../deploy/api/README.md) 加入操作方式。檢查 Docker、必要時啟動 Docker Desktop、驗證既有容器、依序啟動資料庫／LiteLLM／網頁／Tunnel，API 未執行時才以隱藏背景程序啟動；沿用 `%LOCALAPPDATA%\Ymir\deploy\run-api.ps1`，不保存憑證、不拉碼或重建，也不啟動 Agent 容器。
- **驗證**：PowerShell parser 與 `git diff --check` 通過；UTF-8 BOM 保留 Windows PowerShell 5.1 中文相容。實際重複執行保留原 API PID；確認無 QUEUED／RUNNING execution 後，只停止 API，再由 Windows PowerShell 5.1 執行腳本，背景 API 啟動且 `/health` 與本機登入頁通過，執行腳本的程序已結束而 API 仍運行。缺少部署目錄時回非零結束碼。
- **限制**：沒有停止 Docker Desktop 或其他資料庫來測試全部服務從關閉狀態啟動，避免影響其他正在運作的服務；此分支尚未實測。公開網址仍依原有 Tunnel／DNS／網路；API 容器＋runtime host 拓樸不使用本腳本。

---

### #001 · Sprint 6 看板開張

> 👤 **Claude（AI）** · 🕒 2026-10-07 21:30 · `📢公告`

- **背景**：使用者更新了規劃文件與 ADR（ADR-0012 草案、Codex 的 RAG 與網站託管計畫），要求建立新的 Sprint。
  - Sprint 2 看板實際上涵蓋了路線圖的 Sprint 2～5，已經結束（見 [Sprint 2 #037](board-sprint-2.md)）。
  - 新看板接著路線圖編號為 Sprint 6。
- **範圍與決定**：見置頂區。ADR-0012 已依使用者決定改為「已採納（egress 待 spike 結果）」。
- **順序**：
  1. A0 → A1 → A2 依序實作；
  2. R0 與 H0 只寫 ADR，可以在 A 系列進行時先寫好，交給使用者決定；
  3. R1 / R2、H1 / H2 等使用者回覆後才開始。
- **流程**：每一項都依 CLAUDE.md 流程獨立走完：驗證 → commit → 看板留言 → PR → CI → merge。
- **下一步**：A0 spike，用沙箱的 Fake LLM + 真實 Pi 1.0.0 實測 skill / MCP 的載入與關閉，結果寫回 ADR-0012。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #002 · A0 spike 完成：Pi 的 skill / MCP / extension 載入行為與 egress

> 👤 **Claude（AI）** · 🕒 2026-10-07 23:00 · `🔬實測` `📢公告`

- **做了什麼**：用沙箱的真實 Pi 1.0.0（RPC 模式，參數與環境變數同 `PiAgentHarness`）搭配記錄請求內容的假模型實測。依模型收到的 system prompt 與 `tools` 判斷，結果寫進 [ADR-0012「Spike 結果」](../adr/0012-agent-extensions-and-platform-mcp.md)。
- **結論**：
  - `-ns` 關掉使用者層與專案層 skill；`--skill <路徑>` 不受影響，可用來載入平台 skill。
  - `-ne` 關掉 MCP 與所有 extension；`-ne -e builtin:mcp` 只恢復 MCP。
  - MCP 設定只讀 `agentDir/mcp.json` 與受信任專案的 `.pi/mcp.json`，**沒有唯讀層**；只在 session 啟動時讀取。
    - 因此改為 Ymir 每次執行前重新產生 `mcp.json`（平台項目優先），強制點在 gateway 的 token 與存取清單。
- **發現的風險（現況）**：
  - 目前的參數下，Agent 寫進 `/agent-state/pi-agent/` 的 skill、`mcp.json`、**extension（TypeScript 程式碼）**都會在下一次執行被載入。
  - Agent 可以改寫 `trust.json` 或 `settings.json` 讓 workspace 的 `.pi/` 生效。
  - 影響範圍仍限於該使用者自己的 container，沒有跨使用者或拿到平台憑證的問題。A1 會一律加 `-ne`，並在每次執行前重寫這些檔。
- **Egress**（沙箱是 root Podman）：
  - `--network none` 全部不通；
  - 現行的 `slirp4netns` 可連 host 的對外 IP 與網際網路；
  - `--internal` network 只通同網路的 container 與閘道 IP。
  - `VibeMaker__Runtime__Network` 已可設定，不必改 `ContainerCommandBuilder`。
- **驗證**：只改文件；`git diff --check`；spike 腳本留在 scratchpad，不進版控。
- **未驗證、待使用者環境確認**：rootless Podman 下 `--internal` network 與 LiteLLM / gateway container 共用網路的行為。
- **❓待確認**：
  - Egress 建議：第一階段不強制；
  - 正式主機提供 `--internal` network 範本，由管理員選擇啟用；
  - 這會讓使用者自建、需要連外的 MCP 失效。
  - 請使用者確認是否同意。A2 開工前需要答案，A1 不受影響。
- **下一步**：A1 擴充政策實作。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #003 · Egress 決定：管理員控制對外連線，預設允許

> 👤 **Claude（AI）** · 🕒 2026-10-07 23:30 · `📢公告` `📐設計`

- **使用者決定**：
  - Agent container 能否對外連線改由管理員控制；
  - 預設允許；
  - 不論允許與否，Agent 都要連得到 LiteLLM 與 MCP Gateway。
- **設計**（寫進 [ADR-0012 A.8](../adr/0012-agent-extensions-and-platform-mcp.md)）：
  - 新能力 `internet`，與 `skills` / `mcp` 同一套「全域預設 + 每人覆寫」。
  - 關閉時 container 改用主機預先建立的 `--internal` network（`VibeMaker:Runtime:RestrictedNetwork`），LiteLLM 與 gateway 接在同一個 network。
  - 沒有設定受限網路時，執行以摘要錯誤失敗，不會退回成可以對外連線。
  - Network 只能在建立 container 時決定：以 label 比對政策，不符時在使用者沒有其他執行中的 execution 時重建（寫稽核 `runtime.recreate`），否則等下一次執行再套用。
  - Runtime host 只多接受 enum，network 名稱來自 runtime host 自己的設定，符合 ADR-0008 紅線。
- **工作項目**：A1 拆成 A1a（擴充政策與 Pi 參數）與 A1b（對外連線）；A2 的 egress 待確認項目移除。
- **驗證**：只改文件；`git diff --check`。
- **未驗證、待使用者環境確認**：rootless Podman 下的受限網路行為。
- **下一步**：A1a 實作。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #004 · A1a 完成：Agent 擴充政策（skills / mcp）

> 👤 **Claude（AI）** · 🕒 2026-10-08 00:30 · `✅完成`

- **做了什麼**（ADR-0012 A.1～A.7）：
  - **政策**：全域預設存 `vibemaker.extension_policy`（system_settings，預設全部關閉），每人覆寫存新表 `vibemaker.user_extension_grants`（migration `UserExtensionGrants`）。`ExtensionPolicyService` 解析有效值，`ExecutionRunner` 每次執行帶入 `AgentRunRequest.Extensions`；查詢失敗時執行失敗，不會放寬權限。
  - **Pi 參數**（`PiExtensionConfig`）：
    - 一律 `--no-extensions`：Agent 寫的 extension 不再被載入，修掉 A0 發現的現況風險；
    - 沒有 `skills` 加 `--no-skills`；有 `mcp` 才 `--extension builtin:mcp`；
    - 任一能力開啟時以 `--skill` 載入平台 skill `ymir-extension-builder`，內容依開放的能力產生。
  - **每次執行前重寫**：agent dir 的 `settings.json`（`defaultProjectTrust: never`）、`trust.json`、`mcp.json`，以及平台 skill。
    - 使用者自建 MCP 改放 `mcp.user.json`；第一次執行時，舊的 `mcp.json` 會複製成 `mcp.user.json`，不遺失。
    - 檔案路徑以參數傳入（Local runtime 會轉成 host 路徑）；內容經 stdin，因為 MCP 設定可能含憑證。
  - **API**：
    - 管理員：`GET/PUT /api/admin/settings/extensions`、`GET/PUT /api/admin/users/{id}/extensions`（`Inherit` / `Allow` / `Deny`），稽核 `admin.settings.extensions.update`、`admin.user.extensions.update`，都已加入授權矩陣；
    - 成員：`GET /api/extensions` 只回能力與名稱，不回 MCP 設定內容，沒有執行環境時不會為了查詢而建立 container。
  - **前端**：系統設定頁新增「Agent 擴充能力」卡片；使用者頁每列新增「擴充能力」，可設定個人覆寫並預覽結果；個人設定頁顯示我的擴充能力與已建立的名稱。
- **驗證**：
  - `dotnet build`（0 警告）；
  - `dotnet test --solution`：504 通過，其中新增：
    - `PiExtensionConfigTests`、`ExtensionPolicyTests`（單元）；
    - `ExtensionPolicyTests`（整合，真實 Pi 1.0.0 + Fake LLM）：預設時 Agent 放的 skill、extension、trust 設定都不生效；管理員開放後 skill 出現、extension 仍不載入；改回禁止下一次就生效；成員端清單不含 MCP 憑證。
  - OpenAPI 快照已更新、`npm run api:generate`；
  - 前端 lint / 107 個 Vitest / build 通過；
  - `e2e:admin` 全部通過，新增第 14 步，截圖 11～13 在沙箱。
- **與計畫的差異**：
  - `internet` 欄位移到 A1b，和強制機制一起上線，避免出現設定了卻不生效的選項。
  - 平台 skill 不放 image，改成每次執行前寫入 `/agent-state/ymir/skills/`，Local / Remote 都適用（ADR-0012 已更新）。
- **下一步**：A1b 對外連線（`internet` 能力、受限網路、runtime host 協定）。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #005 · A1b 完成：管理員控制 Agent 的對外連線

> 👤 **Claude（AI）** · 🕒 2026-10-08 01:30 · `✅完成` `🔬實測`

- **做了什麼**（ADR-0012 A.8）：
  - **政策**：擴充政策新增 `internet`（全域預設與每人覆寫），預設允許；A1b 之前儲存、沒有這個欄位的設定也視為允許。
  - **Runtime**：`EnsureRuntimeAsync(userId, network, ct)`。
    - 關閉時 container 改接 `VibeMaker:Runtime:RestrictedNetwork`（`--internal`），並以 label `ymir.network` 記錄模式；
    - 政策改變時，在使用者的下一次執行前重建（`ExecutionRunner` 持有使用者的執行鎖），寫稽核 `runtime.recreate`；
    - 沒有設定受限網路時，執行以摘要錯誤失敗，**不會退回成可以對外連線**；
    - 名稱不合法（`host`、`bridge` 等）時啟動就拒絕。
  - **Runtime host**：只多接受 `?network=internet|restricted`，其他值回 400，未設定時回 409，network 名稱只來自 runtime host 自己的設定。
  - **Local runtime**：無法限制，只記錄警告；管理介面顯示「開發模式不強制」。
  - **管理介面**：
    - 擴充能力卡片新增「允許對外連線」；
    - 依 runtime 的支援狀態顯示警告（未設定 / 由 runtime host 決定 / 開發模式）；
    - 使用者頁可對個人覆寫；個人設定頁顯示結果。
  - **部署文件**：`deploy/runtime-host/README.md`「受限網路」說明作法：一般與 `--internal` 兩個 network，LiteLLM 與 gateway 同時接上，Agent 以同一個名稱 `litellm` 連線。`runtime-host.env.example` 與 `deploy/litellm/README.md` 同步更新。
- **驗證**：
  - `dotnet test --solution`：543 通過，新增：
    - `ContainerCommandBuilderTests`：兩種 engine × 兩種模式、label、未設定時不退回、拒絕 host / bridge 等名稱；
    - `ContainerRuntimeManagerNetworkTests`：以模擬的 container CLI 驗證新建、政策改變時重建、舊 container 沒有 label 視為可對外、`network: null` 不重建、未設定時不動 container；
    - `RuntimeHostProtocolTests`、`RuntimeHostTests`：只接受 enum；
    - `ExtensionPolicyTests`：預設允許、舊設定相容。
  - 前端 lint / 108 個 Vitest / build 通過。
  - **沙箱真實 Podman 實測**（新的 `npm run e2e:network`）：
    - 管理員關閉 → container 重建到 `ymir-agents`；
    - Agent 連得到 `litellm`（200）、連不到外網（ENETUNREACH），且仍正常回覆並記得上一輪（Pi session 保留）；
    - 恢復後重建回 `ymir-agents-net`、外網可連；稽核 `runtime.recreate` 2 筆。
  - 沙箱是 root Podman：掛載目錄的擁有者問題改用 Docker 模式（`ContainerExecutable=podman`）跑完，與網路行為無關。
- **未驗證、待使用者環境確認**：rootless Podman（`ymir` 帳號）下兩個 network 與 LiteLLM container 的實際行為，可用 `npm run e2e:network` 驗證。
- **下一步**：A2 MCP Gateway（獨立服務 `Ymir.McpGateway`、每人短期 token、`deploy/mcp/servers.json`、存取清單、echo 服務）。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #006 · 插單：OneDrive connector（ADR-0013）

> 👤 **Claude（AI）** · 🕒 2026-10-08 02:00 · `📢公告` `📐設計`

- **背景**：使用者要求插單實作已規劃的 OneDrive 功能（[計畫](../planning/onedrive-workspace-plan.md) 的方法二）：使用者開啟後可以連到自己的 OneDrive。A2（MCP Gateway）暫停，等 OneDrive 完成後繼續。
- **使用者決定**：
  - 首版做「連結 + 自動同步」；
  - 使用公司帳號，權限為 `Files.ReadWrite`（整個 OneDrive，使用者自選根資料夾）；
  - 由管理員開放（擴充政策新增 `onedrive` 能力，預設關閉），使用者自己連結。
- **設計**（[ADR-0013](../adr/0013-onedrive-connector.md)）：
  - 沿用 Ymir 的 Entra 應用程式註冊，另走一個授權碼 + PKCE 的連結流程；
  - refresh token 以 Data Protection 加密，只在後端使用，Agent 拿不到；
  - 執行前下載、執行後由持久化工作上傳；以 eTag / If-Match 避免覆蓋，衝突時保留兩份；首版不同步刪除。
  - CLAUDE.md 的「不得保存 IdP token」紅線補上這個例外。
- **編號**：RAG、網站託管的 ADR 順延為 ADR-0014、0015。
- **驗證**：只改文件；`git diff --check`。
- **未驗證、待使用者環境確認**：真實 Entra 權限同意與 Microsoft Graph（沙箱連不到，改用 FakeGraph）。
- **下一步**：O1 連結與解除連結。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---
