# ADR-0012：Agent 擴充能力——管理員管制的使用者自建擴充，與開發人員維護的平台 MCP

- 狀態：提議中（待使用者確認「待決定事項」後改為已採納；補充 ADR-0003、ADR-0004、ADR-0007、ADR-0010）
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
   | `extensions` | Pi extension / package（可執行程式碼，可 `npm install`） | 最強；需要 egress（開發計畫 §8 尚未決定）。**第一階段不開放**，列入後續 |

   - 第一階段實作 `skills` 與 `mcp`；`extensions` 的欄位先保留，值固定為 false。
2. **政策儲存與生效方式比照 ADR-0010 / ADR-0011**：
   - 全域預設存在 `platform.system_settings` 的 `vibemaker.extension_policy`（明文 JSON，不含機密），資料庫優先、不必重啟，快取 30 秒。
   - **每位成員可覆寫**：`vibemaker.user_extension_grants`（`user_id`、`capability`、`effect = Allow | Deny`），沒有紀錄就繼承全域預設。跨模組只存 user id，不建 FK（ADR-0001）。
   - 管理員的任何變更都寫稽核（`extension_policy.update`、`extension_grant.set`），actor 為管理員。
3. **管制必須在伺服器端、啟動 Agent 的當下強制執行，不靠 Agent 自律或前端隱藏。**
   - `PiAgentHarness` 每次執行前由 `IExtensionPolicy.ResolveAsync(userId)` 取得有效能力，據此組合 Pi 參數：沒有 `skills` 能力就加 `--no-skills`；沒有 `mcp` 能力就不載入使用者層的 MCP 設定。
   - 參數由 Ymir 後端組合，Agent 不能改；禁用時檔案保留、不刪除，重新啟用即恢復。
   - **MCP 的「關閉」方式需實測確認**（見待驗證項目 1），不能只相信文件。
4. **放在哪裡**：
   - 使用者自建的 skill 與 MCP 設定放在使用者既有的 `/agent-state/pi-agent`（使用者層），不新增 host 路徑、不新增 mount；仍然只有該使用者的 container 看得到（ADR-0007）。
   - 「專案層」擴充（`.pi/skills`）第一階段**不啟用**：Pi 對專案層資源有 project trust 機制，在 RPC 模式預設不載入；之後要開再另議。
5. **讓 Agent 知道怎麼做**：能力啟用時，Ymir 在執行時以 `--skill` 載入一個由平台維護、唯讀的 skill（`ymir-extension-builder`），說明正確的目錄位置、命名規則與限制（例如不得要求使用者貼憑證）。能力關閉時不載入，Agent 就會回答「目前沒有這個權限，請洽管理員」。
6. **可見與可控**：
   - 新增唯讀端點讓使用者看到自己有哪些擴充與目前是否允許（經 `IWorkspaceFileReader` 同樣的 runtime 內掃描，不接受任何路徑參數）。
   - 管理員在使用者頁可看到每人的有效能力。第一階段不提供管理員檢視使用者擴充內容（隱私與範圍另議）。
7. **不共用**：使用者自建的擴充只屬於該使用者。要分享給別人，必須由開發人員審查後升級成平台項目（見 B），第一階段不提供使用者之間分享。

### B. 平台 MCP / 常駐服務（需求 2）

1. **服務目錄只能由開發人員定義，部署在版控與部署設定裡**，不在管理介面與資料庫新增：
   - 目錄檔 `deploy/mcp/servers.json`（版控）：服務名稱、說明、gateway 後端位址、所需角色。上線經一般 PR 與 CI 審查。
   - 憑證（EIP / MES 的服務帳號、資料庫連線）只放在 gateway 或服務自己的 `deploy/*/.env` 與部署 secret，不進版控、**不進 Agent container**（ADR-0004、CLAUDE.md 安全紅線）。
2. **Agent 只透過 Ymir MCP Gateway 連到平台服務**（SA §22 的 MCP Gateway）：
   - Gateway 是獨立於 Agent container 的服務，對 Agent 暴露 streamable HTTP MCP 端點；對後端的 EIP / MES / embedding 則以服務帳號連線。
   - **網路隔離**：Agent container 只能連到 gateway 與 LiteLLM，不能直接連 EIP / MES / embedding 後端。實際做法依 egress 決策（開發計畫 §8）與主機網路設定，列為待驗證項目 2。
   - 對 embedding：[RAG 知識庫計畫](../planning/rag-knowledge-base-plan.md) 已規劃平台共用的獨立 Embedding 容器，由後端而非 Agent 呼叫；若之後要讓 Agent 直接查知識庫，應包成平台 MCP 經 gateway 提供，不讓 Agent 直連。LiteLLM 本來就能提供 `/v1/embeddings`，可直接沿用使用者的 virtual key（ADR-0004），不一定要再包一層；是否要包成 MCP 工具由開發人員依需求決定。
3. **每人專屬的短期 token，不使用共用憑證**：
   - 沿用 ADR-0004 的模式：API 為使用者簽發 gateway token（含 user id、有效期限，建議 ≤ 1 小時，每次執行重新簽發），以 `exec --env NAME` 傳入，值不出現在程序參數。
   - Pi 的 MCP 設定用 `--bearer-token-env-var` 引用，設定檔裡不含 token 值。
   - 平台 MCP 的設定由 Ymir 注入、Agent 不可改：以**唯讀**的方式提供給 Pi（不放在 Agent 可寫的 `/agent-state`），具體機制待驗證（待驗證項目 1）。
4. **授權在 gateway 強制**：
   - 管理員在管理介面對每個平台服務設定「啟用 / 停用」與「可用對象」（角色或個別使用者）；這只改存取清單，**不能新增服務或位址**。
   - Gateway 每次呼叫都驗證 token → user id → 存取清單；沒有權限回 403，Agent 看到的是摘要錯誤（SA §10、§12）。
   - Gateway 為每次工具呼叫寫稽核（user id、服務、工具名稱、結果，不含參數內容與回傳資料），並套用每人 rate limit 與逾時。
5. **Agent 如何「選擇」**：使用者的有效平台 MCP 清單 = 目錄 ∩ 存取清單。Ymir 只把該使用者有權限的服務寫進該次執行的 MCP 設定，並附上 `description`，Pi 會把它列進系統提示，由 Agent 依任務決定是否呼叫。沒權限的服務 Agent 完全看不到。
6. **與需求 1 的邊界**：
   - 使用者自建的 MCP 與平台 MCP 在設定上分開（自建在使用者層，平台在唯讀層），名稱衝突時**平台優先**，避免使用者自建同名服務冒充平台服務。
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
- `ContainerCommandBuilder` 若需要新增唯讀 mount（注入平台 MCP 設定），必須同步更新 `ContainerCommandBuilderTests`（Podman 與 Docker 兩種 engine），且不得掛載 host 敏感路徑。
- 管理員啟用 `mcp` 能力等於允許該使用者的 Agent 連到使用者指定的外部位址；這與目前 Agent 已可在 bash 執行 `curl` 的實際能力相近，但稽核上更明顯，文件需清楚告知管理員。
- Agent 可寫的使用者層目錄今天就可能放 skill 或 `mcp.json`；本 ADR 落地後這件事才有政策與稽核，在此之前行為未定義。

## 待決定事項（請使用者回覆）

1. **權限粒度**：只要「全域預設 + 每位成員覆寫」就夠，還是也要「依角色 / 部門 / 群組」？（建議先做前者，Entra 群組之後再說。）
2. **`mcp` 能力是否第一階段就開放**，還是先只開 `skills`？`mcp` 會讓 Agent 能連到使用者指定的外部位址。
3. **Gateway 部署位置**：與 API 同一個程序（多一組端點，最簡單）或獨立服務（隔離較好，多一個部署單元）？建議獨立服務，但會增加維運面。
4. **Egress 政策**（開發計畫 §8）：Agent container 目前的外連範圍要不要先收斂？這會決定「Agent 不能直連 EIP / MES」能否在網路層做到，而不只是靠 gateway 驗證。

## 待驗證項目（實作第一步以 spike 完成，結果寫進本 ADR）

1. **Pi 的 MCP 設定載入與關閉機制**：
   - 有沒有不讀使用者層 `mcp.json` 的辦法（`-ne` 搭配 `-e builtin:mcp`、設定檔位置的環境變數，或 settings）？
   - 平台 MCP 設定能不能放在 Agent 不可寫的位置（唯讀 mount 或 Ymir 每次執行產生的檔案）並確保 Agent 無法覆蓋？
   - 若 Pi 無法做到，備案是由 Ymir 在 Pi 之外處理 MCP（例如以自己的 extension 或 gateway 代理）。
2. **Agent container 的網路**：目前 `slirp4netns`（Podman）/ `bridge`（Docker）下，如何限制只能連 gateway 與 LiteLLM；Rootless 環境的可行做法。
3. **RPC 模式下 `--no-skills` 與 `--skill` 的行為**，以及 project trust 在 RPC 模式的實際預設（文件說預設不載入，需實測）。
4. 實測結果若與本 ADR 假設不符，先更新本 ADR 再實作，不得在沒有更新的情況下偏離。
