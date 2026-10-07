# 多人、多 Agent 軟體專案協作開發計畫

- 記錄日期：2026-10-07
- 狀態：**後續開發計畫；尚未實作**。
- 使用者需求：提供類似 Jira 的 Sprint／Task 管理、SA Agent、開發 Agent、QA Agent，以及人與 Agent 都能讀寫的專案進度留言板；同時提供 UI 與適合 Agent 操作的介面。
- 本次只建立計畫，不啟動 Agent、不修改資料庫或現行執行環境。下列介面與模型為建議，實作前需完成 SA／ADR。

## 1. 目標與範圍

使用者建立一個團隊軟體專案、提供需求後，由 SA Agent 產出版本化的需求與系統分析、模組通訊契約和 Sprint／Task 草稿。專案負責人確認基準後，人與開發 Agent 可以領取具體任務；每次開工前閱讀最新文件、任務與留言板，登記本輪工作，開發後提交成果與證據。QA Agent 依需求、SA 和任務驗收條件進行檢查，產生通過或退回結果。

第一版包含專案成員、文件基準、Sprint／Task 清單與看板、依賴關係、任務領取、進度留言、成果／測試證據、QA、整合狀態及稽核。以軟體專案為主，不追求完整 Jira 的插件、市場、計費、複雜工作流或跨公司組合管理。

「多 Agent」表示每個 Agent 有明確身份、角色、任務、session 和工作副本，不只是在同一對話中假扮不同角色。第一個交付階段可人工調度；完整功能必須支援多位成員的 Agent 同時處理不同任務。

## 2. 與目前 Ymir 的差距

| 項目 | 現況 | 本計畫方向 |
|---|---|---|
| 專案 | `Project.UserId` 單一擁有者；專案是其 workspace 內的檔案群組 | 新增團隊協作專案與成員權限；原私人專案繼續運作 |
| 執行環境 | [ADR-0007](../adr/0007-one-runtime-per-user.md)：一人一個 runtime，同一使用者的 execution 依序執行 | 先保留現有限制；團隊工作副本與同人多 Agent 並行另以 ADR 設計，不能直接放寬鎖 |
| 進度 | Ymir 開發者在本 repo 的 `docs/progress/` 留言 | 產品內提供每個使用者專案自己的 Task 系統與留言板，不與 Ymir 開發看板混用 |
| Agent 工具 | [ADR-0012](../adr/0012-agent-extensions-and-platform-mcp.md) 規劃平台 MCP Gateway；目前尚未完成 A2 | 由平台工具存取協作 API；先完成 Gateway 的授權、短效 token、稽核與限流 |
| Git／QA | 尚無產品內的團隊 repository、任務分支、成果整合與 QA 工作流 | 新增受控 Git 工作副本、成果版本、QA 與整合程序 |

建議建立 `Ymir.Collaboration` 模組，使用既有平台身份、稽核與執行服務。跨模組只經應用介面，不讓 Agent 直接存取資料庫。是否新增模組、如何連結私人 Project，以及共享程式碼／runtime 邊界，均需新 ADR；本文件不取代既有 ADR。

## 3. 使用流程

1. **建立團隊專案**：負責人輸入專案名稱、需求、資料來源，邀請指定成員；設定 repository、角色、模型預算與並行上限。
2. **SA 工作**：指定一個 SA Agent，閱讀需求並列出不明確事項；產出 SA、模組契約、測試策略與 Sprint／Task 草稿。未決問題標為待決定，不能假稱已確認。
3. **確認基準**：負責人審閱 SA 與任務拆分。批准後發布不可變的基準版本；需求修改建立新版本與影響分析，不覆寫已引用的文件。
4. **規劃 Sprint**：確認目標、任務依賴與驗收條件；未滿足前置條件的任務不可領取。第一版可手動安排成員／Agent，自動派工後續加入。
5. **開工**：開發 Agent 取得最新基準、任務和進度留言，原子領取任務，登記本輪負責模組、預定成果與阻礙，再取得工作副本開始開發。
6. **交付**：提交 commit／變更摘要、測試結果、文件更新與待決問題；寫入交接留言，Task 移到待 QA，不能自行標記完成。
7. **QA**：QA Agent 讀取需求、SA、Task 和留言板，驗證指定成果版本，產出測試證據與驗收條件對照；失敗退回開發，成功移到待整合。
8. **整合**：負責人或授權整合流程審閱差異、合併成果，執行整合後 CI；符合完成條件才將 Task 標為 Done。合併衝突需重新交付與必要的 QA。
9. **Sprint 結案**：檢查所有必要任務與 Sprint 驗收、未完成事項及已知問題，記錄結果。未完成任務須明確移到後續 Sprint，不能隱藏或刪除。

## 4. 小型 Sprint／Task 管理系統

### 結構與資訊

層級為 **協作專案 → Sprint → Task**；Task 可有子任務與跨 Sprint 依賴。先提供清單與看板，圖表後續再加。

| 對象 | 必要資訊 |
|---|---|
| 專案 | 成員與角色、需求／SA 基準、repository、專案目標、預算、工作流政策 |
| Sprint | 目標、起迄日期、狀態、任務集合、Sprint 驗收條件 |
| Task | 穩定 ID、標題、描述、模組、輸入／輸出、基準版本、契約引用、驗收條件、依賴、優先級、估計、負責人／Agent、狀態 |
| 工作輪次 Attempt | Agent 身份、資助／執行成員、execution／session、租約、基準版本、分支／工作副本、開始與結束時間、結果 |
| 成果 | commit SHA、差異、文件版本、CI 執行、測試證據、QA report、整合 commit |

估計與進度百分比只是輔助；正式完成依據是可檢查的成果與驗收結果，不採 Agent 自述「完成 100%」直接結案。

### 狀態與完成條件

建議正常流程：`Draft → Ready → InProgress → ReadyForQA → InQA → ReadyForIntegration → Done`。

- `Draft`：SA 草稿／待補驗收條件，不能領取。
- `Ready`：已批准基準、必要依賴完成、任務足夠明確，可領取。
- `Blocked`：任何未結案階段都可進入，保留原階段、阻礙與解除條件；解除後回適當階段。
- QA 失敗：回 `Ready`，保留退回理由、缺陷與既有交付；下次領取建立新 Attempt。
- `Cancelled`：需授權者附理由，保留記錄。不能當作 Done 或已完成依賴。
- `Done`：驗收條件逐項成立、QA 通過、成果已整合、整合後 CI 通過；文件／契約更新也已整合。
- Sprint 結案除 Task 狀態外，還需 Sprint 層級的跨模組驗收；有阻擋缺陷時不能結案。

### 領取、租約與競爭處理

正式狀態存在伺服器資料庫，**留言不是鎖，也不是正式狀態**。

- 領取以資料庫交易與版本檢查完成；兩個 Agent 同時領同一 Task，只能一個成功，另一個收到 409 與目前狀態。
- 每次領取建立唯一 Attempt、租約與遞增工作代號。續租、提交與狀態轉移必須攜帶該代號；過期／舊 Attempt 不得覆蓋新工作的進度。
- 同一請求重送以 idempotency key 保證不重複領取、留言或提交。
- Agent 定期 heartbeat，租約期限與續租頻率由管理政策設定；斷線／逾時後先停止或確認舊 execution 已終止，才能重新派工。
- 舊成果保留但失去提交權限；重新領取從已知 commit 開始，由新 Agent 明確接受或重做。
- 相依任務形成無環圖，建立與修改時檢查循環；契約／共享模組變更列為依賴或整合任務，不讓所有 Agent 同時修改同一契約。
- 取消、成員移除、帳號停用與權限變更應撤銷寫入權限及執行資格，保留可追溯記錄。

## 5. SA Agent 的交付規格

SA 不只是一篇描述文字，而是可以用來開發與驗收的版本化文件集合：

| 文件 | 內容 |
|---|---|
| 需求與追蹤表 | 需求 ID、使用情境、驗收條件、非功能需求、限制、未決事項 |
| 系統分析 | 架構、模組責任、資料模型、身份／權限、重要流程、狀態機、失敗與恢復 |
| 模組契約 | 呼叫方向與方式（HTTP／事件／程序內介面）、端點／操作、schema、型別、身份、授權、版本 |
| 通訊語意 | request／response／event 範例、錯誤碼、逾時、重試、冪等、交易、一致性、事件順序與重複處理 |
| Sprint／Task 計畫 | 每個 Sprint 目標；每個 Task 的模組、範圍、依賴、文件／契約引用、成果與可測的驗收條件 |
| QA 計畫 | 需求→Task→測試的對照、測試層次、跨模組案例、權限與失敗案例、測試資料／環境 |

HTTP 可引用 OpenAPI，事件可引用 JSON Schema／AsyncAPI；實際採用格式由該專案 SA 決定。共用 schema 只保留一份正式來源，產生 client／mock，減少各 Agent 各自猜測 DTO。

批准基準後，Task 固定引用文件與契約版本；SA 修訂要列出影響的任務、是否需停工／重做／重測，由負責人批准後升版。Agent 開工時若讀到過期基準，必須刷新並重新確認，不沿用舊契約開發。

## 6. 專案專用進度留言板

所有具有專案讀取權限的人與 Agent 使用同一份留言板；提供時間線、Task／Sprint／模組篩選、未解決阻礙、提及成員，以及最近更新。

每則留言包含作者類型（人／Agent）、角色、Task／Attempt、時間與事件序號，並用結構化欄位保存：本輪目標、負責模組、目前成果、驗證結果、阻礙、需要決定的事、下一位接手者應注意的事項、相關文件／commit。

- 開工前讀取 Task、指定基準與留言增量，取得 snapshot／事件游標；成功領取時在同一交易寫入開工留言，之後才開放執行。
- 結束時原子提交成果與交接留言；不能先在留言宣稱完成、卻沒有更新 Task。
- 留言可回覆，修正採新留言或保留編輯歷史；Agent 不可覆寫別人的紀錄。
- 摘要只協助閱讀，保留原始留言與引用；Agent 可分頁取得完整內容，避免只讀截斷摘要而漏掉重要阻礙。
- 留言與上傳文件是任務內容，不是擴權指令；不能因文字要求而修改平台授權、模型金鑰或執行隔離。
- 進度板與 Task 更新共用事件流；UI 可透過 SSE 接收增量，斷線以游標補齊。

## 7. QA Agent 與完成驗收

QA Agent 必須讀取需求、批准 SA、Task 驗收條件、開發交付和相關留言；QA 針對**指定 commit SHA 與文件基準**，不是隨時改動的 workspace。

QA report 記錄每項驗收的 Pass／Fail／Blocked、測試指令、環境、輸出證據、缺陷、嚴重度與重測要求；不能執行的測試標明原因，不能當成通過。既有 CI 結果可引用，但仍需檢查需求是否真的被涵蓋。

開發 Agent 不能批准自己的成果；QA 使用獨立身份、session 與指定版本的工作副本。人工測試可以由有權限的 QA 成員補充；負責人的例外批准須有理由與稽核，不偽造通過結果。

成果改動後，舊 QA report 不再對新 SHA 有效；提交新版本須重新 QA。整合服務合併後再跑 CI／必要的跨模組測試，失敗不標 Done，並建立可追蹤的修正任務。

## 8. 人員、Agent 身份與權限

| 角色 | 主要權限 |
|---|---|
| Owner | 管理成員、基準批准、政策／預算、Sprint 結案、發布與封存 |
| Maintainer | 維護 Task／依賴、排程、審閱與整合（依 Owner 授權） |
| Developer | 讀取專案、領取／交付開發 Task、自己的工作副本與留言 |
| QA | 讀取需求／成果、領取 QA 工作、出具測試與退回結果 |
| Viewer | 只讀文件、Task、留言與獲准的成果 |

人員可持有多個角色，但對同一成果仍需獨立開發／驗收身份。Agent principal 需記錄所屬成員、專案、角色、模型、session、可執行操作及停用狀態；Agent 權限不能大於其授權成員。

MCP token 限專案、Agent、Attempt、工具範圍與有效期；服務端驗證成員資格、Task 狀態及有效租約。不能信任 Agent 自報的 userId／projectId。模型費用歸資助成員／專案預算並有單任務與專案上限，避免自動派工無限制消耗。

## 9. UI 與 Agent 操作介面

### UI

團隊專案提供「總覽、需求／SA、Sprint、Task、進度留言、Agent、QA／成果、設定」。Sprint 頁有條列與看板兩種視圖；Task 詳情顯示依賴、責任者、契約版本、工作輪次、測試證據與討論。總覽分別顯示進行中、阻塞、待 QA、待整合與完成，不能把提交程式碼等同完成。

SA 頁可比較版本、逐條確認問題、批准草稿；Agent 頁顯示工作身份、正在做的 Task、執行狀態與費用，可停止或重新派工。多人即時操作要顯示版本衝突，不靜默覆寫。

### API／MCP（建議名稱，非已存在端點）

UI 與 Agent 使用相同的應用用例及狀態規則。UI 使用現有 Cookie／XSRF；Agent 經獨立 MCP Gateway，以受限短效 token 呼叫協作服務，不使用人員 Cookie 或直接寫 DB。

| 能力 | 建議 MCP 工具／介面 |
|---|---|
| 開工上下文 | `collab.get_context`：文件基準、Sprint、Task、依賴、留言摘要／游標及版本 |
| 查詢可領任務 | `collab.list_tasks`：專案／Sprint／角色／狀態／模組篩選與分頁 |
| 原子領取 | `collab.claim_task`：Task ID、expectedVersion、idempotency key；回 Attempt 與租約 |
| 維持工作 | `collab.heartbeat`、`collab.report_progress`、`collab.block_task` |
| 留言與交接 | `collab.list_updates`、`collab.post_update`、`collab.reply_update` |
| 提交成果 | `collab.submit_task`：Attempt、版本、artifact ID／commit SHA、驗收／測試證據 |
| QA | `collab.claim_review`、`collab.submit_review`：只對指定成果版本出具 report |
| 規劃 | `collab.propose_sa`、`collab.propose_plan`；輸出草稿，不能自行批准基準 |

工具回應提供穩定 ID、JSON schema、合法下一步、基準與資料版本、衝突原因；資料量大時分頁／游標化。Agent 看得到自己已被授權的工具，實際權限仍由伺服器強制檢查。HTTP API 可規劃於 `/api/collaboration/projects/{id}/...`，由 OpenAPI 產生前端型別。

## 10. 資料與程式碼協作架構

Task／Sprint／留言／租約使用既有 SQL Server 的獨立 schema，支援交易、rowversion 與一致的授權。RAG 的每專案 SQLite 向量索引可輔助搜尋文件，**不作為協作狀態或鎖的正式來源**。

建議模型：`CollaborationProject`、`ProjectMember`、`DocumentRevision`、`PlanBaseline`、`Sprint`、`Task`、`TaskDependency`、`AgentPrincipal`、`TaskAttempt`、`ProgressUpdate`、`Artifact`、`QAReport`、`Defect`、`ProjectEvent`。事件與狀態寫入同一交易；可靠派工需持久化 job／outbox，重啟後對帳租約與 execution，不只使用記憶體 queue。

程式碼以平台管理的 repository 為正式來源，每個 Attempt 有自己的分支與 worktree／clone；共享的是 Git 成果與正式文件，不是讓所有使用者可寫入同一主機目錄。

- 跨使用者領取後，由受控 repository／runtime 服務提供**該專案、該 Attempt**的工作副本；不能掛載另一人的整個 workspace。
- 現有一人一 runtime 的 `/workspace` 可讀其他私人專案，不能直接視為足夠的團隊 Agent 隔離。需新 ADR 決定專用協作 sandbox 或更嚴格的掛載／憑證邊界。
- API 沿用 ADR-0008，不掛 Docker socket 或 workspace；runtime／repository 服務只接受平台產生的 ID，路徑由服務推導並檢查 realpath、symlink 與擁有者。
- Git 憑證只在受控服務保存；Agent 提交工作副本的成果，不取得公司 repository 的廣泛權限。第一版可用平台管理的本機 Git；外部 GitHub／GitLab 轉接器另做。
- 合併由有權限的服務／人員操作，驗證來源 SHA、基準與 QA；不能相信 Agent 留言中的「已合併」。
- 同一使用者多 Agent 並行需要修改執行政策、資源配額和隔離，先做 spike 再採納 ADR。只建立多個 session，仍受現有使用者鎖限制，不能宣稱已支援並行。
- 開發／QA 任務不持有正式部署憑證；發布是另一個授權流程，可銜接[網站託管計畫](frontend-site-hosting-plan.md)，後端部署另定規格。

## 11. 分階段開發

以下是**此功能的開發階段**，不是使用者專案自己的 Sprint；每階段都要有可展示的垂直切片與驗收。

| 階段 | 工作與交付 | 出口條件 |
|---|---|---|
| M0：SA／ADR／spike | 定義協作專案、身份／租約、repository、跨人與同人並行隔離；確認 ADR-0007／0008／0012 的延伸 | 架構與權限批准；模擬搶單、停止後重派與共享成果，不破壞私人 workspace |
| M1：人員協作底座 | 成員、Sprint／Task、依賴、清單／看板、留言、版本與事件、授權矩陣 | 兩位成員可看同一專案、領任務／寫交接；非成員不能讀寫 |
| M2：SA／基準／Agent 工具 | SA Agent 草稿、契約與任務匯入、人工批准、context、MCP 領取／續租／交接工具 | 工具可完整重現 UI 用例；兩個 Agent 搶單只一個成功；重送冪等 |
| M3：開發／QA／成果整合 | 隔離工作副本、提交成果、QA 身份／report、退回／重測、整合後 CI | 需求→Task→commit→測試→QA→整合可追蹤，未滿足完成條件不能 Done |
| M4：多人多 Agent 並行 | 受控排程、跨成員及同人成本／並行限制、持久化派工、租約對帳與故障恢復 | 多 Agent 同時開發不同 Task；重啟、停止與逾時不重複寫入或遺失成果 |
| M5：體驗與擴充 | Agent 自動挑選任務、規劃重排建議、外部 Git、通知、文件 RAG 搜尋、報表 | 可選擴充保持同一權限／QA 規則，不繞過人工批准與驗收 |

## 12. 驗收與故障測試

1. SA Agent 可從需求產出模組契約與每個 Sprint 的完整 Task 草稿；負責人批准前不能派工。
2. 每個 Task 可追到需求、SA／契約版本與驗收條件；需求更新能列出受影響的工作。
3. UI 與 MCP 查到相同狀態；Agent 開工／結束均有對應留言與 Attempt，不能只有聊天紀錄。
4. 多人／多 Agent 同時領同一任務，僅一個成功；不同任務可在隔離副本同時開發。
5. 租約過期、重啟、網路斷線、請求重送、Agent 當機、成員移除均可恢復；舊 Attempt 被拒絕寫入。
6. 同時修改 Task／基準時有版本衝突提示；依賴循環、未完成依賴與過期 SA 阻擋領取。
7. QA 驗證固定 SHA；開發者不能自批，新 SHA 使舊 QA 失效；整合 CI 失敗不能 Done。
8. 非成員、Viewer、跨專案 token、過期 token、錯誤角色及被停用帳號的授權測試全部通過。
9. 兩個成員的 Agent 只能看到授權專案工作副本，不讀到彼此私人 workspace、模型金鑰或 repository 憑證。
10. 以 Fake LLM／Scripted Agent 驗證完整流程、費用上限與失敗恢復；真實模型效果、並行負載及正式主機隔離另外驗收，不能以 mock 通過替代。

## 13. 待確認事項與建議預設

| 事項 | 建議預設／後續決定 |
|---|---|
| 第一批協作規模 | 指定使用者邀請，先以小團隊驗證；不預設全公司都能讀取專案 |
| 排程 | 第一版由人安排，Agent 只能領已批准且無阻擋的 Task；自動派工放 M4／M5 |
| QA／整合 | QA 通過＋人工審閱整合，禁止開發 Agent 自批；是否允許自動合併需 Owner 明確設定 |
| 同人多 Agent 隔離 | 專用協作 sandbox 為建議方向；容器模型、CPU／記憶體／並行數待 M0 spike 與 ADR |
| Git 來源 | 先平台管理 Git，再接公司 GitHub／GitLab；確認公司既有平台與可用 runner |
| 模型分工與預算 | SA、Developer、QA 可分別選模型；依成員／專案限額，不預先指定付費模型 |
| 文件與程式碼保存 | 預設封存與保留版本；租約失效不刪成果，工作副本清理期限另定 |
| 與現有功能關係 | 私人專案不自動公開；RAG、OneDrive、網站發布均按其獨立計畫與權限銜接 |

上述建議不代表已批准架構變更。後續執行先從 M0 開始，實作期間依垂直切片更新 SA／ADR、測試與專案開發看板。
