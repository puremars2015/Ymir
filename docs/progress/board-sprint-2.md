# Sprint 2 · 正式認證

[← 回看板列表](README.md) ・ 規則與格式見 [README](README.md#留言規則)

---

## 📌 置頂：狀態總覽

> 最後更新：2026-10-07 21:30 ・ 狀態：**✅ 已結束（實際涵蓋路線圖 Sprint 2～5），後續移到 [Sprint 6](board-sprint-6.md)**

**目標**：以企業帳號登入（OIDC / Entra ID，經由 BFF，ADR-0002），完成 Admin / User 權限與帳號停用流程。

**Done Definition**：使用者以企業帳號登入、瀏覽器不持有任何 token；Admin 可以停用帳號，被停用的帳號立即無法使用 API 與建立 execution（SA 驗收條件 #1）。

| 工作項目 | 狀態 | 備註 |
|---|---|---|
| 對話附加檔案 / 圖片 / 影片給 Agent（使用者回報） | ✅ | 見 [#037](#037--對話可以附加檔案圖片影片給-agent)；整合測試與 e2e 通過，真實視覺模型**待使用者環境確認** |
| RAG 知識庫後續計畫 | 📝 | 共用 Embedding、每專案 SQLite、可選外部向量服務；僅記錄，尚未實作；見 [#035](#035--rag-知識庫後續計畫) |
| 前端網站託管、發布與指定使用者分享計畫 | 📝 | 已記錄需求與開發階段，尚未實作；見 [#032](#032--前端網站託管發布與分享後續計畫) |
| 對話黑字與淺藍漸層泡泡 | ✅ | 見 [#027](#027--對話文字改為黑色)；build 與版面預覽通過 |
| 對話藍色漸層與白色閱讀底面 | ✅ | 見 [#026](#026--對話藍色漸層與白底)；lint/build 與版面預覽通過 |
| Web-Pro favicon 與 Apple touch icon | ✅ | 見 [#024](#024--web-pro-favicon)；正式版 build 通過 |
| Web-Pro 品牌配色（登入、側欄、對話、表單與管理頁） | ✅ | 見 [#023](#023--web-pro-品牌配色)；lint/build 與登入預覽通過，完整驗證由 PR CI 執行 |
| OIDC 登入（Authorization Code + PKCE，後端換 token、只發 cookie） | ✅ | Entra ID（已確認、已註冊）；見 [#017](#017--企業帳號entra-id與本機帳號密碼登入使用者管理) |
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
| 對話隱藏模型思考內容（即時回覆與歷史） | ✅ | 見 [#015](#015--對話隱藏模型思考內容)；過濾 think / thinking 區段與未完成的串流標籤 |
| 使用者 OneDrive Workspace（Microsoft Graph） | ⏳ | 方法二已選定；目前只記錄計畫，待使用者指示實作；見 [#016](#016--記錄-onedrive-workspace-後續計畫暫不實作) 與 [計畫](../planning/onedrive-workspace-plan.md) |
| API 放進容器 + 主機 runtime host（ADR-0008） | ✅ | 見 [#013](#013--api-放進容器agent-runtime-改由主機上的-runtime-host-管理)；沙箱以 Docker 驗證完整流程 |
| API 容器的 engine：Linux 用 rootful Podman（Quadlet）、Windows 用 Docker Desktop | ✅ | 使用者決定；見 [#014](#014--api-容器linux-用-rootful-podmanwindows-用-docker-desktop) |
| 在 Linux 主機安裝 runtime host（rootless Podman、systemd、`ymir-runtime` group）並以 Quadlet 啟動 API 容器 | ⏳ | 依 [deploy/runtime-host](../../deploy/runtime-host/README.md)、[deploy/api](../../deploy/api/README.md)；**待使用者環境確認** |
| Windows：runtime host + Docker Desktop 跑 API 容器 | ⏳ | 依 [deploy/api 的 Windows 一節](../../deploy/api/README.md#windowsdocker-desktop開發--驗證)；**待使用者環境確認** |
| 本機帳號密碼登入（使用者追加需求） | ✅ | Admin 建立、第一次登入強制改密碼、鎖定、rate limit |
| 對話 `/make` 指令：主題按鈕（小工具架設、網站系統架設）、`/make 描述` 由 Agent 判斷主題 | ✅ | 見 [#019](#019--對話-make-指令與-make-主題管理)；真實模型是否照指示先問需求**待使用者環境確認** |
| Admin「Make 主題」管理頁（新增、編輯、排序、停用、刪除） | ✅ | 見 [#019](#019--對話-make-指令與-make-主題管理) |
| Agent 回覆以 Markdown 排版（程式碼區塊可複製） | ✅ | 見 [#020](#020--agent-回覆以-markdown-排版) |
| 管理介面：總覽儀表板、停止執行環境、稽核紀錄（ADR-0010） | ✅ | 見 [#021](#021--管理介面總覽與稽核紀錄adr-0010) |
| 系統設定：Entra ID（網頁設定、secret 加密存 DB、不重啟生效） | ✅ | 見 [#022](#022--系統設定entra-id-可在管理介面設定不重啟生效)；真實 Entra **待使用者環境確認** |
| AI 產生的檔案可下載（單檔、zip，使用者回報） | ✅ | 見 [#025](#025--ai-產生的檔案可以下載使用者回報) |
| 系統設定：Cloudflare Tunnel（token 交給 runtime host、網域可改） | ✅ | 見 [#028](#028--系統設定cloudflare-tunnel-token-與對外網域)；真實 Cloudflare / systemd **待使用者環境確認** |
| 對話體驗（Sprint 3）：改名 / 刪除（封存）、執行中重新整理可接回串流、檔案預覽、複製回覆 | ✅ | 見 [#029](#029--對話體驗改名刪除接回執行中的串流檔案預覽) |
| Runtime 生命週期（Sprint 4）：閒置自動停止、啟動時對帳、每人配額、執行政策可在管理介面修改、用量頁（ADR-0011） | ✅ | 見 [#030](#030--runtime-生命週期閒置停止對帳配額與用量)；正式 Podman / runtime host 上的閒置停止**待使用者環境確認** |
| LiteLLM 用量與每人每月預算（費用、token、預算由 LiteLLM 強制） | ✅ | 見 [#031](#031--接上-litellm模型用量與每人每月預算)；真正的 LiteLLM 回應格式**未驗證、待使用者環境確認**（步驟見 deploy/litellm/README.md） |
| Sprint 5 驗收計畫（交給 Codex 執行） | 📝 | 計畫見 [sprint5-acceptance-plan.md](../planning/sprint5-acceptance-plan.md)，見 [#033](#033--sprint-5驗收計畫交給-codex與強化提案) |
| Sprint 5 強化（Claude）：稽核補齊、監控指標、安全標頭、健康檢查、保存期限與備份 | ✅ | 使用者選 1、2、3、6、7，見 [#034](#034--sprint-5-強化稽核監控安全標頭健康檢查保存期限)；其餘（rate limit、CI 掃描、對外網路、壓測）待定 |
| Agent 擴充能力（管理員管制的使用者自建 skill / MCP；開發人員維護的平台 MCP：EIP / MES / embedding） | 📝 | 只寫了 ADR 草案，尚未實作；見 [#036](#036--agent-擴充能力adr-0012-草案) 與 [ADR-0012](../adr/0012-agent-extensions-and-platform-mcp.md)，等使用者回覆待決定事項 |
| 使用者以 Entra 實際登入驗證 | 🚧 | redirect URI 已加入（使用者確認，見 [#018](#018--合併-main-的看板衝突redirect-uri-已加入)）；待填 client secret、指派使用者並依 [entra-id.md 第 5 節](../guides/entra-id.md#5-驗證清單)登入測試；**待使用者環境確認** |
| 正式主機用完整 Containerfile 重跑 **Rootless Podman** 驗證 | ⏳ | 目前沒有 Linux 主機；可先在 WSL 2 Ubuntu 裝 Podman 驗證（見指南「效能建議」） |

**IdP 決定**：Entra ID（tenant `e333846a-0ee2-4e2d-a13b-efc97851b892`、client `5e85a804-c308-4402-b621-0be6d023a2ac`、網域 `ymir.thetainformation.com`），見 [ADR-0009](../adr/0009-entra-id-and-local-accounts.md)。

---

## 💬 留言區

### #038 · 對話可以附加檔案、圖片、影片給 Agent

> 👤 **Claude（AI）** · 🕒 2026-10-07 13:10 · `✅完成`

使用者回報：對話畫面沒辦法上傳檔案 / 圖片 / 影片給 Agent。規劃與設計決定寫在 [chat-attachments-plan.md](../planning/chat-attachments-plan.md)，摘要：

- **使用方式**：輸入框左邊 📎 選檔，也可以拖放或貼上截圖；只附檔不打字也能送出。首頁、專案頁「直接開聊」也能附加（送出時才上傳）。單檔 50 MB、每則 10 個。
- **存放**：檔案寫進使用者 runtime 工作目錄的 `uploads/`（專案對話共用專案目錄），會出現在檔案面板、可下載；Remote runtime host 一樣適用。新表 `vibemaker.message_attachments`（migration `MessageAttachments`）。
- **Agent 怎麼拿到**：後端把附件路徑附加在送給 Agent 的內容（對話紀錄只顯示使用者文字），Agent 用工具讀取或處理（影片、PDF 等也一樣）。
- **圖片直接給模型**：模型設定 `VibeMaker__Models__N__SupportsImages=true` 時，PNG / JPEG / GIF / WebP 以 Pi RPC `images` 一併送出（Pi 會自動縮圖）；模型選單顯示「（可看圖片）」。**預設是 false**，要開請確認該模型（經 LiteLLM）支援視覺輸入。
- **安全**：上傳端點只接受自己的對話（加入授權矩陣）；附件只能綁自己、同對話、未送出的；檔名清理、路徑由伺服器產生；runtime 內以 `realpath` 拒絕 `uploads` 被換成指向外面的 symlink；圖片以檔頭判斷類型；縮圖只用 blob URL 的 `<img>`。

**驗證**（實際跑過）：
- `dotnet build Ymir.slnx`（0 警告）、`dotnet test --project tests/Ymir.UnitTests`（321 通過）、`dotnet test --project tests/Ymir.IntegrationTests`（197 通過，含真實 Pi 1.0.0 + Fake LLM：支援視覺的模型收到 1 張圖、不支援的不送；新增附件測試 10 個）。
- `cd web && npm run lint && npm test -- --watch=false && npm run build`（111 個測試通過）。
- 新增 e2e `npm run e2e:attach`（API 以 Pi harness、宣告模型可看圖）：首頁附加圖片開聊 → 模型收到圖片；對話中只附圖片 + 影片送出；重新整理後縮圖仍在、檔案面板列出 3 個上傳檔。
- 過程中發現並修正：Guid v7 前 8 碼是時間戳，同一分鐘內同名檔案會覆蓋 → 改用 id 末 8 碼；剛上傳的附件不再被列為「這次 Agent 產生的檔案」。

**未驗證、待使用者環境確認**：真實視覺模型（MiniMax 等）經 LiteLLM 收圖片的格式與效果；Cloudflare Tunnel 下上傳 50 MB 的實際表現。

**後續（未做）**：每人工作目錄容量配額、影片抽影格 / 音訊轉文字 / PDF 轉文字工具、上傳進度條。

---

### #037 · Sprint 2 看板結束，移到 Sprint 6

> 👤 **Claude（AI）** · 🕒 2026-10-07 21:30 · `📢公告`

- 這個看板從正式認證開始，實際做完了路線圖的 Sprint 2～5：
  - 企業帳號與本機帳號登入、管理介面；
  - 對話體驗；
  - runtime 生命週期；
  - LiteLLM 用量與預算；
  - 驗收計畫與強化。
- 使用者要求建立新的 Sprint，所以本看板到此結束。新工作在 [Sprint 6 看板](board-sprint-6.md)：ADR-0012 擴充能力與 MCP Gateway、RAG 知識庫、前端網站託管。
- **移交給 Sprint 6 追蹤、仍待使用者環境確認的項目**：
  - 真 Entra 登入（#018、#022）；
  - 真 LiteLLM + MiniMax 與 `config.yaml` 單價（#031）；
  - Linux rootless Podman + runtime host + Quadlet（#013、#014、#030）；
  - Cloudflare Tunnel（#028）；
  - Windows Docker Desktop（#003）；
  - OTLP 指標匯出（#034）；
  - 驗收計畫由 Codex 執行（#033）。
- ADR-0012 依使用者決定改為「已採納（egress 待 spike）」：全域 + 每人覆寫、`skills` 與 `mcp`、Gateway 獨立服務、egress 先 spike。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #036 · Agent 擴充能力：ADR-0012 草案

> 👤 **Claude（AI）** · 🕒 2026-10-07 20:30 · `❓待決定`

使用者問「使用者能不能透過 Agent 自己新增 connector / addon / skill」。查證結果：**目前不能**（Ymir 啟動 Pi 時沒有載入任何擴充設定；SA §3 排除 MCP、SA §22 列為後續）。使用者接著提出兩個目標，已寫成 [ADR-0012](../adr/0012-agent-extensions-and-platform-mcp.md)（狀態：提議中，**只有文件，沒有改任何程式**）：

1. **管理員管制、成員自建**：預設全部關閉；管理員設全域預設與每人覆寫；在啟動 Pi 時由伺服器端強制（`--no-skills` 等），不靠前端隱藏。第一階段做 `skills` 與 `mcp`，可執行的 `extensions` 暫不開放。
2. **平台 MCP 只由開發人員建置**：服務目錄放版控（`deploy/mcp/servers.json`），管理員只能開關與授權、不能新增服務；Agent 只能經 MCP Gateway 呼叫，拿每人專屬的短期 token，Agent container 內沒有任何平台憑證。

查證過程發現：Pi 的使用者層目錄 `/agent-state/pi-agent` 本來就是 Agent 可寫的，所以今天 Agent 已經可能自己放 skill 或 `mcp.json`，只是沒有政策也沒有稽核；ADR-0012 落地才會有控制。

**驗證**：只讀了 Pi 1.0.0 隨附文件與現有程式碼，沒有實際跑 Pi 載入 skill / MCP。ADR 裡列了「待驗證項目」，第一步要用 spike 實測（尤其是 Pi 能否不讀使用者層 `mcp.json`、平台設定能否放在 Agent 不可寫的位置）。

**❓待決定**（詳見 ADR-0012 最後一節）：

1. 權限粒度：全域預設 + 每人覆寫就夠，還是也要角色 / 群組？
2. `mcp` 能力第一階段就開放，還是先只開 `skills`？
3. Gateway 與 API 同程序，或獨立服務？（建議獨立）
4. Agent container 的 egress 要不要先收斂？（開發計畫 §8）

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #035 · RAG 知識庫後續計畫

> 👤 **Codex（AI）** · 🕒 2026-10-07 11:09 · `📢公告`

- **已確認**：使用者提供文件後建立向量索引並進行文件問答；Embedding 使用共用獨立容器；同一使用者可有多個專案，每個專案預設一份 SQLite 向量資料庫，同專案不同任務沿用；必要時選擇獲授權的外部共用向量服務。
- **完成**：新增 [RAG 知識庫後續開發計畫](../planning/rag-knowledge-base-plan.md)，並加入主開發計畫。記錄匯入／切段／索引、引用問答、文件版本更新與移除、儲存 provider 抽象、隔離與持久保存、服務邊界、開發階段及驗收。
- **驗證**：PowerShell 檢查規劃文件的相對 Markdown 檔案連結存在；`git diff --check` 通過。依先前查閱的 TEI、sqlite-vec 與 SQLite WAL 官方資料記錄技術候選及限制。僅文件變更，未跑應用程式 build／test，未安裝模型或驗證實際 RAG 效能。
- **待確認**：Embedding 模型與硬體、第一個外部 provider、文件格式／容量及保存政策、外部回答模型的資料傳輸政策；知識庫分享尚未確認，首版預設私人，不繼承網站分享權限。
- **範圍**：尚未實作；未變更程式、資料庫、容器或部署。實作前需補新 ADR。

---

### #034 · Sprint 5 強化：稽核、監控、安全標頭、健康檢查、保存期限

> 👤 **Claude（AI）** · 🕒 2026-10-07 11:30 · `✅完成`

- **背景**：使用者從 #033 的提案選了 1、2、3、6、7。
- **做法**：
  1. **runtime lifecycle 稽核**（SA §12、AC-12）：
     - EnsureRuntime 回報這次是新建立還是從停止啟動（`RuntimeInfo.Transition`；Podman / Docker / Local / runtime host 協定都帶上，舊版 runtime host 視為 None）；
     - 稽核新增 `runtime.create`、`runtime.start`、`runtime.ensure`（失敗）、`runtime.stop`（帳號停用）、`runtime.reconcile`；
     - runtime 原本就在執行時不寫，避免每次 execution 都產生一筆。
  2. **監控指標與追查**（SA §18、AC-10）：
     - meter `Ymir.VibeMaker`：`ymir.runtimes.active` / `busy`、`ymir.executions.started` / `finished`（status、error_code）、`ymir.execution.duration`、`ymir.runtime.start_failures`，經 OpenTelemetry 匯出（設定 `OTEL_EXPORTER_OTLP_ENDPOINT`）；
     - 每個 execution 有自己的 trace，log scope 帶 ExecutionId / ConversationId / UserId，稽核的 correlation id 就是這個 trace id；
     - console log 現在會印出 scope。原本的 appsettings 設定沒有生效，改在 ServiceDefaults 用程式設定。
  3. **安全標頭**：所有回應都加上下列標頭；下載檔案的 `CSP: sandbox` 不受影響。
     - CSP：script 只允許同源，所以 Angular build 關閉 `inlineCritical`，避免產生 inline script；style 允許 inline；`frame-ancestors 'none'`；
     - `X-Frame-Options: DENY`（覆蓋 antiforgery 的 SAMEORIGIN）、`Referrer-Policy`、`Permissions-Policy`、COOP。
  6. **健康檢查**：
     - `/health` 包含資料庫、執行環境（runtime host `/health` 或 container CLI）、LiteLLM（`/health/liveliness`）；
     - `GET /api/admin/health` 回傳各項狀態與摘要；
     - 管理總覽新增「服務狀態」卡片，服務異常時最上方顯示警告。
  7. **保存期限與備份**：
     - `DataRetentionWorker` 每天刪除過期的稽核紀錄（預設 365 天）與已結束 execution 的事件（預設 90 天），並寫入稽核 `system.retention.purge`；
     - 對話、訊息、檔案永久保留；
     - 新增 [backup-restore.md](../guides/backup-restore.md)：資料庫與 Data Protection 金鑰必須一起備份，另有 workspace、LiteLLM、排程、還原步驟與驗證清單。
  - 文件：deploy/api README 新增「維運」、`.env` 範本、CLAUDE.md；驗收計畫的 AC-12 改為「已補上」。
- **驗證（實際跑過）**：
  - 後端：`dotnet test --solution Ymir.slnx` **476 項全部通過**，`dotnet format` 無差異。新增：
    - runtime 稽核（create / reconcile）、runtime host 回報 Transition；
    - 指標（MeterListener 收到 started / finished / duration / busy）；
    - 健康檢查（全部正常；LiteLLM 無法連線時 `/health` 回 503，摘要不含內部位址）；
    - 安全標頭（3 個端點）；
    - 保存期限（過期的被刪除，訊息與期限內的稽核保留）；
    - 授權矩陣、OpenAPI 已更新。
  - 前端：`npm run lint`、`npm test`（**104 項**）、`npm run build` 都通過。
  - **CSP 實測**：由 API 直接提供正式 build，用 Chromium 登入並收到 Markdown 回覆，沒有 CSP 違規。唯一的 console 錯誤是未登入時 `/api/me` 回 401，屬於預期。
  - 端對端：`e2e:admin` 15 步、`e2e` 19 步、`e2e:make` 10 步全部通過；總覽截圖顯示三項服務正常。
  - API log 確認每個 execution 都帶自己的 TraceId 與 ExecutionId。
- **卡關 / 待決定**：
  - OTLP collector（例如 Grafana / Prometheus）要在使用者環境架設，**指標匯出待使用者環境確認**。
  - 稽核保存天數請依公司政策調整（`Ymir__Retention__AuditLogDays`）。
  - 尚未做的強化項目：一般 API rate limit 與 SSE 連線上限、CI 安全掃描、Agent 對外網路限制（需要 ADR）、壓測。

### #033 · Sprint 5：驗收計畫（交給 Codex）與強化提案

> 👤 **Claude（AI）** · 🕒 2026-10-07 10:20 · `📝規劃`

- **背景**：使用者決定「驗收」只寫計畫，交給 Codex 執行；「強化」由 Claude 提案。
- **驗收計畫**：[docs/planning/sprint5-acceptance-plan.md](../planning/sprint5-acceptance-plan.md)
  - 範圍是 SA §21 的 12 項條件，加上開發規劃新增的 13～16 項，每項都列出現有涵蓋、要補的測試與驗證方式；
  - 產出三樣：`e2e:acceptance`、`tests/Ymir.IntegrationTests/Acceptance/`、驗收報告；
  - Podman 相關項目（AC-06 / 07 / 08 / 14 / 16）在 GitHub Actions 的 Ubuntu runner 跑；
  - 規則：只補測試、不改產品行為；遇到缺口就標 `Skip` 並在看板回報。
- **盤點時發現的缺口**（由強化處理）：
  - **AC-12**：runtime 的建立 / 啟動（`EnsureRuntime`）沒有寫稽核，SA §12 要求 runtime create / start / stop / delete 都要記錄。
  - **SA §18 監控指標**：`ServiceDefaults` 已訂閱 `Ymir.*` meter，但程式裡沒有任何 meter。缺少的指標：active / busy runtimes、execution duration、execution failures、runtime start failures。
  - **log 追蹤**：execution 的 log 沒有 `execution_id` scope，從錯誤追到同一次執行的所有 log 不方便（AC-10）。
- **強化提案**（待使用者確認）：
  1. 稽核補齊 runtime lifecycle（AC-12）；
  2. SA §18 監控指標 + log scope（AC-10）；
  3. HTTP 安全標頭：CSP、frame-ancestors、Referrer-Policy、Permissions-Policy；
  4. 一般 API 的 rate limit 與每人 SSE 連線數上限；
  5. CI 安全掃描：NuGet / npm 弱點、container image（Trivy）、secret 掃描；
  6. 健康檢查：readiness 包含 DB、runtime host、LiteLLM，管理總覽顯示服務狀態；
  7. 資料保存：稽核紀錄保存期限與清理、備份指南；
  8. Agent container 的對外網路限制（開發規劃 §8 尚未決定，需要新 ADR）；
  9. 小規模壓測（例如 20 位同時使用者），找出 SSE 與 runtime 的上限。
- **驗證**：只有文件，沒有程式變更。

---

### #032 · 前端網站託管、發布與分享後續計畫

> 👤 **Codex（AI）** · 🕒 2026-10-07 09:41 · `📢公告`

- **需求**：使用者建立前端網站後可發布成網站網址，提供公開、公司內部及指定使用者三種存取方式；發布後可以管理分享名單。
- **完成**：新增 [前端網站託管、發布與分享開發計畫](../planning/frontend-site-hosting-plan.md)，並於主開發計畫加入入口。記錄獨立 Nginx 託管、發布產物快照、穩定網址、版本切換與撤權、分享限瀏覽、接收者入口、身分隔離及分階段驗收。
- **驗證**：已對照 `/make`、檔案端點、容器目錄／掛載及相關 ADR；PowerShell 檢查兩份規劃文件的 Markdown 檔案連結存在，`git diff --check` 通過。僅文件變更，未跑應用程式 build／test，網站發布與私人網站登入尚未實作或驗證。
- **待確認**：實際網站網域、公司帳號範圍、容量／數量／版本保留限制及來源封存政策；實作前需新增 ADR。未變更程式、資料庫、部署或 Tunnel。

---
### #031 · 接上 LiteLLM：模型用量與每人每月預算

> 👤 **Claude（AI）** · 🕒 2026-10-07 09:40 · `✅完成`

- **背景**：使用者要求先把 LiteLLM 接進來，並選擇「用量 + 每人預算」，單價填在 LiteLLM 設定檔。
- **原本的問題**：
  - 發 virtual key 時沒有設定 LiteLLM 的 `user_id`，LiteLLM 無法依使用者彙總花費。
  - 預算設在每把 key 上，key 每 24 小時換發一次，等於每天重置。
- **做法**：
  - **key 掛在使用者底下**：發 key 時帶 `user_id`（= Ymir 使用者 id）。發 key 前先 `/user/update`（不存在時改 `/user/new`）建立 LiteLLM 使用者，並套用每月預算（`max_budget` + `budget_duration: 30d`）。
  - **每人每月預算**：執行政策新增「每人每月模型預算（US$）」，部署預設為 `VibeMaker__LiteLlm__MonthlyBudgetUsd`。
    - 管理介面改預算時，立即套用到所有用過的使用者；
    - 由 LiteLLM 強制；
    - Ymir 送訊息前也會先檢查（每人快取 60 秒，LiteLLM 無法連線時不擋），用完回 429「本月模型預算已用完（US$X，將於 MM/DD 重置）」；
    - 執行中才用完時，Agent 以「本月模型預算已用完」的摘要結束（不含原始錯誤）。
  - **用量頁**：每位使用者的模型費用、輸入 / 輸出 token、本期已用 / 預算（接近或用完時標示），另有合計卡片。
    - 資料來自 LiteLLM 的 `/user/daily/activity` 與 `/user/info`，Ymir 不自己保存；
    - 沒有連接 LiteLLM（開發環境）時顯示提示、隱藏這些欄位。
  - **單價**：`deploy/litellm/config.yaml` 的 `minimax` 加上 `input_cost_per_token` / `output_cost_per_token` 佔位值（0）。**請依 MiniMax 方案填寫**；沒填時費用為 0、預算不會用完（token 數仍正確）。
  - **Fake LLM**：模擬 LiteLLM 的使用者、預算、花費與 daily activity，每次呼叫算 10 + 5 個 token、US$0.01。
  - **文件**：
    - deploy/litellm README 說明預算與用量；
    - `smoke-test.sh usage <使用者 id>` 可查詢預算與當天的用量；
    - ADR-0004 補充、ADR-0011 新增一列；
    - CLAUDE.md 已更新。
- **驗證（實際跑過）**：
  - 後端：`dotnet test --solution Ymir.slnx` **469 項全部通過**，`dotnet format` 無差異。新增：
    - gateway 單元測試：key 帶 `user_id`、upsert（update 失敗才 new，401 / 5xx 不 new）、預算 0 送 null、`/user/info` 與分頁的 daily activity 解析、單一使用者失敗不影響其他人；
    - 預算檢查的快取與 LiteLLM 無法連線時不擋；
    - 預算驗證；
    - 整合測試（真 Pi + Fake LiteLLM）：預算套用到 LiteLLM 使用者、用量 API 回傳費用與 token、用完後被擋下或以預算摘要結束、調高預算立即套用；
    - OpenAPI 快照已更新。
  - 前端：`npm run lint`、`npm test`（**102 項**）、`npm run build` 都通過。
  - 端對端（Fake LLM 以 `FAKE_LLM_MASTER_KEY` 模擬 LiteLLM）：
    - `e2e:admin` 擴充到 **15 步全部通過**：預算欄位驗證、設為 US$0.02 後已用過的使用者看到「本月模型預算已用完」、用量頁顯示費用、token 與已達上限；
    - `e2e`（19 步）、`e2e:make` 重跑通過。
- **卡關 / 待決定**：
  - 沙箱拉不到 LiteLLM image，**真正 LiteLLM v1.103.2 的 `/user/update`、`/user/new`、`/user/info`、`/user/daily/activity` 只依官方文件格式模擬，未驗證、待使用者環境確認**。步驟見 deploy/litellm/README.md 的「手動驗證」。
  - 這次上線前發出的 key 沒有 `user_id`，它們的花費不會算到使用者身上；最晚 24 小時後換發就正常。
  - MiniMax 的單價需要使用者依方案填入 `config.yaml`。

### #030 · Runtime 生命週期：閒置停止、對帳、配額與用量

> 👤 **Claude（AI）** · 🕒 2026-10-07 01:45 · `✅完成`

- **背景**：Sprint 3 完成後，依使用者指示接著做下一階段，也就是路線圖的 Sprint 4「Runtime 完整生命週期」。
- **先修的 bug**：runtime id 只存在記憶體。
  - API 或 runtime host 重新啟動後，資料庫紀錄的 runtime id 就與 manager 不同。
  - 影響：Admin「停止執行環境」與停用帳號時的停止都會失敗（找不到 runtime）。
  - 修正：生命週期操作改為以 user id 進行（新增 `StopForUserAsync`、`GetStatusForUserAsync`）。
  - runtime host 的 `GET /runtime` 與 `POST /stop` 改為直接以 user id 操作 container，不依賴記憶體中的對照表；端點清單不變。
- **做法**（[ADR-0011](../adr/0011-runtime-lifecycle-policy.md)）：
  - **啟動時對帳**：`RuntimeLifecycleWorker` 啟動時，比對每筆 runtime 紀錄與實際狀態，以實際狀態為準。
  - **閒置停止**：
    - 每分鐘檢查一次，停止超過閒置時間的 runtime；
    - execution 開始與結束都會更新最後活動時間；
    - 正在執行或有排隊工作的使用者會跳過（非阻塞取得使用者 lock）；
    - 寫稽核 `runtime.idle_stop`；
    - 檔案保留，下次送訊息時自動啟動。
  - **執行政策**：閒置停止時間、單次執行上限、每人同時排隊的工作數、每人每日執行次數。
    - 部署設定 `VibeMaker__Runtime__*` 是預設值；
    - 管理介面的設定存在 `system_settings`，優先於部署設定、**不必重啟**。
  - **配額**：送訊息時檢查，超過回 429 `QUOTA_EXCEEDED`，聊天頁直接顯示原因。這是軟性上限；「同一對話單一執行中」仍由資料庫唯一索引保證。
  - **管理介面**：
    - 系統設定新增「執行環境」卡片；
    - 新增「用量」分頁：各使用者的執行數、成功率、失敗 / 取消數、Agent 執行時間、24 小時次數（接近或達到每日上限時標示）、執行環境狀態，期間可選 1、7、30、90 天。
  - CLAUDE.md、ADR 索引已更新。
- **驗證（實際跑過）**：
  - 後端：`dotnet test --solution Ymir.slnx` **452 項全部通過**，`dotnet format` 無差異。新增：
    - 執行政策：預設值、存檔、驗證、還原、稽核；
    - 每日上限回 429；
    - 閒置停止：停止、寫稽核，再送訊息會自動啟動；
    - 執行中的使用者不會被停止；
    - 對帳；
    - 用量；
    - runtime host 測試：**新的 API instance（不認得任何 runtime id）仍能以 user id 查詢與停止**；
    - 政策驗證與解析的單元測試；
    - 授權矩陣加入 4 個端點；OpenAPI 快照已更新。
  - 前端：`npm run lint`、`npm test`（**100 項**）、`npm run build` 都通過。
  - 端對端：
    - `e2e:admin` 擴充到 **14 步全部通過**：設定執行政策（含欄位驗證）、每日上限設為 1 後一般使用者再送出被擋下並看到原因、用量頁標示已達上限、還原政策。
    - `e2e`（19 步）、`e2e:make` 重跑通過。
  - 沙箱只有 Local runtime；Podman / Docker 與正式 runtime host 上的閒置停止與對帳**未驗證、待使用者環境確認**。Podman 的 inspect / stop 指令沿用原本經過測試的 `ContainerCommandBuilder`。
- **卡關 / 待決定**：
  - 模型 token 用量與費用在 LiteLLM（ADR-0004），目前用量頁只有執行次數與時間。需要的話，下一步可以把 LiteLLM 的 spend 接進用量頁。
  - 閒置停止與使用者 lock 都是單一 API instance 的記憶體 lock；之後要多 instance 時需要改成分散式 lock。
  - 路線圖下一步是 Sprint 5「驗收與強化」：OTel 指標、安全檢查、Playwright 覆蓋 SA 的 12 項驗收條件。

### #029 · 對話體驗：改名、刪除、接回執行中的串流、檔案預覽

> 👤 **Claude（AI）** · 🕒 2026-10-07 01:15 · `✅完成`

- **背景**：使用者選擇接著做 Sprint 3「對話體驗」。原本對話不能改名或刪除、專案不能刪除；重新整理頁面時執行中的 Agent 看起來像卡住；檔案只能下載不能先看。
- **做法**：
  - **刪除＝封存**：依資料保存原則（Conversation / Workspace 永久保留，可封存）。
    - 狀態改為 `Status = Archived`，資料與 runtime 內的檔案都保留。
    - 封存後所有端點回 404，送訊息也會被拒絕。
    - 執行中的對話不能封存，回 409 `EXECUTION_CONFLICT`。
    - 封存專案時，專案內的對話一起封存。
  - **新端點**：`PATCH /api/conversations/{id}`（改名）、`DELETE /api/conversations/{id}`、`DELETE /api/projects/{id}`。
    - 都驗證擁有者並加上 antiforgery；
    - 稽核動作：`conversation.update`、`conversation.archive`、`project.archive`；
    - 已加入授權矩陣（DELETE 放在最後執行，先等 execution 結束）。
  - **接回串流**：
    - `ConversationResponse.ActiveExecutionId` 是執行中（Queued / Running）的 execution id。
    - 前端載入對話時若有這個值，就訂閱該 execution 的 SSE（事件從頭重播）。最後一則使用者訊息改由 live turn 顯示，不會重複；判斷邏輯寫成純函式 `resumeTurnFrom`。停止按鈕可以使用。
    - 接回的這一輪不知道執行前的檔案狀態，所以不顯示「這次產生的檔案」，避免把舊檔案誤判成新的（e2e 發現後修正）。
  - **側邊欄**：每個對話與專案都有「⋯」選單。
    - 重新命名：inline 編輯，Enter 存檔、Esc 取消；
    - 刪除：確認後封存；刪除專案時會說明「N 個對話一起移除，檔案保留」；
    - 刪除目前開啟的項目時，導回首頁或該專案頁；
    - 先更新畫面再呼叫 API，失敗時還原。
  - **對話頁**：
    - 標題點兩下可以改名；
    - Agent 回覆加上「複製」按鈕，複製的是 Markdown 原文。
  - **檔案預覽**：點檔名開啟預覽，下載按鈕另外放；類型判斷寫成純函式 `previewKind`。
    - 文字與程式碼 ≤ 512 KB：以文字綁定放在 `<pre>`，HTML 只顯示原始碼、不執行；
    - Markdown：經既有的 sanitizer 排版；
    - 圖片 ≤ 10 MB：fetch 成 blob 後放進 `<img>`，SVG 的腳本不會執行；關閉預覽時 revoke URL；
    - 其他類型或檔案太大：顯示「無法預覽，請下載」。
  - **樣式修正**：全域 `button:hover` 的權重太高，透明的圖示按鈕滑過時會變成深藍方塊。改用 `:not(a, b, c)` 降低權重。
  - CLAUDE.md 補上封存、接回串流與預覽的規則。
- **驗證（實際跑過）**：
  - 後端：`dotnet test --solution Ymir.slnx` **429 項全部通過**，`dotnet format` 無差異。新增 `ConversationManagementTests`：
    - 改名、封存後回 404；
    - 專案連同對話一起封存；
    - 執行中回 409、`activeExecutionId` 執行中有值、結束後為 null；
    - 別人的資源回 404。
  - 前端：`npm run lint`、`npm test`（**93 項**）、`npm run build` 都通過。新增 `resumeTurnFrom`、`previewKind`、側邊欄樂觀更新的純函式測試。
  - 端對端：
    - `e2e`（chat-flow）擴充到 **19 步全部通過**：預覽 `hello.txt`、`[slow]` 執行中重新整理後自動接回並停止（使用者訊息只出現一次）、複製整則回覆、標題改名、側邊欄改專案名稱、刪除目前開啟的對話（導回首頁）、刪除專案（重新整理後仍不見）。
    - `e2e:make`、`e2e:admin` 重跑通過。
- **卡關 / 待決定**：
  - 目前沒有「已封存」清單，也不能還原；需要的話可以在管理介面或個人設定加上。
  - 接下來進入下一階段：**執行環境生命週期**（閒置自動停止、啟動時對帳、每位使用者的配額、執行逾時設定、管理介面顯示用量）。

### #028 · 系統設定：Cloudflare Tunnel token 與對外網域

> 👤 **Claude（AI）** · 🕒 2026-10-06 21:45 · `✅完成`

- **做法**（ADR-0010 第 4 點）：
  - **runtime host**：
    - cloudflared 改成以 `ymir` 帳號執行的 rootless Podman Quadlet user service（新增 `deploy/cloudflared/ymir-cloudflared.container`）。
    - runtime host 本來就是 `ymir`，所以不需要 root：token 寫進 `~ymir/.config/ymir/cloudflared.env`（600），再以 `systemctl --user restart` 重啟。
    - 新設定 `RuntimeHost:Tunnel:Mode`，預設 `Disabled`。
    - 新端點：`GET /v1/edge/tunnel`、`PUT /v1/edge/tunnel-token`。
    - token 驗證：只允許 base64 字元、長度 100～4096。regex 用 `\z`，因為 .NET 的 `$` 會放行結尾換行，可能被用來注入 env 檔。
    - 寫檔方式：暫存檔建立時就是 600，再以 rename 取代原檔。
    - 以參數清單執行 `systemctl`，不經 shell。
    - 新增路由清單測試，固定 runtime host 只能有哪些端點。
  - **API**：
    - `ITunnelManagement`：Remote 部署時轉送給 runtime host；其他 provider 回傳「此部署不支援」。
    - token 不保存，也不出現在回應、稽核、log。
  - **對外網域**：
    - `Ymir.Edge` 的 Host 限制原本在啟動時固定在 `HostFilteringOptions`，改成每個請求讀目前值（`IPublicHostnameSource`），管理介面改網域後**不必重啟**。`/health` 的隱藏規則也跟著改。
    - 網域存在 `system_settings`，快取 30 秒，資料庫裡的不合法值會被忽略。
  - **端點**：`GET /api/admin/settings/tunnel`、`PUT .../tunnel/token`、`PUT .../tunnel/hostname`（空字串表示還原）。
  - **前端**：「系統設定」新增「對外連線」卡片。
    - 狀態燈號；
    - token 欄位只能寫入，也可以直接貼上 Cloudflare 提供的整行指令；
    - 對外網域可以設定與還原；
    - 改網域後提示要同步 Cloudflare 的 Public Hostname 與 Entra 的 redirect URI；
    - 總覽在 tunnel 未連線時顯示警告。
  - **文件**：
    - `deploy/runtime-host/README.md` 新增「Cloudflare Tunnel 由管理介面設定」；
    - `docs/guides/cloudflare-tunnel.md` 新增方式 C 與「對外網域」；
    - 設定範本加上 `RuntimeHost__Tunnel__Mode`；
    - CLAUDE.md 安全紅線補上 tunnel token 的規則。
- **驗證（實際跑過）**：
  - 後端：`dotnet test --solution Ymir.slnx` **425 項全部通過**。新增：
    - tunnel 單元測試 20 項：token 規則、unit 名稱、atomic 600 寫檔、Disabled 模式；
    - runtime host 整合測試 9 項：寫檔與重啟、非法 token 不動檔案、結尾換行被拒、重啟失敗回 502、需要 token、路由清單；
    - Edge 10 項：網域覆寫不重啟即生效、網域驗證；
    - API：開發環境不支援並回 409、網域設定與還原、**經真實 runtime host 轉送 token 且回應與稽核都不含 token**；
    - 授權矩陣加入 3 個端點；OpenAPI 快照已更新。
  - 前端：`npm run lint`、`npm test`（**82 項**）、`npm run build` 都通過。
  - 端對端：
    - `e2e:admin` **12 步全部通過**，新增 Tunnel 卡片：開發環境顯示「此部署不支援」、token 欄位停用、網域格式檢查、設定與還原；
    - `e2e`（14）、`e2e:make`（10）、`e2e:auth`（6）都通過。
- **未驗證、待使用者環境確認**：
  - 真實的 Cloudflare token；
  - `ymir` 帳號的 systemd user service 與 Quadlet；
  - 重啟後實際連線。
  - 沙箱沒有 systemd user session，以 fake 服務控制器替代驗證。
  - 在 Linux 主機上請依 `deploy/runtime-host/README.md` 新增的章節安裝。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #027 · 對話文字改為黑色

> 👤 **Codex（AI）** · 🕒 2026-10-06 17:46 · `✅完成`

- **需求**：對話區的文字改成黑色。
- **調整**：對話色票主文字改為 `#111111`，輔助文字改深灰；使用者泡泡改淺藍漸層並沿用黑字，保持白底閱讀風格。
- **驗證**：正式版 build 通過；以實際 SCSS 產生的靜態版面預覽確認黑字與淺藍泡泡。純配色變更不新增測試，完整測試由 PR CI 執行。
- **部署**：待 CI 全部通過後合併並更新網頁容器，API 與登入設定不變。
### #026 · 對話藍色漸層與白底

> 👤 **Codex（AI）** · 🕒 2026-10-06 17:40 · `✅完成`

- **需求**：對話區改成藍色漸層搭配白色底。
- **調整**：淺藍漸層標題列、深藍漸層使用者泡泡與白字、白色 AI 回覆卡片、藍色漸層輸入框邊框。
- **範圍**：新對話與對話閱讀區套用白底色票（含系統深色模式），管理頁保留原本色票；保留主分支最新的檔案下載功能。
- **驗證**：前端 lint、正式版 build 與 API publish 通過；用目前 SCSS 產生不需登入的靜態對話版面預覽，瀏覽器無 console error。尚未使用登入帳號驗證實際對話畫面，完整測試由 PR CI 執行。
- **部署**：CI 全部通過後合併並更新目前的網頁與 API，保留 OIDC / Cloudflare Tunnel 設定；不變更使用者資料。

---

### #025 · AI 產生的檔案可以下載（使用者回報）

> 👤 **Claude（AI）** · 🕒 2026-10-06 17:45 · `✅完成`

- **問題**：Agent 完成的檔案（例如 `calculator.html`）使用者無法取得。Cloudflare Tunnel 的 PR 先暫緩，優先處理這個。
- **做法**：
  - **讀檔方式**：經由既有的「在使用者 runtime 內執行程序」介面讀檔（新的 `IWorkspaceFileReader`）。Local、Podman、Docker、Remote（runtime host）都適用，API 容器仍然不掛載 workspace（ADR-0008），runtime host 協定沒有變更。
  - **安全**：
    - 列檔用 `find`，略過隱藏檔、`node_modules`、symlink。
    - 讀檔的路徑經 stdin 傳入（NUL 分隔），不組進命令列。
    - runtime 內以 `realpath` 確認路徑仍在工作目錄內，且本身不是 symlink、是一般檔案；否則一律 404。
    - API 端另外拒絕 `..`、絕對路徑、隱藏檔。
  - **打包下載**：一個程序就能串流多個檔案；zip 先寫暫存檔再送出。
  - **上限**：列出 1000 個、單檔 200 MB、zip 500 個檔案 / 200 MB。沒執行過 Agent 的對話不會為了列檔案而啟動 runtime。
  - **下載回應**：一律是附件（`application/octet-stream`、`nosniff`、CSP `sandbox`、`no-store`），Agent 產生的 HTML 不會在 Ymir 網域上執行（避免帶著 Ymir 的 cookie 跑腳本）。
  - **檔名**：同時提供 ASCII 的 `filename=` 與 `filename*=UTF-8''`，瀏覽器會顯示原本的中文檔名。
- **前端**：
  - 對話標題列新增「📁 檔案 (N)」，打開檔案面板：逐一下載，或「全部下載（.zip）」。專案內的對話共用同一組檔案。
  - Agent 每回合結束後，這一回合新增或修改的檔案會直接顯示在回覆下方，點一下就能下載。
- **驗證（實際跑過）**：
  - 後端：`dotnet test --solution Ymir.slnx` **383 項全部通過**。
    - 單元測試：路徑規則、`find` 輸出解析、BoundedReadStream。
    - 整合測試：
      - 列檔（隱藏檔與 node_modules 不出現）；
      - 二進位與中文檔名下載，以及 header 檢查；
      - zip；
      - 8 種不安全或不存在的路徑都回 404；
      - **symlink 逃逸**：指向工作目錄外的檔案或目錄都下載不到，也不會被打包；
      - 別人的對話回 404；
      - **經由 runtime host（Remote）** 列檔、下載、zip。
    - 授權矩陣加入 3 個端點。
  - 前端：`npm run lint`、`npm test`（**78 項**）、`npm run build` 都通過。
  - 端對端：
    - `npm run e2e` **14 步全部通過**。新增一步：回覆下方的檔案 chip 下載 `hello.txt`（內容正確）→ 檔案面板 → zip 下載（確認是 zip，Content-Disposition 正確）。
    - `e2e:make` 10 步通過。
- **注意**：測試用的 headless Chromium 無法處理中文下載檔名（會變成 "download"），所以 e2e 改為直接檢查 header。實際的 Chrome / Edge 會用 `filename*` 顯示中文檔名，**待使用者環境確認**。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #024 · Web-Pro favicon

> 👤 **Codex（AI）** · 🕒 2026-10-06 17:15 · `✅完成`

- **需求**：新配色完成後替換 favicon。
- **調整**：沿用官網 https://www.webpromaterials.com/images/favicon/favicon.ico 與 180px Apple touch icon；圖片保存在 web/public，由本站提供，不依賴外部載入。
- **快取**：icon URL 加 `webpro-20261006` 版本標記，theme-color 使用品牌藍 `#004EA0`。
- **驗證**：正式版 build 通過，確認原始圖片為 Web-Pro 藍色品牌圖示；純資產變更不新增測試。PR CI 全部通過後合併並更新網頁容器，再驗證公開 icon 與 HTML。

---

### #023 · Web-Pro 品牌配色

> 👤 **Codex（AI）** · 🕒 2026-10-06 17:04 · `✅完成`

- **需求**：參考 https://www.webpromaterials.com/ 的網站配色，統一 Ymir 外觀。
- **調整**：官網深藍 `#004EA0`、白色與淺藍色套用共用樣式；側欄選取、對話泡泡、表單與管理頁共用色彩；深色模式改為海軍藍與淺藍；補上焦點與滑過狀態。
- **登入頁**：淺藍背景、白色卡片、品牌藍標題與按鈕，保留現有登入功能。
- **驗證**：前端 lint、正式版 build 通過；本機 Docker 預覽確認登入畫面，瀏覽器無 console error。純樣式改動不新增測試；完整測試由 PR CI 執行。
- **部署**：沿用既有 Production API、Cloudflare Tunnel、OIDC 設定；同步主分支新加入的總覽、稽核、Entra 系統設定頁，保留 #021、#022 的進度。待 PR CI 全部通過後合併與更新網頁容器。

---

### #022 · 系統設定：Entra ID 可在管理介面設定（不重啟生效）

> 👤 **Claude（AI）** · 🕒 2026-10-06 17:15 · `🚧進度`

- **後端**：
  - 新增 `platform.system_settings` 與 `ISystemSettingsStore`。機密以 Data Protection 加密（purpose `Ymir.SystemSettings.v1`），解不開時退回部署設定並記錄 log。
  - `OidcSettingsProvider` 決定生效值：資料庫 > `.env`。
    - `oidc` scheme 依目前設定**動態加入 / 移除**，並清掉 `OpenIdConnectOptions` 快取，存檔後下一個登入請求就用新設定。
    - 多個實例時，其他實例 30 秒內同步。
    - 資料庫讀不到時退回部署設定。
  - Authority 由部署設定的 `Ymir:Auth:Oidc:AuthorityHost`（預設 `https://login.microsoftonline.com`）加上 Tenant GUID 組成，「測試設定」不會被拿來連任意網址。
  - 端點：`GET` / `PUT` / `DELETE /api/admin/settings/oidc`、`POST /api/admin/settings/oidc/test`。
    - secret 永遠不回傳。
    - 停用或還原時，若沒有啟用中的本機 Admin 會回 409 `LAST_LOGIN_METHOD`。
    - 稽核動作：`admin.settings.oidc.update` / `update_secret` / `reset`，不記錄值。
- **前端**：
  - 「系統設定」分頁：Entra 表單，secret 欄位只能寫入，並顯示重新導向 URI、測試設定、還原為部署設定。
  - 總覽警告：Entra 未設定、secret 30 天內到期、已過期。
- **文件**：
  - `docs/guides/entra-id.md` 新增「2A. 在管理介面設定（建議）」；
  - CLAUDE.md 安全紅線補上 secret 加密存放的規則。
- **驗證（實際跑過）**：
  - 後端：`dotnet test --solution Ymir.slnx` **350 項全部通過**。
    - 新增 7 組單元測試：輸入驗證、authority 組合、JSON、生效值、AuthorityHost 規則、角色對應跟著設定變動。
    - 新增 4 個整合測試（Fake OIDC）：
      1. 存檔後不重啟即可完成企業帳號登入；資料庫是密文；只改 Client ID 會沿用 secret 並立即生效；稽核不含 secret。
      2. 沒有本機 Admin 時不能停用。
      3. 輸入驗證，以及第一次必須提供 secret。
      4. 測試設定（正確 / 錯誤 / 非法 tenant）。
    - 授權矩陣加入 4 個端點。`dotnet format` 通過，OpenAPI 快照已更新。
  - 前端：`npm run lint`、`npm test`（**73 項**）、`npm run build` 都通過。
  - 端對端：
    - `npm run e2e:admin` **11 步全部通過**，新增：總覽提示未設定 → 填入 Fake OIDC tenant → 測試成功 → 儲存（secret 不再顯示）→ 總覽提示即將到期 → 新瀏覽器直接用企業帳號登入（沒有重啟）→ 還原後登入頁不再有企業帳號按鈕。
    - `e2e`（13）、`e2e:make`（10）通過。
    - `e2e:auth`（6）用 `.env` 方式設定 OIDC 也通過，確認原本的部署方式不受影響。
- **未驗證、待使用者環境確認**：實際連到 Microsoft Entra（沙箱連不到）。可以照 entra-id.md 的 2A 在網頁上填入貴公司的值測試。
- **下一步**：PR C，Cloudflare Tunnel。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #021 · 管理介面：總覽與稽核紀錄（ADR-0010）

> 👤 **Claude（AI）** · 🕒 2026-10-06 16:45 · `🚧進度`

- **範圍**（使用者確認）：
  - 管理總覽；
  - 稽核紀錄；
  - 系統設定：Entra ID；
  - 系統設定：Cloudflare Tunnel。
  - 分三個 PR 做，這是第一個。
- **ADR-0010**（[docs/adr/0010](../adr/0010-admin-editable-system-settings.md)）：
  - 系統設定存在 `platform.system_settings`，生效值為「資料庫 > `.env`」。
  - Entra secret 以 Data Protection 加密，只能寫入、不回顯。Authority 由 Tenant ID 組成，避免 SSRF。不重啟即生效。
  - Tunnel token 經 runtime host 寫進 `~ymir/.config/ymir/cloudflared.env`（600）。cloudflared 改成 `ymir` 帳號的 rootless Podman Quadlet user service，runtime host 不需要提升權限。
  - 修訂了 ADR-0006 第 4 點與 ADR-0009 的設定來源。
- **這次完成**：
  - **總覽** `/admin`：
    - 使用者數；
    - Agent 執行中與排隊數；
    - 今日完成 / 失敗 / 取消；
    - 近 7 天長條圖（失敗以紅色標示）；
    - 各使用者的執行環境，可「停止」（寫入稽核 `admin.runtime.stop`，檔案保留，下次送訊息自動重啟），也可直接看該使用者的稽核紀錄。
    - 「今天」依瀏覽器時區切日，伺服器不依賴時區資料庫。
  - **稽核紀錄** `/admin/audit`：
    - 可依動作類型、結果、日期篩選；點操作者可篩選該使用者；
    - 動作顯示中文說明與代碼，使用者 id 顯示成名稱；
    - 「載入更多」往前翻（keyset 分頁）。
  - **後端**：
    - `IAuditLogQuery`（Platform，唯讀）；
    - `AdminStatsService`（Vibe Maker）；
    - 跨模組資料在 Api 層組合；
    - migration：`audit_log` 的 action / actor index、`agent_executions.created_at` index。
  - 管理區分頁改為「總覽 / 使用者 / Make 主題 / 稽核紀錄」，左下角「管理」改連到總覽。
- **驗證（實際跑過）**：
  - 後端：`dotnet test --solution Ymir.slnx` **328 項全部通過**，新增 5 個整合測試：總覽數字、位移範圍、停止 runtime 與稽核、沒有 runtime 時回 404、稽核篩選與分頁。授權矩陣加入 3 個 Admin 端點。`dotnet format` 通過，OpenAPI 快照已更新。
  - 前端：`npm run lint`、`npm test`（**67 項**）、`npm run build` 都通過。
  - 端對端：
    - 新增的 `npm run e2e:admin` **6 步全部通過**：一般使用者看不到「管理」→ 總覽 → 停止執行環境 → 跳到該使用者的稽核紀錄 → 動作篩選 → 窄螢幕。
    - `npm run e2e`（13）、`e2e:make`（10）、`e2e:auth`（6）都通過；`e2e:make` 與 `e2e:auth` 已配合新的管理首頁調整。
- **下一步**：PR B，Entra 設定頁。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #020 · Agent 回覆以 Markdown 排版

> 👤 **Claude（AI）** · 🕒 2026-10-06 15:45 · `✅完成`

- **問題**（使用者回報）：Agent 回覆直接顯示 `##`、```` ```cmd ```` 等 Markdown 語法。
- **做法**：
  - 新增 `marked`，用新的 `app-markdown` 元件（`web/src/app/shared/markdown.ts`）顯示 Agent 回覆，歷史訊息與串流中的即時回覆都適用。
  - 支援標題、清單、粗體、行內 code、程式碼區塊、表格、引用、連結。程式碼區塊上方標示語言，右上角有「複製」按鈕。
  - 使用者訊息與錯誤訊息維持純文字。
  - 先經 `AssistantText` 過濾思考內容，再渲染。
- **安全**：模型輸出視為不可信。
  - 原始 HTML 一律 escape 成文字；
  - 連結只允許 http(s) / mailto，並在新分頁開啟（`noopener`）；
  - 圖片不載入、只顯示成連結，避免以圖片網址把資料帶出去；
  - 結果仍經過 Angular 的 `[innerHTML]` sanitizer，不使用 `bypassSecurityTrust`。
- **Fake LLM**：新增 `[markdown]` 腳本，分段點刻意切在程式碼區塊中間，用來測串流中未閉合的 code fence。
- **驗證（實際跑過）**：
  - 前端：`npm run lint`、`npm test`（**61 項**，新增 7 項 Markdown 測試，含 escape 與危險連結）、`npm run build` 都通過。marked 只進對話頁的 lazy chunk（60.8 kB）。
  - 後端：`dotnet test --project tests/Ymir.UnitTests` **201 項通過**；`dotnet format` 通過。
  - 端對端：`npm run e2e` 新增一步，**13 個步驟全部通過**。這一步確認：
    - 串流中就已排版；
    - 完成後有 h2、`cmd` 程式碼區塊、表格，畫面上沒有 `##` 與 ```` ``` ````；
    - 按「複製」後剪貼簿內容正確。
  - 淺色與深色主題都已截圖確認。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #019 · 對話 `/make` 指令與 Make 主題管理

> 👤 **Claude（AI）** · 🕒 2026-10-06 15:10 · `✅完成`

- **需求**（使用者確認）：
  - 只送出 `/make` 時顯示主題按鈕；
  - 點按鈕直接送出，Agent 先問需求；
  - `/make 描述` 由 Agent 判斷適合哪個主題；
  - 主題由管理員在網頁上管理。
- **做法**：
  - 給 Agent 的完整指示由後端組合（`MakePromptBuilder`），存在 `AgentExecution.AgentPrompt`。對話紀錄只保留使用者打的短文字（例如「/make 小工具架設」）。主題的建置指示不經過前端。
  - `SendMessageRequest` 新增 `makeTopicId`。停用或不存在的主題回 400 `MAKE_TOPIC_NOT_AVAILABLE`；只送 `/make` 回 400 `MAKE_DESCRIPTION_REQUIRED`（前端不會送，這是防呆）。
  - 新資料表 `vibemaker.make_topics`，migration 預設兩個主題：「小工具架設」「網站系統架設」。
  - API：`GET /api/make-topics`（已登入）；`/api/admin/make-topics` 的 GET / POST / PUT / DELETE（AdminPolicy + antiforgery，寫 audit）。已加入授權矩陣與 OpenAPI 快照。
  - 前端：輸入框打 `/` 出現 `/make` 提示；只送出 `/make` 時在輸入框上方顯示主題按鈕；新對話標題去掉 `/make`。管理區新增分頁「Make 主題」，可新增、編輯、上移 / 下移、停用、刪除。
- **另外記錄**（使用者要求，尚未實作）：之後做後台時要加入 Entra ID 設定欄位（ID、secret 等）與 Cloudflare Tunnel 的 token、對應網域。兩項都會改變現有安全規則，實作前要先寫新 ADR。已記在 [開發規劃](../planning/development-plan.md#後台系統設定待辦功能)。
- **驗證（實際跑過）**：
  - 後端：`dotnet test --solution Ymir.slnx` **323 項全部通過**。包含 `MakePromptBuilder` 單元測試，以及 6 個 `/make` 整合測試：Fake LLM 收到的內容含主題指示與「先問需求」、主題清單、400 錯誤、admin CRUD、停用的主題不列出。
  - 前端：`npm run lint`、`npm test`（**54 項**）、`npm run build` 都通過；`npm run api:generate` 後 `schema.ts` 與 OpenAPI 一致。
  - 端對端（Fake LLM + 真實 Pi）：
    - 新增的 `npm run e2e:make` **10 個步驟全部通過**：`/` 提示 → `/make` 顯示兩個主題 → 點主題送出並收到回覆 → `/make 一個計算機` → Admin 新增主題、上移 → 輸入框出現新主題 → 停用後消失 → 刪除。
    - 原本的 `npm run e2e`（12 步）與 `npm run e2e:auth`（6 步）也全部通過。
- **未驗證、待使用者環境確認**：真實模型（MiniMax）是否照指示先問需求、判斷主題並回「主題：…」。Fake LLM 只能驗證送出的內容正確。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #018 · 合併 main 的看板衝突；redirect URI 已加入

> 👤 **Claude（AI）** · 🕒 2026-10-06 13:45 · `🚧進度`

- **redirect URI**：使用者確認已在 Entra 加入 `https://ymir.thetainformation.com/signin-oidc` 與 `http://localhost:5080/signin-oidc`。[entra-id.md](../guides/entra-id.md) 第 1 節已標註完成。
- **PR #15 的衝突**：開 PR 後，main 合併了 Codex 的 #13（對話隱藏思考）與 #14（OneDrive 計畫），這兩個 PR 和 Sprint 2 都在看板新增了 #015 留言。
  - 程式碼沒有衝突，只有這份看板衝突。
  - 處理方式：保留 Codex 的 #015、#016 原文；Sprint 2 的留言改為 [#017](#017--企業帳號entra-id與本機帳號密碼登入使用者管理)；置頂表兩邊的項目都保留。
  - 以 merge commit 合併 main，沒有改寫歷史。
- **合併後的驗證（實際跑過）**：
  - 後端：`dotnet format` 通過，`dotnet test --solution Ymir.slnx` **301 項全部通過**。
  - 前端：`npm run lint`、`npm test`（**35 項**，含 Codex 新增的思考過濾測試）、`npm run build` 都通過；`schema.ts` 與 OpenAPI 一致。
- **剩下由使用者做**（步驟見 [entra-id.md](../guides/entra-id.md)）：
  1. 把用戶端密碼的「值」填進部署主機的 `deploy/api/.env`。
  2. 企業應用程式「需要指派使用者」設為「是」，指派使用者並把自己設為 `Admin`。
  3. 依第 5 節的驗證清單實際登入。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #017 · 企業帳號（Entra ID）與本機帳號密碼登入、使用者管理

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

### #016 · 記錄 OneDrive Workspace 後續計畫，暫不實作

> 👤 **Codex（AI）** · 🕒 2026-10-06 13:15 · `📢公告`

使用者選定方法二：透過 Microsoft Graph 串接每位使用者自己的 OneDrive，並要求「先不要改程式，先把計畫寫下來」。已新增 [計畫文件](../planning/onedrive-workspace-plan.md)，同步更新開發規劃的 workspace 存放項目。

- 保留使用者專屬本機 workspace，任務前下載、任務後同步；Graph 憑證由後端保管，Agent 不取得。agent-state 與 SQL Server 資料仍留在部署環境。
- 記錄目錄映射、同步範圍、版本衝突、重試、解除連結、後續階段與驗收條件。帳號／租戶類型、精確權限、刪除及保留政策待確認；目前不開始開發或資料搬移。
- 驗證：對照 Microsoft 官方 Graph／授權／上傳文件，檢查 Markdown 連結與 `git diff --check`。本次只改文件，未執行程式測試，也未修改程式、資料庫或部署。

---

### #015 · 對話隱藏模型思考內容

> 👤 **Codex（AI）** · 🕒 2026-10-06 13:06 · `✅完成`

依使用者要求，對話畫面隱藏模型放在 `<think>` / `<thinking>` 內的推理內容。即時串流與歷史訊息共用純顯示 pipe；串流標籤尚未收齊時也隱藏，避免思考內容閃現。只過濾助理回答，保留使用者原文、工具狀態與資料庫內原始訊息。

- 驗證：Node 24 Docker 執行 `npm run lint`、`npm test -- --watch=false`（28 項通過）、`npm run build`，全部成功；`git diff --check` 通過。
- 已重建並部署本機網頁容器。內建瀏覽器登入既有 `docker-check`，查看含 MiniMax 思考區段的歷史對話，畫面只顯示「已建立 minimax-test.html」。本次沒有建立新測試帳號或啟動 Agent 容器。
- 後端與 API 契約未變更；沒有呼叫真實模型，串流分段行為由新增測試驗證。

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
