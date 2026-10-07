# ADR-0012：Agent 擴充能力——管理員管制的使用者自建擴充，與開發人員維護的平台 MCP

- 狀態：已採納（2026-10-07 使用者確認決定事項；A0 spike 結果已寫入，egress 由管理員以 `internet` 能力控制、預設允許；補充 ADR-0003、ADR-0004、ADR-0007、ADR-0010）
- 日期：2026-10-07

## 背景

目前使用者無法讓 Agent 擴充自己的能力：Ymir 啟動 Pi 時沒有載入 skill、extension 或 MCP 的設定，SA §3 也把 MCP 正式實作排除在 Core MVP 之外，SA §22 只列為後續擴充。

使用者提出兩個需求：

1. **管理員管制、成員自建**：管理員可設定誰有權限；權限啟用時，成員可以自己要求 Agent 建置 skill、connector、addon。
2. **平台常駐服務只由開發人員建置**：EIP、MES、embedding 這類需要常駐 MCP 或常駐服務的能力，只有 Ymir 專案開發人員能建置，再提供給成員的 Agent 選擇與呼叫。

實測 Pi 1.0.0（`@earendil-works/pi-coding-agent` 隨附文件）後確認：

- Pi 原生支援四種擴充：**skills**（含 `SKILL.md` 的目錄）、**extensions**（可執行程式碼）、**packages**（npm / git 的 extension + skill 組合）、**MCP**（stdio 與 streamable HTTP，設定檔 `~/.pi/agent/mcp.json`，`pi mcp add` 可由 Agent 在 bash 執行）。
- Pi 以 `PI_CODING_AGENT_DIR` 當作「使用者層」目錄。Ymir 把它設在 `/agent-state/pi-agent`，這個目錄是 **Agent 可寫**的 bind mount（ADR-0007）。所以今天 Agent 其實已能把 skill 或 `mcp.json` 寫進去；只是 Ymir 沒有控制、沒有稽核，是否會載入也沒有被測過。
- 載入開關有 `-ns`（`--no-skills`）、`-ne`（`--no-extensions`）、`--skill <path>`、`-e <path>`。明確指定的路徑在關閉探索後仍會載入。
- Pi 官方文件明講：extension、套件安裝程式與子程序都以 Pi 程序的權限執行，**安全靠外部隔離，不靠 Pi 內部開關**。這正是 Ymir 的 container 邊界（ADR-0003、ADR-0007）要扛的事。

兩個需求的信任等級完全不同，必須分開設計：

| | 使用者自建（需求 1） | 平台 MCP（需求 2） |
|---|---|---|
| 程式碼來源 | 使用者的 Agent 產生（不可信） | Ymir 開發人員審查後部署 |
| 執行位置 | 使用者自己的 container 內 | 主機或內網的常駐服務，不在 Agent container |
| 能碰到的資源 | 只有該使用者 container 本來就能碰到的 | EIP / MES / embedding 等企業內部資料 |
| 憑證 | 不得有平台憑證（ADR-0004） | 由 gateway 保管，Agent 只拿到每人專屬的短期 token |
| 誰決定能不能用 | 管理員的能力政策 | 管理員的存取清單（只能開關與授權，不能新增服務） |

## 決策

### A. 使用者自建擴充（需求 1）

1. **能力分級，預設全部關閉（預設拒絕）。** 管理員可控制的能力：

   | 能力 | 內容 | 風險說明 |
   |---|---|---|
   | `skills` | `SKILL.md` 指示、附帶的腳本與參考檔 | 只影響該使用者自己的 Agent 行為；腳本仍在其 container 內執行 |
   | `mcp` | 使用者自建的 MCP server（stdio 在自己的 container 內，或 HTTP 連外部位址） | 能新增工具與連線目的地；憑證只能是使用者自己的 |
   | `internet` | Agent container 能否連到 LiteLLM 與 gateway 以外的位址（網際網路、內網） | 關閉時 Agent 無法下載套件或呼叫外部 API；**預設允許**（使用者 2026-10-07 決定），見 A.8 |
   | `extensions` | Pi extension / package（可執行程式碼，可 `npm install`） | 最強；需要 egress（開發計畫 §8 尚未決定）。**第一階段不開放**，列入後續 |

   - `skills`、`mcp` 預設關閉；`internet` 例外，預設允許以維持既有行為。
   - 第一階段實作 `skills` 與 `mcp`；`extensions` 的欄位先保留，值固定為 false。
2. **政策儲存與生效方式比照 ADR-0010 / ADR-0011**：
   - 全域預設存在 `platform.system_settings` 的 `vibemaker.extension_policy`（明文 JSON，不含機密），資料庫優先、不必重啟，快取 30 秒。
   - **每位成員可覆寫**：`vibemaker.user_extension_grants`（`user_id`、`capability`、`effect = Allow | Deny`），沒有紀錄就繼承全域預設。跨模組只存 user id，不建 FK（ADR-0001）。
   - 管理員的任何變更都寫稽核（`extension_policy.update`、`extension_grant.set`），actor 為管理員。
3. **管制必須在伺服器端、啟動 Agent 的當下強制執行，不靠 Agent 自律或前端隱藏。**
   - `PiAgentHarness` 每次執行前由 `IExtensionPolicy.ResolveAsync(userId)` 取得有效能力，據此組合 Pi 參數（A0 spike 實測，見文末「Spike 結果」）：
     - **一律**加 `--no-extensions`（`-ne`），只以 `-e` 明列 Ymir 允許的內建擴充；否則 Agent 寫進 `/agent-state/pi-agent/extensions/` 的 TypeScript 會被 Pi 載入執行（實測現況就是如此）。
     - 沒有 `skills` 能力：加 `--no-skills`（`-ns`）；平台 skill 以 `--skill <唯讀路徑>` 另外載入，不受 `-ns` 影響。
     - 沒有 `mcp` 能力：不加 `-e builtin:mcp`；平台 MCP 需要時才加，且該次執行的 `mcp.json` 只含平台項目（見 B.3）。
   - 每次執行前，Ymir 重新寫入 Pi 的 `settings.json` 與 `trust.json`（Agent 可改寫這兩個檔讓專案層資源生效，見 Spike 結果 3）。Pi 每次執行都是新程序，設定只在 session 啟動時讀取，執行中被改寫也要到下一次才生效，而下一次又會被 Ymir 覆蓋。
   - 參數由 Ymir 後端組合，Agent 不能改；禁用時使用者的 skill 與 MCP 設定保留、不刪除，重新啟用即恢復。
4. **放在哪裡**：
   - 使用者自建的 skill 與 MCP 設定放在使用者既有的 `/agent-state/pi-agent`（使用者層），不新增 host 路徑、不新增 mount；仍然只有該使用者的 container 看得到（ADR-0007）。
   - 「專案層」擴充（`.pi/skills`、`.pi/mcp.json`、`.pi/extensions`）第一階段**不啟用**：Pi 只在專案受信任時載入，預設不信任；但信任紀錄存在 Agent 可寫的 `trust.json`，因此由 Ymir 每次執行前重寫 `trust.json` / `settings.json` 確保不信任（見 A.3）。之後要開再另議。
5. **讓 Agent 知道怎麼做**：能力啟用時，Ymir 在執行時以 `--skill` 載入一個由平台維護、唯讀的 skill（`ymir-extension-builder`），說明正確的目錄位置、命名規則與限制（例如不得要求使用者貼憑證）。能力關閉時不載入，Agent 就會回答「目前沒有這個權限，請洽管理員」。
6. **可見與可控**：
   - 新增唯讀端點讓使用者看到自己有哪些擴充與目前是否允許（經 `IWorkspaceFileReader` 同樣的 runtime 內掃描，不接受任何路徑參數）。
   - 管理員在使用者頁可看到每人的有效能力。第一階段不提供管理員檢視使用者擴充內容（隱私與範圍另議）。
7. **不共用**：使用者自建的擴充只屬於該使用者。要分享給別人，必須由開發人員審查後升級成平台項目（見 B），第一階段不提供使用者之間分享。

8. **對外連線（`internet` 能力）**：
   - 能力值決定 Agent container 的網路模式（`RuntimeNetworkAccess`）：
     - `Internet`：沿用 `VibeMaker:Runtime:Network`（預設 Podman `slirp4netns`、Docker `bridge`）；
     - `Restricted`：使用 `VibeMaker:Runtime:RestrictedNetwork`，即主機預先建立的 `--internal` network；LiteLLM 與 gateway 以 container 接上同一個 network，Agent 只連得到它們（Spike 結果 4）。
   - 沒有設定 `RestrictedNetwork` 時無法使用 `Restricted`：執行以摘要錯誤失敗（「受限網路尚未設定，請洽管理員」），管理介面同時顯示警告；不會退回成可以對外連線。
   - Network 只能在建立 container 時決定。Container 加上 label `ymir.network=internet|restricted`，`EnsureRuntime` 發現與政策不符時：
     - container 沒在執行，或呼叫端確認該使用者沒有其他執行中的 execution：移除後以新 network 重建（檔案都在掛載目錄，SA §15），寫稽核 `runtime.recreate`；
     - 否則沿用舊的 container，等下一次執行再套用，避免中斷另一個對話正在跑的 Agent。
   - 管理員變更政策不會主動停止 container，下一次執行時生效；管理介面需說明。
   - **Runtime host（ADR-0008）**：建立 runtime 的端點只多接受 enum（`network=internet|restricted`）與是否允許重建；network 名稱只來自 runtime host 自己的設定，不接受任何 network 名稱、host 路徑或資源設定，符合 ADR-0008 的紅線。協定變更同步更新 `RuntimeHostProtocolTests` 與 `RuntimeHostTests`。
   - **Local runtime** 沒有隔離（只限 Development），無法強制網路模式：只記 warning，管理介面標示「開發模式不強制」。
   - 部署文件（`deploy/runtime-host/README.md`）提供受限網路的建立方式；rootless Podman 下的行為「未驗證、待使用者環境確認」。

### B. 平台 MCP / 常駐服務（需求 2）

1. **服務目錄只能由開發人員定義，部署在版控與部署設定裡**，不在管理介面與資料庫新增：
   - 目錄檔 `deploy/mcp/servers.json`（版控）：服務名稱、說明、gateway 後端位址、所需角色。上線經一般 PR 與 CI 審查。
   - 憑證（EIP / MES 的服務帳號、資料庫連線）只放在 gateway 或服務自己的 `deploy/*/.env` 與部署 secret，不進版控、**不進 Agent container**（ADR-0004、CLAUDE.md 安全紅線）。
2. **Agent 只透過 Ymir MCP Gateway 連到平台服務**（SA §22 的 MCP Gateway）：
   - Gateway 是獨立於 Agent container 的服務，對 Agent 暴露 streamable HTTP MCP 端點；對後端的 EIP / MES / embedding 則以服務帳號連線。
   - **網路隔離**：Agent container 只能連到 gateway 與 LiteLLM，不能直接連 EIP / MES / embedding 後端。對外連線由管理員以 `internet` 能力控制（見 A.8）；關閉時 Agent 只連得到受限網路上的 LiteLLM 與 gateway。
   - 對 embedding：[RAG 知識庫計畫](../planning/rag-knowledge-base-plan.md) 已規劃平台共用的獨立 Embedding 容器，由後端而非 Agent 呼叫；若之後要讓 Agent 直接查知識庫，應包成平台 MCP 經 gateway 提供，不讓 Agent 直連。LiteLLM 本來就能提供 `/v1/embeddings`，可直接沿用使用者的 virtual key（ADR-0004），不一定要再包一層；是否要包成 MCP 工具由開發人員依需求決定。
3. **每人專屬的短期 token，不使用共用憑證**：
   - 沿用 ADR-0004 的模式：API 為使用者簽發 gateway token（含 user id、有效期限，建議 ≤ 1 小時，每次執行重新簽發），以 `exec --env NAME` 傳入，值不出現在程序參數。
   - Pi 的 MCP 設定用 `--bearer-token-env-var` 引用，設定檔裡不含 token 值。
   - 平台 MCP 的設定由 Ymir 注入：Pi 1.0.0 只從 `PI_CODING_AGENT_DIR/mcp.json`（與受信任專案的 `.pi/mcp.json`）讀 MCP 設定，**沒有唯讀層可用**（Spike 結果 2）。因此改為：
     - 每次執行前，Ymir 重新產生 `/agent-state/pi-agent/mcp.json`：平台項目（該使用者有權限的）+ 使用者自建項目（僅在 `mcp` 能力開啟時）；名稱衝突時平台項目覆蓋使用者項目；
     - 使用者自建的項目另存在 Ymir 管理的檔案（例如 `mcp.user.json`），Ymir 合併時讀它；`mcp` 能力關閉時只是不合併，不刪除；
     - Agent 在執行中改寫 `mcp.json` 不會生效（Pi 只在 session 啟動時讀取），下一次執行又被 Ymir 覆蓋；
     - **真正的強制點是 gateway 的 token 與存取清單**：即使 Agent 自己寫了指向 gateway 的設定，也只能拿自己的 token、只能用自己有權限的服務。
4. **授權在 gateway 強制**：
   - 管理員在管理介面對每個平台服務設定「啟用 / 停用」與「可用對象」（角色或個別使用者）；這只改存取清單，**不能新增服務或位址**。
   - Gateway 每次呼叫都驗證 token → user id → 存取清單；沒有權限回 403，Agent 看到的是摘要錯誤（SA §10、§12）。
   - Gateway 為每次工具呼叫寫稽核（user id、服務、工具名稱、結果，不含參數內容與回傳資料），並套用每人 rate limit 與逾時。
5. **Agent 如何「選擇」**：使用者的有效平台 MCP 清單 = 目錄 ∩ 存取清單。Ymir 只把該使用者有權限的服務寫進該次執行的 MCP 設定，並附上 `description`，Pi 會把它列進系統提示，由 Agent 依任務決定是否呼叫。沒權限的服務 Agent 完全看不到。
6. **與需求 1 的邊界**：
   - 使用者自建的 MCP 與平台 MCP 在來源上分開（自建存在 `mcp.user.json`，平台來自 `deploy/mcp/servers.json` + 存取清單），由 Ymir 每次執行合併成 `mcp.json`，名稱衝突時**平台優先**，避免使用者自建同名服務冒充平台服務。
   - 即使使用者自建了 MCP，它拿不到任何平台 token 以外的憑證，也連不到後端；它能做的事不會超過該使用者的 container 本來就能做的事。

### C. API 與管理介面（概要，細節在實作時定稿並更新 OpenAPI）

- 管理員（`AdminPolicy`，加入授權矩陣 `AdminOnlyRequests`）：
  - `GET / PUT /api/admin/extension-policy`
  - `GET / PUT /api/admin/users/{id}/extension-grants`
  - `GET /api/admin/mcp-servers`、`PUT /api/admin/mcp-servers/{name}/access`（只能改啟用與對象）
- 成員（登入即可，只回自己的資料，不接受任何 user id 或路徑）：
  - `GET /api/extensions`：我的有效能力、我的自建擴充清單、我可用的平台 MCP 摘要
- 所有狀態變更端點加 `RequireAntiforgeryHeader()`，並加入 `AuthorizationMatrixTests`。
- Gateway 本身不走 Cookie：只接受簽發的 token，不暴露給瀏覽器。

## 分階段

| 階段 | 內容 | 驗證方式 |
|---|---|---|
| 1 | 政策與每人覆寫、`skills` 與 `mcp` 能力的強制執行、管理介面、`ymir-extension-builder` skill、稽核 | Fake LLM + 真實 Pi：政策關閉時使用者放的 skill / `mcp.json` 不被載入，開啟時被載入 |
| 2 | Gateway 骨架、token 簽發、存取清單、稽核；用一個假的 MCP server（echo）驗證端到端與授權矩陣 | 整合測試；Agent container 無法直連後端 |
| 3 | 開發人員逐一上線真實服務（EIP、MES、embedding），各自一份 PR（含服務、目錄、測試、安全審查） | 依各服務需求；需要內網資源時標註「未驗證、待使用者環境確認」 |
| 後續 | `extensions` 能力（需先決定 egress）、使用者擴充分享與升級流程 | — |

## 影響

- 新增 `IExtensionPolicy`（Application 層）與 `PiAgentHarness` 的參數組合邏輯；`PiAgentHarnessTests` 與 `PiProcessSpecTests` 需覆蓋各種能力組合。
- 新增 `vibemaker.user_extension_grants` 資料表與 migration；`vibemaker.extension_policy` 沿用 `system_settings`。
- 新增 gateway 元件與部署文件（`deploy/mcp/`）；它與 runtime host、API 一樣屬於平台，**不得**掛載 container runtime socket 或使用者 workspace。
- 平台 skill（`ymir-extension-builder`）放在 Agent runtime image 內的唯讀路徑（image 本身 `--read-only`），不需要新增 mount；平台 MCP 設定以每次執行重新產生的方式注入，也不需要新增 mount。若之後仍需新增 mount，必須同步更新 `ContainerCommandBuilderTests`（Podman 與 Docker 兩種 engine），且不得掛載 host 敏感路徑。
- 管理員啟用 `mcp` 能力等於允許該使用者的 Agent 連到使用者指定的外部位址；這與目前 Agent 已可在 bash 執行 `curl` 的實際能力相近，但稽核上更明顯，文件需清楚告知管理員。
- Agent 可寫的使用者層目錄今天就可能放 skill 或 `mcp.json`；本 ADR 落地後這件事才有政策與稽核，在此之前行為未定義。

## 已決定事項（使用者 2026-10-07 確認）

1. **權限粒度**：全域預設 + 每位成員覆寫。角色、部門、Entra 群組留待之後再說。
2. **第一階段開放 `skills` 與 `mcp`**，兩者都預設關閉、由管理員開啟。`extensions` 維持後續項目。
3. **Gateway 做成獨立服務**（新的 `Ymir.McpGateway` 專案與部署單元），不與 API 同程序。
4. **Egress 由管理員控制，預設允許**（2026-10-07 依 A0 spike 結果決定）：
   - 新增能力 `internet`，粒度同 `skills` / `mcp`（全域預設 + 每人覆寫），全域預設 **允許**，維持既有行為；
   - 不論允許與否，Agent 都必須連得到 LiteLLM 與 MCP Gateway；
   - 平台服務的授權仍然由 gateway 的 token 與存取清單強制，不依賴網路隔離。做法見 A.8。

## Spike 結果（A0，2026-10-07）

以沙箱的真實 Pi 1.0.0（RPC 模式，參數與環境變數同 `PiAgentHarness`）搭配會記錄請求內容的假模型實測：依模型收到的 system prompt 是否含 skill 標記、`tools` 清單是否含 MCP 工具判斷，不依賴 Pi 的輸出文字。測試用的 skill、`mcp.json`（一個 stdio echo MCP server）、extension 都放在 Agent 可寫的位置。

1. **Skill**：

   | 參數 | 使用者層 skill（`agentDir/skills`） | 專案層 skill（受信任時） | `--skill <路徑>` |
   |---|---|---|---|
   | 現行參數 | 載入 | 載入 | — |
   | `-ns` | 不載入 | 不載入 | — |
   | `-ns --skill <路徑>` | 不載入 | 不載入 | 載入 |

2. **MCP 與 extension**：

   | 參數 | 使用者 `mcp.json` 的工具 | Agent 寫的 extension（`agentDir/extensions`、受信任專案 `.pi/extensions`） |
   |---|---|---|
   | 現行參數 | 載入 | **載入並執行** |
   | `-ne` | 不載入（`codemode`、`tool-search` 等內建擴充也一起關閉） | 不載入 |
   | `-ne -e builtin:mcp` | 載入 | 不載入 |
   | `-ne -e builtin:codemode -e builtin:tool-search` | 不載入 | 不載入 |

   - MCP 設定只來自 `getAgentDir()/mcp.json` 與受信任專案的 `.pi/mcp.json`，沒有額外的設定檔位置或唯讀層。
   - 設定只在 `session_start` 讀取（`extensions/mcp/index.js`）；執行中改寫檔案不會生效。
   - MCP 工具預設以 `codemode` 方式提供（藏在 `codemode` 工具後面），`exposure: "direct"` 時直接列在 `tools`。
   - **現況風險**：目前的參數下，Agent 寫的 skill、`mcp.json`、extension 都會在下一次執行被載入。A1 落地前這是未管制的狀態；A1 必須一律加 `-ne`。

3. **Project trust**：
   - 預設不信任：只在 workspace 放 `.pi/mcp.json` 時不會載入。
   - 但 Agent 只要寫 `agentDir/trust.json`（`{"<workspace 路徑>": true}`）或 `agentDir/settings.json`（`{"defaultProjectTrust":"always"}`），下一次執行專案層的 `mcp.json`、skill、extension 都會載入。
   - `-ne` / `-ns` 仍然有效；為了讓「平台優先」與政策可預期，Ymir 每次執行前重寫這兩個檔（A.3）。

4. **Egress**（沙箱的 Podman 4.9.3 是 root 模式，非 rootless）：

   | `--network` | host 回送位址（10.0.2.2） | host 的對外 IP | 網際網路 |
   |---|---|---|---|
   | `none` | 不通 | 不通 | 不通 |
   | `slirp4netns:allow_host_loopback=false`（現行預設的行為） | 不通 | **通** | **通** |
   | `slirp4netns:allow_host_loopback=true` | 通 | 通 | 通 |
   | 自訂 `--internal` network | — | 不通 | 不通 |

   - `--internal` network 上：同一 network 的其他 container 可以用名稱連到；network 的閘道 IP（host 端）上聽 `0.0.0.0` 的服務**也連得到**，所以 host 上的服務要只綁必要介面或另設防火牆。
   - `RuntimeOptions.Network`（`VibeMaker__Runtime__Network`）已經可以設定，不必改 `ContainerCommandBuilder`。
   - **未驗證、待使用者環境確認**：rootless Podman 下 `--internal` network 與 LiteLLM / gateway container 共用 network 的實際行為（rootless 的 bridge 在 `ymir` 帳號的 network namespace 內，host 服務不一定連得到）。

### Egress 決定（使用者 2026-10-07）

- 對外連線改由管理員控制（`internet` 能力，全域預設 + 每人覆寫），**預設允許**。設計見 A.8。
- 正式主機要關閉對外連線前，需先依部署文件建立受限網路並把 LiteLLM 與 gateway 接上；rootless 的實際行為待使用者在正式主機確認。
