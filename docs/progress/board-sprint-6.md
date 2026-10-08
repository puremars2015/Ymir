# Sprint 6 · Agent 擴充能力、RAG 知識庫、前端網站託管

[← 回看板列表](README.md) ・ 規則與格式見 [README](README.md#留言規則)

---

## 📌 置頂：狀態總覽

> 最後更新：2026-10-08 11:30 ・ 狀態：**✅ 本輪計畫完成（A2、R0～R2、H0～H2 都在開發分支，使用者要求先不合併回 main；P0 維持僅計畫）**

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
| I1 | main 與擴充能力整合測試分支 | 🔬 | `claude/inspiring-einstein-2pwxx6`；不合併 main，見 #028 |
| W9 | 對話工具執行提示簡化 | ✅ | 工具／終端只顯示「正在工作中......」、清除完成／失敗／取消狀態；見 #027 |
| L3 | 個別使用者模型權限 | ✅ | 系統預設＋逐模型允許／禁止、選單／execution／virtual key 一致；ADR-0019，見 #026 |
| M2 | `/help` 系統用途與指令說明 | ✅ | Agent 說明功能、兩個指令與開放主題，斜線提示；見 #025 |
| M1 | `/make` 簡報與公司公告 Word 引導 | ✅ | 兩個可管理主題、唯讀公告模板及文件工具映像；見 #024 |
| F1 | 明確交付成果與工作檔分離（ADR-0015） | ✅ | 成功執行登記成果、單檔／ZIP、成果與專案檔案分頁；見 #010 |
| P0 | 多人、多 Agent 軟體專案協作計畫 | 📝 | 見 [#009](#009--多人多-agent-軟體專案協作計畫)；僅計畫，功能尚未實作，排程另定 |
| L2 | 管理員開放模型與預設模型 | ✅ | 持久設定、選單／執行／virtual key 一致授權；ADR-0016，見 #015 |
| L1 | LiteLLM OpenRouter 模型接入 | ✅ | Sonnet 5.5、GPT-6.1 Sol、GPT-6 Luna；virtual key、用量／費用與路由驗證；見 #012 |
| W7 | 圖示開啟模型與思考深度面板 | ✅ | execution 保存深度、Pi／LiteLLM 實際參數、手機與鍵盤操作；ADR-0017，見 #016 |
| W6 | 模型名稱與輸入能力圖示 | ✅ | 移除 provider 後綴、文字／圖片 SVG、自訂可存取選單；見 #014 |
| W5 | 手機對話輸入區靠近底部 | ✅ | 新／既有對話、鍵盤與安全區、訊息獨立捲動；見 #013 |
| W8 | PWA：手機可「加入主畫面」安裝 | ✅ | 見 #018；iOS / Android 實機安裝**待使用者環境確認** |
| W4 | AppDashboard 側欄清單與漢堡收合 | ✅ | 圖示、列高與群組分隔線；桌面記住收合，手機抽屜；見 #011 |
| W3 | AppDashboard 配色與淺色／深色／自動切換 | ✅ | 見 [#008](#008--appdashboard-配色與主題切換) |
| W2 | Ubuntu 既有 Docker 部署啟動檔 `start-ymir.sh` | ✅ | 見 [#005](#005--ubuntu-既有-docker-部署啟動檔)；語法與隔離模擬通過，實機待驗證 |
| W1 | Windows 既有部署一鍵啟動檔 `start-ymir.ps1` | ✅ | 見 [#004](#004--windows-既有部署一鍵啟動檔)；已在目前主機驗證 |
| A0 | ADR-0012 spike：實測 Pi 1.0.0 在 RPC 模式的 `--no-skills` / `--skill`、能否不讀使用者層 `mcp.json`、平台 MCP 設定能否放在 Agent 不可寫的位置，以及 rootless Podman 能否限制 egress；結果寫回 ADR-0012 | ✅ | — |
| A1a | 擴充政策（ADR-0012 第一階段）：<br>• `vibemaker.extension_policy`（`skills`、`mcp`）+ 每人覆寫資料表 `vibemaker.user_extension_grants`<br>• `IExtensionPolicy`<br>• `PiAgentHarness` 依政策組合參數<br>• `ymir-extension-builder` skill<br>• 管理介面、`GET /api/extensions`、稽核、授權矩陣<br>• 一律 `-ne`、每次執行重寫 `settings.json` / `trust.json` / `mcp.json`（A0 結果） | ✅ | — |
| A1b | 對外連線（ADR-0012 A.8）：<br>• `RuntimeNetworkAccess`、`VibeMaker:Runtime:RestrictedNetwork`<br>• container label 比對與重建、`runtime.recreate` 稽核<br>• runtime host 協定（只接受 enum）<br>• `internet` 能力加入擴充政策（全域 + 每人）<br>• 受限網路部署文件 | ✅ | — |
| A2 | MCP Gateway（ADR-0012 第二階段）：<br>• 獨立專案 `Ymir.McpGateway`<br>• 每人短期 token、`deploy/mcp/servers.json` 服務目錄、存取清單<br>• echo 服務、稽核與 rate limit、部署文件 | ✅ | 見 #013 |
| O0 | **插單** OneDrive connector ADR（[ADR-0013](../adr/0013-onedrive-connector.md)） | ✅ | — |
| O1 | OneDrive 連結 / 解除連結：<br>• `onedrive` 能力<br>• 授權碼 + PKCE 連結流程、refresh token 加密保存<br>• 根資料夾、設定頁<br>• FakeGraph 測試替身 | ✅ | — |
| O2 | OneDrive 同步：<br>• `IWorkspaceFileWriter`<br>• 執行前下載、執行後上傳（持久化工作）<br>• eTag 衝突保留兩份<br>• 雲端保存狀態與重試、使用指南 | ✅ | — |
| R0 | RAG ADR（ADR-0014）：服務與 volume 邊界、Embedding 抽象、向量儲存介面、SQLite 部署、權限 | ✅ | 使用者 2026-10-08 決定：Embedding 經 LiteLLM；回答模型由管理員標記可用於知識庫 |
| R1 | RAG 最小索引：知識庫、文件儲存、背景索引、Fake Embedding、每專案一份 SQLite | ✅ | 見 #015 |
| R2 | RAG 問答：檢索、回答、引用、資料不足提示、UI | ✅ | 見 #016 |
| H0 | 網站託管 ADR（[ADR-0016](../adr/0016-site-hosting.md)；0015 已用於明確交付成果）：獨立網站網域、每站來源隔離、私人網站登入、經主機複製產物、容量限制 | ✅ | 使用者 2026-10-08 決定：網域為設定值 `<代碼>.<BaseDomain>`（真實 DNS / Tunnel 待使用者環境）；可見範圍由網站擁有者選擇，Ymir 管理員一律可看 |
| H1 | 公開網站發布：網站與版本、產物檢查、SiteHost 託管（ADR-0016 不用 Nginx）、原子切換、取消發布 | ✅ | 見 #018 |
| H2 | 身分與分享：公司模式、指定使用者、分享給我的網站 | ✅ | 見 #019 |
| C1 | 對話附加檔案 / 圖片 / 影片給 Agent（使用者回報） | ✅ | 見 [#006](#006--對話可以附加檔案圖片影片給-agent)；真實視覺模型**待使用者環境確認** |

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

### #029 · 整合測試分支本機部署

> 👤 **Codex（AI）** · 🕒 2026-10-08 11:15 · `✅完成`

依使用者要求部署 `claude/inspiring-einstein-2pwxx6` 的 `ab212b2`，不合併 main。重新 publish API、SiteHost，建置 Angular／nginx 與 Agent 映像；保留 nginx 50 MB 上傳限制、現有登入金鑰、模型設定、對外網域與資料。部署前確認 0 個 Queued／Running execution，SQL Server COPY_ONLY／CHECKSUM 備份後套用完整 VibeMaker idempotent migration（sqlcmd 使用 QUOTED_IDENTIFIER ON）。本機備份位於 `%LOCALAPPDATA%/Ymir/deploy/ymir-before-inspiring-ab212b2.bak`。

API、nginx、SiteHost 已啟動；2 個正式使用者容器已移除以便下次使用重建新版，workspace／agent-state 保留。SiteHost 使用本機 `*.sites.localhost:5300`，未新增對外 DNS／Tunnel；知識庫未配置 EmbeddingModel、MCP 目錄為空且 Gateway 未啟動，因此兩者維持停用；OneDrive 沿用既有設定，需要實際帳號授權。

驗證：API health、本機 4200／5080、SiteHost health 與現有對外網域首頁均 200；本機與對外 models 匿名請求均 401；nginx 設定有效、50m 上限保留；新版 Agent uid=1000，Pi 1.0.0、pdftotext、python-pptx 與公告模板 smoke test 通過。16 個 VibeMaker migration 已套用，28 個對話與 124 個訊息保留。未以付費模型執行測試，也未以真實 OneDrive／MCP／Embedding 後端驗證。分支程式碼先前 Linux CI 764 項後端測試、164 項前端測試與映像檢查全數通過。

---


### #028 · main 與擴充能力整合至測試分支

> 👤 **Codex（AI）** · 🕒 2026-10-08 10:15 · `🔬驗證`

依使用者要求，將 main（15e02a8）整合到 `claude/inspiring-einstein-2pwxx6`，保留 MCP Gateway、OneDrive 同步、RAG 與公開／私人網站託管，並納入個別模型權限、思考深度、文件 /make、/help 與簡化工作提示。另兩個 Claude 工作分支已包含在此分支歷史內。此次不合併回 main，也不替換現有部署。

解決模型與 Agent request、DI、API 契約及文件衝突，重新產生 OpenAPI 與前端型別。修正知識庫未套用個別模型權限、知識庫索引換金鑰可能撤銷執行中 Agent 的整合問題；三種用途的金鑰分別快取，停用帳號時全部撤銷。Windows MCP 測試改用容器可達的本機假服務，OneDrive 測試改用跨平台可建立但不符合同步規則的長檔名。

已驗證：Release build 0 警告／0 錯誤；後端單元測試 493 通過、5 項既有 Windows 平台略過；前端 lint、164 測試、build；OpenAPI 快照 1、知識庫 11、模型權限 4、授權矩陣 4、MCP 6、網站 6、/make 10 項整合測試通過。OneDrive 連線測試先前 9 項通過，同步 6 項在修正 Windows 測試檔名後重跑全部通過；完整 Linux CI 檢查中（前端與 Agent 映像已通過）。測試使用 Fake LLM，不呼叫付費模型。

---

### #027 · 對話工具與終端機執行提示簡化

> 👤 **Codex（AI）** · 🕒 2026-10-08 01:16 · `✅完成`

- **實作**：對話區不再呈現工具名稱、指令、摘要及個別工具結果；有執行中的工具時只顯示「正在工作中......」。多個工具重疊也只顯示一行；終止事件優先清除提示，未收到個別工具完成事件也不會卡住。一般文字回覆保留，SSE 重播可還原提示。刪除不再使用的工具樣式，README 同步。
- **驗證**：前端 lint、148 Vitest 及 production build 通過；新增重疊工具／失敗工具／三種終止事件測試。`e2e:tool-progress` 以可控瀏覽器 SSE 替身驗證不顯示指令及工具摘要、單一提示、部分文字保留、重播、完成／失敗／取消及保存錯誤訊息；未呼叫模型或執行指令。截图確認畫面只保留工作提示與正常回覆。
- **部署**：確認沒有執行中工作後更新本機前端，網頁 200、API 健康 200、未登入 API 401。API、Agent 映像、資料庫、事件保存、workspace 與 agent-state 維持原樣；無 migration 或 API 契約變更。

---

### #026 · 個別使用者模型權限

> 👤 **Codex（AI）** · 🕒 2026-10-08 01:05 · `✅完成`

- **實作**：「管理 → 使用者 → 模型權限」提供依系統設定／允許／不允許，動態繼承且可額外開放系統未開放的已接入模型；保存個人覆寫、全部還原、預覽結果及沒有可用模型的提示。切換人員隔離草稿，手機編輯區不放在寬表格內。
- **後端**：沿用 `platform.system_settings`，不新增 migration；Admin／XSRF 保護、使用者存在與部署模型上限驗證、稽核。模型清單、送訊息、排隊執行啟動及限模型 virtual key 採個人生效權限。損毀個人設定拒絕開放，全部停用時拒絕送出訊息。契約、產生型別、README 與 [ADR-0019](../adr/0019-user-model-access.md) 同步更新。
- **驗證**：Release build 0 警告、format 檢查通過；420 單元測試通過（5 個既有 Windows shell 測試略過）；4 模型管理整合測試、4 授權矩陣測試及 OpenAPI 快照通過。前端 lint、144 Vitest 及 production build 通過。`e2e:user-models` 在隔離 Development API／SQL 真實驗證系統 a/b/d → Alice a/b/c、Bob a/b/d、切換人員、使用者選單、重新整理、還原、繼承變更、全部停用及手機版面；未呼叫付費模型。
- **部署**：本機 API 與前端已更新；部署前確認無執行中工作，健康檢查 200、前端 200、未登入 API 401。維持 Agent 映像、workspace、agent-state、資料與可信 proxy 位址；沒有調整任何既有使用者權限。

---

### #025 · `/help` 系統用途與指令說明

> 👤 **Codex（AI）** · 🕒 2026-10-08 00:43 · `✅完成與本機部署`

- **功能**：新增 `/help`（忽略大小寫、前後空白；不攔截 `/helper` 或一般文字），由後端組合平台事實給 Agent：先說明 Ymir 的問答、附件分析、專案與成果建置用途，再列出目前只有 `/help`／`/make` 兩個指令與使用範例。透過既有 MakeTopicService 取得開放主題，只提供名稱及說明、不公開內部建置指示；空清單時說明 `/make <需求描述>` 用法。要求只在對話說明，不呼叫工具或建檔。
- **前端**：輸入 `/` 同時提示兩個指令，輸入 `/h`／`/he` 只提示 `/help`；點擊填入 `/help`，送出後沿用既有 Agent、SSE 及歷史紀錄流程，紀錄保留原始指令。同步更新 `/make` 提示文字，涵蓋簡報與文件。
- **驗證**：Release build 0 警告；完整格式檢查；414 個單元測試通過（5 個既有 Windows shell 測試略過），10 個 Make／Help 整合測試通過（真實 Pi + Fake LLM；驗證 Agent 收到系統事實、指令、開放主題，歷史仍為 `/help`）；前端 lint、142 個測試與正式 build 通過。新增 `e2e:help`，瀏覽器驗證斜線提示、前綴篩選、Agent 回覆、沒有成果／下載卡片、重新整理歷史與手機提示皆通過；沒有呼叫付費模型。
- **部署**：publish 本機 API 至 `api-help-20261008`，前端映像 `localhost/ymir/web:help-20261008`；確認沒有執行中的任務後更新 API 及 web，保留回復版本、原有 Agent 映像與 workspace。web 仍綁定 127.0.0.1:4200／5080，可信任 proxy 的 bridge／ymir-edge IP 保持原值。API health、前端回應 200，匿名 `/api/me` 回應 401；無資料 migration 或 OpenAPI 契約變更。

### #024 · `/make` 簡報與公司公告 Word 引導

> 👤 **Codex（AI）** · 🕒 2026-10-08 00:25 · `✅完成與本機部署`

- **功能**：新增「建立簡報」與「建立公告 Word」資料 migration，沿用管理員編輯／排序／停用機制；先依主題詢問必要需求，再確認簡報逐頁大綱或公告草稿。既有主題不覆寫，API 契約不變。
- **公告模板**：依使用者提供的 DOCX 保留公司 Logo、橫幅、表格、背景、字型與頁尾；移除原始活動資料、姓名、信箱及作者資訊，原始文件未修改且不進版控。正文改成可重複的段落／條列，取消最小列高並靠上排列，頁尾文字改白色以提高對比。模板與建置器位於映像唯讀 `/opt/ymir/templates/announcement/`，建置器只修改 document.xml，其餘 package parts 保留。
- **簡報工具**：映像預裝鎖定版本 python-pptx 1.0.2／lxml 5.3.2 與 Noto CJK 字型；提供建置指引，避免建立工具用 npm 專案。只有最終 PPTX／DOCX 交付，JSON、腳本與模板不放入成果目錄。
- **驗證**：Release build（0 警告）、完整格式檢查、406 個單元測試成功（5 個 Windows shell 測試依原設計略過）、9 個 Make 整合測試成功（真實 Pi + Fake LLM）、前端 lint 與 141 個測試成功；`make-flow.mjs` 瀏覽器流程（含四主題、管理員排序／停用／刪除）通過。修正端對端測試原本假設只有兩個主題的排序步驟；Windows 專用 Make fixture 經 host.docker.internal 連 Fake LLM，避免容器 localhost 連線錯誤。沒有呼叫付費模型。
- **文件驗證**：5 個模板建置測試（package 保留、可變段落／條列、換行、XML 特殊字元、錯誤欄位及模板覆寫保護）通過；原始文件 2 頁與模板／測試成品各 1 頁均使用 LibreOffice 渲染並查看。唯讀 uid 1000 映像內 DOCX 建置與 PPTX 建置／重新讀取通過；提供可在文件映像內執行的模板測試腳本。
- **補充驗證與 CI 修正**：另以長公告產生 2 頁文件並查看跨頁正文及頁尾。遠端 CI 揭露既有 ModelAccessTests 共用資料庫的順序依賴：管理政策測試停用模型後影響思考深度測試；改為每個測試使用獨立 factory／資料庫，不改產品行為、不略過測試，2 個模型政策整合測試本機通過。
- **部署**：本機已套用 DocumentMakeTopics migration、publish API、建置 `make-documents-20261008` Agent 映像。確認沒有 QUEUED／RUNNING execution 後更新 API；移除 3 個舊映像容器，下一次使用由 API 重建，workspace／agent-state bind mounts 保留。API health 與前端回應 200，資料庫已登記兩個新主題。前端選單動態載入主題，無需更改使用者介面或重新建置前端。
- **限制**：實際模型的文件設計品質尚未用付費模型驗證；Agent 映像不含 Office 渲染器，要求其如實說明無法視覺檢查的情況。長公告可跨頁，需依內容檢查分頁；平台模板沿用來源的非 A4 頁面尺寸。

### #023 · 依模型 API 能力提供思考選項

> 👤 **Codex（AI）** · 🕒 2026-10-07 23:41 · `✅完成與部署`

依使用者要求新增模型 Thinking 能力契約（Parameter／Levels／DefaultLevel／Required），前端依精確清單顯示，MiniMax 不顯示深度控制；依官方 OpenRouter 目錄，Luna 可選 none／low／medium／high／xhigh／max，Sol／Sonnet 為必須思考且排除 none。未知／不適用的保存偏好採模型預設，不強制改成另一值。提交與執行開始均驗證精確值；原 execution 欄位足以保存新 effort，無 migration。舊 SupportsThinking=true 相容低／中／高，新部署採明確配置。ADR-0018、README、部署環境範例、OpenAPI 與前端型別同步。

实际传輸驗證抓到并修正：Pi 的别名 xhigh 被降为 high，需要显式 thinkingLevelMap；off→null 會排除 off 而改用 minimal，自動改为本次模型不受 Pi effort 管理且不送參數，none 才明確 off→none。LiteLLM 1.103.2 的 reasoning_effort 转換未完整保留新值，OpenRouter 部署採 reasoning.effort，經假上游驗證精確值。無 adapter 的 token-budget／其他開關配置會拒絕啟動，不冒充通用深度，未宣稱已支援所有廠商格式。

驗證：後端單元 406 通過／5 個平台相關略過，模型存取與 OpenAPI 整合 3 通過；前端 lint、139 測試、build 通過；dotnet format verify 與 git diff --check 通過。Pinned Pi 兩種傳輸格式共 38 個本機假請求，LiteLLM 28 個假上游請求，驗證精確 effort、none／自動及同 session 重設，全部通過；新版預覽與部署入口兩次 e2e 驗證各模型選项、點擊／鍵盤／偏好、訊息參數與手機佈局通過。首輪 SQL 測試因 localhost 連線逾時，改用 127.0.0.1 後通過。

部署確認無執行中工作，API 更新至 api-thinking-capabilities-20261007，前端映像 localhost/ymir/web:thinking-capabilities-20261007；launcher 三個 OpenRouter 模型已配置實際 Levels、預設與 Required，保持金鑰、管理員開放清單、workspace／agent-state 和入口 IP。API health、本機與公開入口 200，提供 main-WOD4GX4O.js；未發送付費模型請求，原瀏覽器需重新整理取新版本。

---
### #022 · 思考深度改用直接點擊按鈕

> 👤 **Codex（AI）** · 🕒 2026-10-07 23:25 · `✅修正與部署；原現場原因未重現`

使用者回報電腦上選項正常、點選思考深度沒有改變、面板仍開著。核對前端依 supportsThinking 控制停用、部署 launcher 三個 OpenRouter 模型設定 true；既有 API fixture 瀏覽器檢查未重現原現場問題，不把推測當成已確認原因。將隱藏 radio／label／change 的間接操作改成原生 button 直接 emit，整個選項可點擊，aria-checked 與畫面由同一深度狀態控制；保留 radiogroup 語意、方向鍵／Home／End、Enter／Space、Tab、disabled 模型與 Escape／外部關閉。

擴充既有模型選單 e2e：三個 OpenRouter 模型皆點擊選項邊缘並輪流選四種深度，驗證鍵盤切換、重新整理保存；GPT-6 Luna／GPT-6.1 Sol 加驗送出 modelId 與 thinkingLevel，既有 Sonnet／新對話／專案／不支援模型行為仍通過。前端 lint、build、136 測試與 git diff --check 通過；新版隔離預覽及部署入口兩次 e2e 通過，使用 API fixtures，沒有呼叫付費模型或修改正式對話。已部署 `localhost/ymir/web:thinking-buttons-20261007`，確認本機與公開入口皆提供 main-UPLCSBKV.js，nginx 與入口 IP 保留；預覽容器已清理。使用者原有瀏覽器需重新整理後確認，尚未宣稱已在其原現場重現及驗證。

---
### #021 · 統一對話輸入提示

> 👤 **Codex（AI）** · 🕒 2026-10-07 23:04 · `✅完成`

依使用者要求，共用 composer 的提示文字簡化為「問問Ymir」，移除首頁與專案頁的個別覆寫，讓新對話、既有對話及專案對話一致。前端 lint、production build、git diff --check 通過；純提示文字修改，沒有新增測試。已更新本機前端映像 `localhost/ymir/web:prompt-20261007`，確認入口提供新版 main-EVVTF3DY.js、nginx 設定有效，保留原入口網路 IP 與後端／資料。可重新整理查看。

---
### #020 · 合併後重新建置與本機部署

> 👤 **Codex（AI）** · 🕒 2026-10-07 23:01 · `✅完成`

依使用者要求，重新執行 Angular production build、API Release publish 與 Agent image build，部署合併提交 `4583f3d`。API 更新至 `%LOCALAPPDATA%/Ymir/deploy/api-merged-4583f3d-20261007`；web 與 Agent 映像分別為 `localhost/ymir/web:merged-4583f3d`、`localhost/ymir/agent-runtime:merged-4583f3d`，launcher 已切換新路徑／映像。確認執行中任務為 0，並以 idempotent SQL 套用 OneDriveConnections；既有 ExecutionThinkingLevel 保留。三個閒置正式使用者容器已重建並恢復閒置，沿用原 workspace、agent-state、網路與資源／安全限制。

nginx 維持 `client_max_body_size 50m`，增加 index.html、sw.js、manifest.webmanifest 的 no-cache／no-store，保留 SSE、轉送設定及入口 IP。API health、本機前端與公開入口 200；本機／公開 PWA manifest 與 service worker 200 並確認不快取；未登入 API 回應 401。部署前後均通過模型選單瀏覽器檢查，包含模型名稱／圖示、思考選擇與保存、首次訊息與既有對話、鍵盤操作、手機與縮小視窗；Agent Pi 1.0.0 與 pdftotext 檢查通過。前版 web 容器、API publish 與 launcher 備份保留供回復；預覽及此次 Agent 回復容器已清理。

沒有發送付費模型請求或連線真實 OneDrive；手機實機安裝仍待確認。合併測試結果沿用 #019：前端 136 測試通過，後端完整 Windows 測試仍有 50 個環境／Runtime／Pi 失敗，未宣稱全部通過。本次重新建置與部署驗證通過，資料與金鑰保留。

---

### #019 · 解決 main 與遠端合併衝突

> 👤 **Codex（AI）** · 🕒 2026-10-07 22:53 · `✅完成`

完成使用者已開始的 merge（本機 1 個提交、遠端 8 個提交）：index.html 保留本機 `interactive-widget=resizes-content` 與遠端 PWA manifest／iOS meta；看板保留兩邊所有內容，PWA 的重複 W5／#012 改為 W8／#018，原留言文字保留。OpenRouter、模型管理、思考深度與 OneDrive／PWA 功能均保留。修正 3 處檔尾換行及 ModelAccessTests using 排序，使 dotnet format 驗證通過。

驗證：Release solution build 零警告／錯誤；前端 lint、136 測試、build 通過；後端完整 Windows 測試實際結果 583 通過、50 失敗、7 略過，失敗包含 Windows 不支援 Local runtime 的 fixture，以及 Docker／Pi 的 Fake LLM 位址與 runtime 預期不一致，未修改或停用測試來掩蓋。合併相关 OpenAPI 快照 1、OneDrive 9、WebAppHosting 11、Models 8 個整合測試另行通過。完整 Runtime／Pi 路徑需由 Linux CI 驗證。本次測試建立的暫時 Docker 容器已清理，原有服務保留；未更新目前部署的 API／前端。

此合併會以正常提交保留雙方歷史，完成 pull 並 push 至 origin/main，不使用 force push。

---

### #018 · PWA：手機可安裝到主畫面

> 👤 **Claude（AI）** · 🕒 2026-10-07 19:00 · `✅完成`

前端本來就是 SPA，補上 PWA 安裝能力，使用者用手機開網址即可「加入主畫面」，以獨立視窗開啟。

- **新增**：`manifest.webmanifest`、192／512／maskable 圖示（暫由 `apple-touch-icon.png` 放大產生，有正式 logo 再替換）、`sw.js`、`index.html` 的 manifest 與 iOS meta、`viewport-fit=cover`。
- **service worker 只快取帶雜湊的靜態檔**（cache-first）；`/api`（含 SSE、登入、下載）與 `index.html` 一律不經快取，登入狀態仍是 HttpOnly cookie（ADR-0002），不碰任何憑證。沒有離線功能（Agent 需要連線）。
- **登入頁安裝提示**：Android 顯示原生安裝按鈕（`beforeinstallprompt`），iOS 顯示「分享 → 加入主畫面」說明，已安裝則不顯示（`PwaInstallService` + 純函式 `pwa-rules.ts`）。
- **伺服器**：`WebAppHostingExtensions` 對 `index.html`、`sw.js`、`manifest.webmanifest` 回 `Cache-Control: no-cache`，手機才會取得新版。
- **CSP 不需修改**：`manifest-src` / `worker-src` 退回 `default-src` / `script-src 'self'`。沒有新增相依套件（手寫 service worker）。

驗證：
- 前端 lint 通過、131 個測試通過（含新增 `pwa-rules.spec.ts`）、production build 通過；
- `dotnet build`、`dotnet format --verify-no-changes`、`WebAppHostingTests` 11 個通過（含新增 no-cache 與 manifest content-type）；
- Playwright（Chromium、iPhone 尺寸）：service worker 啟用、manifest 可讀、重新整理後只快取 5 個雜湊檔（無 `/api`、無 `index.html`）、登入頁顯示 iOS 說明。

**未驗證、待使用者環境確認**：真實 iPhone / Android 在 HTTPS 網域（Cloudflare Tunnel）上的安裝與登入（iOS 主畫面 App 的 cookie 與 Safari 分開，第一次需重新登入；Entra OIDC 在獨立視窗內的跳轉需實機測）。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #017 · 清理本機未使用 Docker 容器與映像

> 👤 **Codex（AI）** · 🕒 2026-10-07 18:51 · `✅完成`

依使用者要求清理目前主機：刪除 30 個停止的容器（舊前端備份／預覽、已停止 Agent 與舊測試容器）及 24 個未被保留容器引用的映像，並清除建置快取。沒有停止任何執行中的服務，也沒有刪除 volume 或 host 資料目錄；workspace／agent-state 的 bind mount 資料保留。除了 7 個執行中服務使用的映像，額外保留部署 launcher 指定的 `localhost/ymir/agent-runtime:deliverables`，避免下次建立 Agent 時缺少映像。

`docker system df`：映像 32 → 8、7.699 GB → 6.616 GB；容器 39 → 9、剩餘全部執行中；建置快取占用 23.61 MB → 0。Docker 管理的內容約減少 1.1 GB；此數值不表示 Windows 虛擬磁碟檔會立即縮小，未停止 Docker 或進行 VHDX 壓縮。3 個既有 volumes 全部保留。清理後 localhost:4200 及 API health 均 HTTP 200，SQL／LiteLLM／Tunnel／EBS 與使用中 Agent 容器保留。

---

### #016 · 圖示模型與思考深度設定

> 👤 **Codex（AI）** · 🕒 2026-10-07 18:48 · `✅完成`

輸入區使用滑桿圖示開啟面板，含模型清單與自動／輕量／標準／深入；維持純模型名稱與文字／圖片圖示，旁邊顯示目前選擇。新對話、既有對話與專案共用，Tab 可到深度、Escape／外部點擊關閉，手機依 viewport 展開。修正 Chrome 點擊 radio label 的暫時空 relatedTarget 導致提早關閉。

部署明確宣告 `SupportsThinking`，三個 OpenRouter 模型經官方目錄確認可使用 low／medium／high；MiniMax 維持模型預設。每則訊息捕捉深度，API 驗證能力與允許值，execution migration 保存 `thinking_level`，新對話第一則與冪等重送同樣保留。Pi models.json 開啟 reasoning 能力並傳入 `--thinking`；自動重設 session 且不傳 reasoning_effort，不要求供應商關閉原有思考。[ADR-0017](../adr/0017-execution-thinking-depth.md)、README、OpenAPI 與前端型別已同步。

驗證：後端單元 386 通過／5 Windows shell 測試略過；相關 Models 整合 8 通過、OpenAPI 快照 1 通過；前端 lint／build、128 測試通過。pinned Pi 1.0.0 映像本機假模型 12 次 RPC 驗證三模型的 low／medium／high 與同 session 自動重設；LiteLLM 本機上游 21 次請求驗證原有文字／工具／串流與深度／預設，不呼叫付費模型。瀏覽器 fixtures 驗證面板、鍵盤、深度偏好、既有與第一則訊息參數、無能力模型送 null、手機與深色；手機 composer 與管理員模型面板回歸通過，部署後 localhost:4200 再驗證通過。手機原生鍵盤與付費模型輸出效果未實測。

本機 API 更新至 `api-model-settings-20261007`，web image `localhost/ymir/web:model-settings-20261007`，保留 workspace／agent-state 及入口網路 IP。正式模式不自動 migrate，啟動首次因缺少 thinking_level 退出；已套用該次 idempotent migration 後重啟，health／web 200。部署 launcher 已加入三模型 SupportsThinking 設定，未修改管理員的開放清單。截圖保存於 `%LOCALAPPDATA%/Ymir/deploy/screenshots-model-settings-20261007/deployed/`。既有未提交變更保留，未推送遠端。

---

### #015 · 管理員控制開放模型

> 👤 **Codex（AI）** · 🕒 2026-10-07 18:30 · `✅完成`

新增「管理 → 系統設定 → 開放模型」勾選清單、預設與还原部署設定；既有 system_settings 保存政策，不需要 migration。後端只允許部署 Models／AllowedModels 交集，至少一個與合法預設；Admin／XSRF／稽核保護。使用者清單與新訊息立即讀取政策，排隊執行啟動前重查、模型關閉則失敗，已開始不打斷。下次執行 virtual key 模型集合不同時換發、撤銷舊 key，空模型不發 key。前端頁面與視窗回前景刷新，儲存後同步刷新。新增 ADR-0016、OpenAPI／前端型別與 README。

驗證：後端 build／Release publish 零警告、單元 384 通過／5 個 Windows shell 測試略過；模型相關整合 7、授權矩陣 4、OpenAPI 快照 1 通過（SQL 使用 127.0.0.1 避免 localhost IPv6 逾時）；測試覆蓋非法政策、不允許的模型、損毀設定不重新開放、持久設定、拒絕成員修改／停用模型、排隊停用與 virtual key 換發。前端 lint／123 Vitest／build 通過；`e2e:admin-models` 唯讀狀態 fixtures 驗證儲存、還原、空清單、預設、重新整理、使用者清單與手機；model-picker 及 mobile-composer 回歸通過。未對真實使用者變更開放政策，沒有額外上游付費模型呼叫。

本機 API 更新至 `api-model-access-20261007`（確認排隊／執行數為 0），前端映像 `localhost/ymir/web:model-access-20261007`；保留 API launcher／前端旧容器備份。未重啟 LiteLLM／SQL／Agent，也不改動目前開放模型。

### #014 · 模型名稱與輸入能力圖示

> 👤 **Codex（AI）** · 🕒 2026-10-07 18:10 · `✅完成`

共用模型選單只顯示名稱，移除既有 OpenRouter 後綴及「可看圖片」文字，文字／圖片能力改為 OpenRouter 風格的小型 SVG 徽章。文字輸入皆顯示 T，圖片以既有 supportsImages 決定；不推測音訊或影片能力。API 型別、模型 alias 與路由保持不變，部署模型名称範本同步移除後綴。選單改為可容納 SVG 的 listbox，支援方向鍵、Home／End、Enter／空白、Escape、Tab、外部點擊與焦點回復，隨可用空間展開，viewport 改變時關閉。

驗證：lint、build、123 個 Vitest 通過；新增 `e2e:model-picker` 唯讀 fixtures，驗證模型名稱、文字／圖片圖示、選擇保存與重新整理、鍵盤、外部關閉、對話／專案重用、手機定位、深色與鍵盤尺寸；`e2e:mobile-composer` 回歸通過。既有 chat-flow 模型選擇與還原檢查同步改用新選單；未執行會建立真實對話的完整 chat-flow。

README 已更新，本機前端映像為 `localhost/ymir/web:model-picker-20261007`；原容器保留為備份，維持 50m、API 代理與網路。未重啟 API／LiteLLM／資料庫／Agent。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---
### #013 · 手機對話輸入區靠近底部

> 👤 **Codex（AI）** · 🕒 2026-10-07 18:00 · `✅完成`

手機新對話原本置中，既有對話的冗長說明則占據輸入框下方空間；現在兩者均靠近底部，保留 8px／safe-area。訊息區獨立捲動、header／composer 不縮小；多行輸入高度限制為可見畫面的四分之一。Shell 監聽 VisualViewport resize／scroll，調整手機頁面高度及位置，縮放閱讀時不重新縮排，離開手機尺寸後清除覆寫；viewport meta 與側欄也配合安全區。README 同步更新。

驗證：`npm run lint`、`npm test -- --watch=false`（123 通過）、`npm run build` 通過。`npm run e2e:mobile-composer` 在唯讀 API fixtures 下驗證 390×844／375×667／667×375、新舊對話、送出按鈕、多行輸入、附件、40 則訊息獨立捲動、模擬鍵盤縮小／平移／收起、pinch zoom 與桌面還原；`e2e:sidebar` 桌面收合、手機焦點與抽屜回歸通過。未在手機實機 Safari／Chrome 測試原生鍵盤。

本機前端更新至 `localhost/ymir/web:mobile-composer-20261007`，保留原 nginx 50m／API 代理設定、連線網路與旧容器以便回復；API、LiteLLM、資料庫與 Agent 未重啟。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---
### #012 · LiteLLM OpenRouter 模型接入

> 👤 **Codex（AI）** · 🕒 2026-10-07 17:45 · `✅完成`

依使用者指定開放 anthropic/claude-sonnet-5.5、openai/gpt-6.1-sol、openai/gpt-6-luna。LiteLLM 原生 OpenRouter provider、Compose 的 OPENROUTER_API_KEY／OR_APP_NAME、金鑰與 Ymir 模型設定範本、部署說明及 README 已更新。上游 key 僅交给 LiteLLM；Models 與明確的 AllowedModels 同步加入三個 alias，支援圖片輸入，保留既有 MiniMax-M2.7 預設。

驗證：OpenRouter key 驗證與官方模型清單確認成功；固定的 LiteLLM 1.103.2 映像中 test-openrouter.py 完成三模型共 9 次本機 HTTP stub 驗證（路由、模型 ID、鑰匙、文字、工具與串流）；Compose config --quiet、git diff --check 通過。重建本機 LiteLLM 並於零排隊／執行工作時重啟 API，health 200。額外建立 5 分鐘、US$0.05 上限且僅允許三模型的測試 virtual key，三個真實模型皆 HTTP 200、非零 response cost；未列入模型 403，測試 key 已撤銷。未對真實使用者對話送出 Agent 訊息，未測試真實圖片或工具呼叫；其 provider 格式由 stub 覆蓋。

現有 PostgreSQL volume、master key、salt、MiniMax key 保留；服務設定備份位於本機部署目錄。變更僅部署設定與文件，不修改 API 契約／資料庫 schema，也不新增圖片生成、語音或 embeddings 的 UI。

---

### #019 · H2 完成：私人網站的票據登入、分享與管理員可見（本輪計畫收尾）

> 👤 **Claude（AI）** · 🕒 2026-10-08 11:30 · `✅完成`

- **做了什麼**（ADR-0016 §3、§4）：
  - **存取模式**：公開 / 所有 Ymir 使用者 / 指定使用者（`vibemaker.site_shares`）。
    - 擁有者與 Ymir 管理員一律可以看。
    - API 與 SiteHost 共用 `SiteAccessRules.CanView`。
  - **私人網站登入**：
    1. SiteHost 導向平台的 `/site-access`（未登入時先走 Ymir 登入）；
    2. 前端 `POST /api/sites/{id}/ticket` 取得票據：60 秒、一次性、資料庫只存 SHA-256；
    3. SiteHost 的 `/.ymir/auth` 以條件更新兌換，設定 `ymir_site` cookie：只限該 hostname、綁定網站、HttpOnly、8 小時。
    - 導回路徑只接受站內相對路徑。
    - 平台 cookie 不會送到網站網域。
  - **每個請求的授權**：重新檢查帳號狀態、管理員角色與分享名單（快取 ≤ 30 秒）。網站查詢快取 10 秒。
  - **API**（加入授權矩陣與 OpenAPI；存取變更寫稽核 `site.access.update`）：
    - `PUT /api/sites/{id}/access`；
    - `GET /api/sites/shared-with-me`；
    - `GET /api/users/search`：只回未停用帳號的 id / 名稱 / 帳號，不含自己；
    - 票據端點。
  - **前端**：
    - 「我的網站」的存取設定與分享選擇器；
    - 「分享給我的網站」；
    - `/site-access` 轉接頁：沒有權限時顯示摘要。
  - **修正**：SiteHost 原本用 `app.Run`，會在端點之前攔下 `/.ymir/*`。改成 `MapFallback("{**path}")`。
- **驗證**：
  - `dotnet build`（0 警告）、`dotnet format --verify-no-changes`。
  - `dotnet test --solution`：722 / 722 通過，新增：
    - `SiteHostingTests` 2 個：私人網站導向登入、票據 / cookie、分享、AllUsers、撤權、管理員、別人改不了設定；票據重放、跨站兌換、cookie 綁定網站、停用帳號、分享名單拒絕停用帳號。
    - `SiteRulesTests` 15 個：CanView、SafeReturnPath、票據雜湊。
  - 前端 lint、141 個測試、build 通過。
  - **e2e:sites**（`*.localhost`，SiteHost 在 5300）全部通過，截圖 5 張：
    - 發布 → 匿名看公開網站；
    - 改成指定使用者 → 匿名被導向登入；
    - 被分享者經票據進站，「分享給我的網站」看得到；
    - 其他使用者被拒；
    - 管理員可看；
    - 撤權後 cookie 失效。
- **發現**：公開網站回應帶 `Cache-Control: public, max-age=60`，所以公開改私人後，**已經看過**的瀏覽器最多 60 秒內還會顯示快取內容。私人網站一律 `private, no-store`。
- **未驗證、待使用者環境確認**：
  - 真實網域、萬用字元 DNS / TLS、Tunnel 路由；
  - 正式主機 SiteHost 的 Data Protection 金鑰目錄權限。
- **本輪總結**：A2 → R0 → R1 → R2 → H0 → H1 → H2 都已完成，都在 `claude/inspiring-einstein-2pwxx6`。依使用者指示**未合併回 main**；PR #47 保持開啟。P0 維持「僅計畫」。

---
### #018 · H1 完成：公開網站發布（SiteHost）

> 👤 **Claude（AI）** · 🕒 2026-10-08 10:30 · `✅完成`

- **做了什麼**（ADR-0016 H1）：
  - **發布**：檔案面板新增「🌐 發布網站」，會列出含 `index.html` 的目錄，可選 SPA 模式。
    - `SiteService` 先檢查擁有者，再經 runtime 內的 reader 把檔案複製到網站 volume 的新版本目錄。
    - 完整寫好才切換版本；保留最近 3 版；失敗不影響目前版本；第一次發布就失敗時不留下網站。
  - **`src/Ymir.SiteHost`**（唯讀）：依 `{代碼}.{BaseHost}` 找網站，只提供版本目錄內的檔案。
    - 路徑逐段檢查，並確認解析後仍在版本目錄內。
    - SPA 模式只對沒有副檔名的路徑回 index.html，不存在的資源仍是 404。
    - 隱藏檔不會發布，也不會被提供。
    - 標頭 `nosniff`；未知或未發布的網站一律回同樣的 404。
  - **「我的網站」頁**（側邊欄「網站」）：網址、重新發布、取消發布、刪除。
  - **API**：已加入授權矩陣與 OpenAPI；狀態變更寫稽核 `site.publish` / `site.unpublish` / `site.delete`。
  - **部署**：`deploy/sites/`（systemd unit、env 範本、萬用字元網域與 Tunnel 說明）。
- **驗證**：
  - `dotnet build`（0 警告）、`dotnet format --verify-no-changes`。
  - `dotnet test --solution`：705 / 705 通過，新增：
    - `SiteHostingTests` 4 個：提供檔案與 SPA、重新發布與保留版本、取消發布與刪除、擁有者與限制。
    - `SiteRulesTests` 15 個。
  - 前端 lint、139 個測試、build 都通過。
  - 網站的 e2e（公開 + 私人）在 H2 一起做。
- **未驗證、待使用者環境確認**：真實網域、萬用字元 DNS / TLS、Tunnel 路由、正式 volume 權限。
- **下一步**：H2（私人網站：票據登入、分享、管理員可見）。

---
### #017 · H0 完成：ADR-0016 網站託管

> 👤 **Claude（AI）** · 🕒 2026-10-08 09:30 · `✅完成`

- **做了什麼**：新增 [ADR-0016](../adr/0016-site-hosting.md)，並更新 ADR 索引、計畫狀態，以及 CLAUDE.md 的 ADR 範圍。
  - **SiteHost**：新增獨立服務 `Ymir.SiteHost`。它依 Host 找網站、檢查權限、提供靜態檔案；只讀，不執行使用者程式。
  - **發布**：API 經 `IWorkspaceFileReader` 把選定的目錄複製成版本，完整寫好後才切換；保留最近 3 個版本。
  - **存取模式**：公開 / 所有 Ymir 使用者 / 指定使用者；擁有者與 Ymir 管理員一律可以看。
  - **私人網站登入**：前端 `/site-access` 頁取得 60 秒一次性票據 → SiteHost 兌換成只限該 hostname 的 cookie → 每個請求重新檢查權限（最多快取 30 秒）。
  - **限制**：單站 50 MB、2,000 個檔案、每人 20 個網站。另有 SPA 路由、路徑檢查、快取標頭的規則。
- **驗證**：文件變更，無程式。
- **下一步**：H1 公開網站發布。

---
### #016 · R2 完成：根據專案文件問答並附引用

> 👤 **Claude（AI）** · 🕒 2026-10-08 09:00 · `✅完成`

- **做了什麼**（ADR-0014 §8）：
  - `POST /api/projects/{id}/knowledge/ask`：
    1. 問題向量化；
    2. 只在該專案、相同 embedding 模型的可查詢版本中檢索；
    3. 低於門檻的段落不列為引用；沒有段落時回「資料不足」，**不呼叫模型**。
  - **回答模型**：只有管理員標記 `VibeMaker__Models__N__AllowKnowledgeBase=true` 的模型會收到片段。其他模型只回相關段落（`modelAllowed=false`），片段不會外送。
    - 這點與 ADR 原本寫的「回 403」不同，實作改為仍回傳段落，ADR 已同步修正。
  - **Prompt**：片段放在 `<knowledge>` 資料區塊，system prompt 要求把片段當資料、以 `[n]` 標註來源。
  - **引用內容**：檔名、版本、段落、頁碼、摘錄；不含伺服器路徑。
  - **前端**：知識庫卡加問答區，模型選單只列可用模型，回答以文字綁定顯示，引用可以展開。
  - **Fake LLM**：遇到 `<knowledge>` 就回第一個片段並附 `[1]`。
- **驗證**：
  - `dotnet build`（0 警告）、`dotnet format --verify-no-changes`。
  - `dotnet test --solution`：686 / 686 通過，新增：
    - `KnowledgeAskTests` 4 個：引用、資料不足不呼叫模型、未允許的模型只回段落、他人 404 與無效問題。
    - prompt 資料區塊的單元測試。
  - 前端 lint、137 個測試、build 都通過。
  - `e2e:knowledge`：建立專案 → 上傳 2 份文件 → 索引完成 → 提問得到 `[1]` 與「請假規則.txt」引用 → 無關問題顯示資料不足，全部通過。
  - e2e 備註：Playwright 以「檔案路徑」上傳時，瀏覽器讀不到 scratchpad 目錄的檔案，改用 buffer 上傳（附件的 e2e 也是這樣做）。
- **未驗證、待使用者環境確認**：真實模型的回答與引用品質、LiteLLM 的預算在知識庫問答上的套用。
- **下一步**：H0（ADR-0016 網站託管）。

---
### #015 · R1 完成：專案知識庫的文件索引

> 👤 **Claude（AI）** · 🕒 2026-10-08 08:00 · `✅完成`

- **做了什麼**（ADR-0014 R1）：
  - **上傳**：專案頁新增「知識庫」卡，可上傳、看狀態（自動更新）、重試、移除。
  - **API**：`/api/projects/{id}/knowledge/*`，驗證擁有者，已加入授權矩陣。
  - **背景索引** `KnowledgeIndexWorker`：
    1. 擷取文字：TXT / Markdown / 文字型 PDF（含頁碼）/ DOCX（禁止 DTD）；
    2. 切段：依句尾、有重疊、不跨頁；
    3. 經 LiteLLM `/v1/embeddings` 向量化（使用者的 virtual key；embedding 模型自動加入 key 的允許模型）；
    4. 寫入每專案一份 SQLite。
  - **版本切換**：新版本完整寫入才切換（同檔名只有一份 Ready），失敗保留舊版本，重啟時重做、不會產生重複段落。
  - **儲存位置**：原始檔與 SQLite 放在 API 自己的 volume `Ymir:Knowledge:Root`，不在 Agent workspace。
  - **Fake LLM**：新增 `/v1/embeddings`（固定的字元 bigram 向量）。
- **驗證**：
  - `dotnet build`（0 警告）、`dotnet format --verify-no-changes`。
  - `dotnet test --solution`：681 / 681 通過，新增：
    - `KnowledgeBaseTests` 5 個：各格式索引、無效檔案摘要、新版本取代與移除後不再召回、專案與使用者隔離、重啟重做。
    - `KnowledgeDisabledTests` 1 個。
    - `KnowledgeRulesTests` 11 個。
  - 前端 lint、136 個測試、build 都通過。
  - 知識庫的 e2e 在 R2（問答）完成後一起做。
- **未驗證、待使用者環境確認**：真實 Embedding 模型的品質與速度、正式 volume 的備份。
- **下一步**：R2 問答。

---
### #014 · R0 完成：ADR-0014 RAG 知識庫

> 👤 **Claude（AI）** · 🕒 2026-10-08 06:40 · `✅完成`

- **做了什麼**：新增 [ADR-0014](../adr/0014-rag-knowledge-base.md)，並更新 ADR 索引、計畫文件狀態，以及 CLAUDE.md 的 ADR 範圍。
  - 知識庫屬於專案，首版只有擁有者可用。
  - 文件與每個專案一份的 SQLite 放在 API 自己的持久 volume（`Ymir:Knowledge:Root/<userId>/<projectId>/`），不掛給 Agent。
  - Embedding 經 LiteLLM、使用者的 virtual key。回答模型需要管理員標記 `AllowKnowledgeBase`。
  - 支援格式：TXT、Markdown、文字型 PDF（PdfPig）、DOCX。
  - 版本化索引：新版本成功才切換，失敗保留舊版本，重啟後重做。
- **Spike（沙箱）**：
  - 2 萬段 × 1024 維向量存進 SQLite：寫入約 1.2 秒、檔案 156 MB；暴力餘弦搜尋約 250 ms（純量迴圈）。
  - PdfPig 0.1.16 可以載入。
  - `sqlite-vec` 沒有官方 NuGet，首版不採用，之後可在 `IVectorStore` 下替換。
- **未驗證、待使用者環境確認**：真實 Embedding 模型的繁中檢索品質與速度、LiteLLM 的 embedding 路由。
- **下一步**：R1 最小索引。

---
### #013 · A2 完成：平台 MCP Gateway；接下來依序完成 RAG 與網站託管

> 👤 **Claude（AI）** · 🕒 2026-10-08 06:00 · `✅完成`

- **使用者指示（2026-10-08）**：
  - 依序完成剩下的計畫：A2 → R0 → R1 → R2 → H0 → H1 → H2；P0 維持「僅計畫」。
  - **做完先不要 merge 回 main**，留在 `claude/inspiring-einstein-2pwxx6`；PR #47 保持開啟、不合併。
- **使用者決定**：

  | 項目 | 決定 |
  |---|---|
  | RAG Embedding | 經 LiteLLM `/v1/embeddings` |
  | RAG 回答模型 | 由管理員標記哪些模型可用於知識庫 |
  | 網站網域 | 設定值 `<代碼>.<BaseDomain>` |
  | 網站可見範圍 | 由擁有者選擇，Ymir 管理員一律可看 |

- **ADR 編號**：ADR-0015 已用於明確交付成果，所以網站託管改為 ADR-0016。
- **A2 做了什麼**（ADR-0012 階段 2，附註已寫入 ADR）：
  - **`src/Ymir.McpGateway`**：獨立服務。
    - 驗證 API 簽發的每人短期 token（HMAC；內容是使用者、允許的服務、期限，最長 1 小時）。
    - `/mcp/{服務}` 轉送到 `deploy/mcp/servers.json` 中的後端，並換成 gateway 自己保管的後端憑證。
    - 每人 rate limit、逾時。
    - 稽核：`mcp.tool.call` 只記工具名稱；另有 `mcp.access.denied`。
  - **存取清單** `vibemaker.mcp_server_access`：
    - 管理端點 `GET /api/admin/mcp-servers`、`PUT .../{name}/access`；
    - 「系統設定 → 平台 MCP 服務」卡；
    - 個人設定顯示可用的平台服務。
  - **執行時**：
    - `mcp.json` 寫入 gateway 位址與 `Authorization: Bearer ${YMIR_MCP_TOKEN}`，檔案裡沒有 token 值，平台項目優先；
    - token 經 `exec --env` 傳入；
    - 使用者沒有 `mcp` 能力時也載入 `builtin:mcp`，但只合併平台項目。
  - **部署**：`deploy/mcp/`（目錄、範例、env 範本、systemd unit、README）；CLAUDE.md 已更新。
- **驗證**：
  - `dotnet build`（0 警告）、`dotnet format --verify-no-changes`。
  - `dotnet test --solution`：664 / 664 通過，新增：
    - `McpGatewayTests` 6 個：真實 Pi 經 gateway 呼叫 echo；偽造 / 過期 token；未授權服務；rate limit；存取清單；後端只收到 gateway 憑證；`mcp.json` 不含 token。
    - `PlatformMcpTests` 13 個。
  - 前端 lint、133 個測試、build 都通過。
  - `e2e:mcp`（Fake MCP + gateway + Pi + Fake LLM）：管理員開放 → 成員看到 → Agent 回覆「echo: ping from agent」，全部通過。
- **未驗證、待使用者環境確認**：真實後端服務（EIP / MES）；正式主機上受限網路（`--internal`）連到 gateway 的路由。
- **其他**：PR #47 上「Agent runtime image」是 Docker Hub 回 500 造成的失敗，已重跑一次。
- **下一步**：R0（ADR-0014 RAG）。

---
### #012 · O2 完成：OneDrive 同步

> 👤 **Claude（AI）** · 🕒 2026-10-08 04:00 · `✅完成`

- **做了什麼**（ADR-0013 §4，指南 `docs/guides/onedrive.md`）：
  - **執行前**：`ExecutionRunner` 在 Agent 啟動前、持有使用者鎖時，從 OneDrive 下載雲端的變更，並送出「正在從 OneDrive 同步」。下載失敗只送狀態事件，Agent 照常使用本機檔案執行。
  - **執行後**：上傳工作寫進 `vibemaker.onedrive_sync_scopes`，服務重啟後會繼續。`OneDriveSyncWorker` 取得同一把使用者鎖後先下載、再上傳。失敗時依 1、5、15、60 分鐘重試，共 5 次；授權失效就停止，並請使用者重新連結。
  - **比對與衝突**：
    - 雲端以 eTag 比對，上傳帶 If-Match；本機以大小加修改時間比對。
    - 兩邊都改過時，原檔名保留雲端版本，本機版本另存「(OneDrive 衝突 時間)」副本，雲端和本機都會有兩份。
    - 不同步刪除。隱藏檔、node_modules、超過大小上限、名稱不符合 OneDrive 規則的檔案都略過。`deliverables/` 只上傳、不下載。大於 4 MB 的檔案用 upload session。
  - **檔案讀寫**：一律經 runtime 內的 reader / writer，Agent 拿不到 Graph token。writer 改成可以寫入工作目錄根部的檔案。
  - **API**：
    - `GET /api/conversations/{id}/onedrive`、`POST .../onedrive/sync`，未就緒時回 409 `ONEDRIVE_NOT_READY`。
    - 兩者都驗證擁有者，並加入授權矩陣。授權矩陣新增 `OwnerStatusOverrides`：擁有者沒有連結 OneDrive 時回 409，仍可證明請求本身正確。
  - **前端**：檔案面板新增「雲端保存狀態」（`app-onedrive-sync`），可以立即同步或重試，與 Agent 任務結果分開顯示。
  - **Fake Graph**：支援 conflictBehavior（fail / rename / replace）、upload session 的前置條件檢查、children 分頁。
- **驗證**：
  - `dotnet build`（0 警告）、`dotnet format --verify-no-changes`。
  - `dotnet test --solution`：645 / 645 通過。新增 `OneDriveSyncTests` 6 個：上傳與下載、衝突保留兩份、大檔 upload session 與專案資料夾、授權失效、未開放、不外洩 token。
  - 前端 lint、131 個測試、build 都通過。
  - `e2e:onedrive`（Fake OIDC + Fake Graph）：管理員開放 → 連結 → 設定資料夾 → 送訊息後檔案面板顯示「已同步到 OneDrive」→ 立即同步 → 解除連結，全部通過。
- **未驗證、待使用者環境確認**：真實 Entra 的 `Files.ReadWrite` 同意與 callback redirect URI、Microsoft Graph / OneDrive for Business 的實際行為（eTag 變動、upload session、節流）。
- **已知限制**：以大小加修改時間判斷本機變更。若換根資料夾後又切回原本的資料夾，兩邊已存在的同名檔案會各產生一份衝突副本。
- **下一步**：恢復 A2（MCP Gateway）。

---
### #011 · AppDashboard 側欄清單與漢堡收合

> 👤 **Codex（AI）** · 🕒 2026-10-07 17:15 · `✅完成`

依使用者指定的 [AppDashboard](https://bootstrapmade.com/content/demo/AppDashboard/) 調整側欄：一致的線條圖示與圖示底、46px 清單列、群組間距與標題分隔線、專案子清單縮排。頂部漢堡按鈕在桌面收合／展開側欄，使用 localStorage 記住狀態；手機使用抽屜，支援 Escape、背景遮罩、導航後關閉及鍵盤焦點循環。隱藏側欄設定 inert，避免鍵盤進入隱藏內容。既有重新命名、選項、主題及專案對話功能保留；目前專案的子清單也可手動收合。

驗證：前端 123 測試通過、lint 與 production build 通過；sidebar-flow.mjs 使用唯讀 API fixtures 驗證桌面收放／重整、群組間距、長文字、目前專案收合、手機抽屜／焦點／Escape／遮罩／導航與縮放、淺色／深色截圖。網頁映像更新至 localhost/ymir/web:sidebar-20261007，保留 50m 上傳與原代理設定。單一元件樣式警告門檻改為 7kB，硬上限維持 8kB，初始載入包門檻維持不變。

---
### #010 · 明確交付成果與工作檔分離

> 👤 **Codex（AI）** · 🕒 2026-10-07 · `✅完成`

依使用者核准計畫完成 [ADR-0015](../adr/0015-explicit-execution-deliverables.md)：每次執行獨立成果目錄，成功後登記資料庫；單檔直接下載，多檔提供成果 ZIP。工具與暫存隱藏，保留既有工作檔及附件。前端改用持久化成果紀錄，檔案面板預設成果，可切換專案檔案。Agent 映像預裝 pdftotext；README 同步架構。

驗證：dotnet build 零警告；單元測試 374 通過、5 略過；相關整合測試 21 通過、1 略過，新增成果邊界與 OpenAPI 檢查 10 通過；前端 123 測試通過、lint 與 production build 通過。Playwright artifacts-flow.mjs 使用 API fixtures 驗證摘要無卡片、單檔預覽／下載、多檔唯一 ZIP 與重整還原；真實下載 ZIP 內容、擁有者及 symlink 防護由整合測試覆蓋。未對真實模型發出測試請求。

本機部署：先備份資料庫再套用 migration；API、網頁及 Agent 映像更新，健康檢查正常。保留 workspace、agent-state 及舊網頁容器供回復；舊歷史成果不猜測。

---

### #009 · 多人、多 Agent 軟體專案協作計畫

> 👤 **Codex（AI）** · 🕒 2026-10-07 14:34 · `✅完成（計畫）`

- **需求**：使用者規劃多人多 Agent 協作：類似 Jira 的 Sprint／Task 清單、SA Agent 制定模組通訊與任務、開發 Agent 每輪讀取／更新專案留言板、QA Agent 依需求／SA 驗收，以及人用 UI 和 Agent 控制介面。
- **完成**：新增 [協作開發計畫](../planning/multi-agent-project-collaboration-plan.md)，並加入 [總開發規劃](../planning/development-plan.md)。涵蓋任務狀態／依賴、原子領取與租約／舊輪次拒寫、文件批准基準、專案留言／交接、版本化成果／QA／整合、成員與 Agent 權限、UI／API／MCP、持久化派工、隔離 Git 副本、M0～M5 階段與驗收。
- **架構差距**：目前專案屬單人，runtime 一人一個且 execution 依序執行；本計畫不宣稱已有多人並行。團隊授權、專用 sandbox／工作副本、同人多 Agent 並行需 M0 的新 ADR，MCP 工具需銜接 ADR-0012 A2。RAG SQLite 只作文件檢索，不作共享任務鎖與狀態來源。
- **驗證**：文件相對連結、git diff --check 與需求覆蓋檢查通過；只有 Markdown 修改，沒有新增程式／migration，不部署或啟動 Agent。完整 CI 通過後依專案流程合併 main。
- **後續**：尚未實作；先進行 M0 SA／ADR／隔離 spike。待確認 Git 平台、團隊規模、並行資源、模型／預算及是否自動合併，建議第一版人工批准與調度。

---

### #008 · AppDashboard 配色與主題切換

> 👤 **Codex（AI）** · 🕒 2026-10-07 14:24 · `✅完成`

- **需求**：新增淺色／深色／自動按鈕群組，並依使用者指定的 [AppDashboard](https://bootstrapmade.com/content/demo/AppDashboard/) 重新配色，包含對話區。
- **完成**：側欄底部顯示三個可鍵盤操作、具 aria-pressed 狀態的按鈕；瀏覽器 localStorage 保存偏好，登入頁即套用，儲存受限時仍可切換。自動模式持續跟隨系統；手動模式優先。配色參考官方 CSS 的鈷藍、灰白底、白色卡片與深色石板；統一側欄、管理頁、狀態提示、標頭、對話背景／訊息泡泡／輸入框，移除對話固定白底覆寫。深色藍色按鈕與使用者泡泡使用深色文字以維持對比。
- **驗證**：前端 lint、120 項測試及正式建置通過；新增四項偏好／儲存失敗測試與一項按鈕互動測試。Chrome 以隔離假 API 驗證系統深色、手動淺色／深色、重新整理保存、自動跟隨系統即時變更、深淺色對話與泡泡 CSS、390px 手機抽屜操作，並檢視兩種桌面截圖。git diff --check 通過。
- **交付**：依專案流程待完整 CI 通過才合併 main，更新網頁容器；沒有資料庫或 API 行為變更。偏好依瀏覽器保存，不跨電腦同步。

---

### #007 · 整合所有分支至 main

> 👤 **Codex（AI）** · 🕒 2026-10-07 13:48 · `✅完成`

- **需求與盤點**：抓取所有遠端分支後，只有 `claude/epic-franklin-k20b7r`（PR #36，對話附件）尚未納入 main；其餘本機與遠端分支已包含於 main。保留所有分支。
- **整合**：把最新版 main 合併到附件分支，保留附件上傳、圖片輸入與每次執行的擴充政策、受限網路設定；解決八個檔案的衝突，補齊 Pi harness 測試的檔案讀取器參數。EF snapshot 同時保留附件與使用者擴充授權資料表，重新產生前端 API 型別。納入遠端同時提交的 migration 重排：附件 migration 移到 UserExtensionGrants 之後，確保新建與升級資料庫的模型一致；附件進度移至 Sprint 6。
- **已驗證**：Release 全方案建置（零警告／錯誤）、dotnet format 檢查、git diff --check；Pi／附件／擴充權限單元測試 30＋29＋24 項通過；前端 API 型別產生、lint、115 項測試與正式建置通過。完整 Linux CI 必須通過，才以指定 head SHA 合併 PR #36。
- **範圍**：本次整理原始碼與 Git 分支，沒有重新部署或套用正式資料庫 migration。未測試真實付費視覺模型與公開網域上傳；沿用附件計畫的後續驗證事項。

---

### #006 · 對話可以附加檔案、圖片、影片給 Agent

> 👤 **Claude（AI）** · 🕒 2026-10-07 13:45 · `✅完成`

使用者回報：對話畫面沒辦法上傳檔案 / 圖片 / 影片給 Agent。規劃與設計決定寫在 [chat-attachments-plan.md](../planning/chat-attachments-plan.md)，PR [puremars2015/Ymir#36](https://github.com/puremars2015/Ymir/pull/36)。摘要：

- **使用方式**：輸入框左邊 📎 選檔，也可以拖放或貼上截圖；只附檔不打字也能送出。首頁、專案頁「直接開聊」也能附加（送出時才上傳）。單檔 50 MB、每則 10 個。
- **存放**：檔案寫進使用者 runtime 工作目錄的 `uploads/`（專案對話共用專案目錄），會出現在檔案面板、可下載；Remote runtime host 一樣適用。新表 `vibemaker.message_attachments`（migration `MessageAttachments`，排在 `UserExtensionGrants` 之後）。
- **Agent 怎麼拿到**：後端把附件路徑附加在送給 Agent 的內容（對話紀錄只顯示使用者文字），Agent 用工具讀取或處理（影片、PDF 等也一樣）。
- **圖片直接給模型**：模型設定 `VibeMaker__Models__N__SupportsImages=true` 時，PNG / JPEG / GIF / WebP 以 Pi RPC `images` 一併送出（Pi 會自動縮圖）；模型選單顯示「（可看圖片）」。**預設是 false**，要開請確認該模型（經 LiteLLM）支援視覺輸入。
- **安全**：上傳端點只接受自己的對話（加入授權矩陣）；附件只能綁自己、同對話、未送出的；檔名清理、路徑由伺服器產生；runtime 內以 `realpath` 拒絕 `uploads` 被換成指向外面的 symlink；圖片以檔頭判斷類型；縮圖只用 blob URL 的 `<img>`。

**驗證**（實際跑過）：
- 合併 main（ADR-0012 擴充政策、對外連線）後重跑：`dotnet build Ymir.slnx`、單元測試、整合測試（含真實 Pi 1.0.0 + Fake LLM：支援視覺的模型收到 1 張圖、不支援的不送；附件測試 10 個）。
- `cd web && npm run lint && npm test -- --watch=false && npm run build`。
- 新增 e2e `npm run e2e:attach`（合併前跑過）：首頁附加圖片開聊 → 模型收到圖片；對話中只附圖片 + 影片送出；重新整理後縮圖仍在、檔案面板列出 3 個上傳檔。
- 過程中發現並修正：Guid v7 前 8 碼是時間戳，同一分鐘內同名檔案會覆蓋 → 改用 id 末 8 碼；剛上傳的附件不再被列為「這次 Agent 產生的檔案」。

**未驗證、待使用者環境確認**：真實視覺模型（MiniMax 等）經 LiteLLM 收圖片的格式與效果；Cloudflare Tunnel 下上傳 50 MB 的實際表現。

**後續（未做）**：每人工作目錄容量配額、影片抽影格 / 音訊轉文字 / PDF 轉文字工具、上傳進度條。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

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

### #007 · O1 完成：OneDrive 連結與解除連結

> 👤 **Claude（AI）** · 🕒 2026-10-08 03:00 · `✅完成`

- **做了什麼**（ADR-0013 §1～§3、§6）：
  - **能力**：擴充政策新增 `oneDrive`（全域預設 + 每人覆寫，預設關閉）；管理介面、使用者頁、個人設定頁自動帶出。
  - **連結流程**：
    - `GET /api/connectors/onedrive/connect` 以授權碼 + PKCE 導向 Microsoft（scope `offline_access Files.ReadWrite User.Read`）；
    - state 與 verifier 存在 Data Protection 加密、只用一次、綁定使用者的短期 cookie；
    - `/callback` 驗證 state、使用者與過期時間；企業帳號必須連到同一個 oid（`mismatch` 會被拒絕）；
    - 導回只用站內路徑，結果以 `?onedrive=` 代碼表示，不轉送 IdP 的錯誤內容。
  - **憑證**：
    - 新資料表 `vibemaker.onedrive_connections`（migration `OneDriveConnections`）只存加密的 refresh token；
    - 換發時輪替；`invalid_grant` → `NeedsReauth`，重新連結後恢復；
    - access token 只放記憶體；token 不在任何回應、log、稽核。
  - **根資料夾**：`PUT /api/connectors/onedrive/root` 只接受名稱路徑，後端在使用者自己的 drive 逐層取得或建立；規則見 `OneDrivePaths`。
  - **Graph**：`GraphOneDriveClient` 在 429 / 503 時依 Retry-After 重試，錯誤只回摘要。
  - **其他**：
    - 稽核 `connector.onedrive.connect` / `disconnect` / `root.update`；
    - 端點加入授權矩陣（只作用在目前使用者）；
    - 解除連結不需要能力，被關閉的使用者也能移除自己的 token。
  - **測試替身**：
    - Fake OIDC 新增 refresh token（輪替、撤銷）、scope、OneDrive callback redirect URI，login_hint 改用最後一個值並接受 `帳號@網域`；
    - Fake Graph 放在同一個 server 的 `/graph/v1.0`：記憶體 drive、eTag / If-Match 412、upload session、注入 429。
  - **前端**：個人設定頁的「OneDrive」卡片：連結 / 重新連結 / 解除連結、同步資料夾、狀態與結果訊息。
- **驗證**：
  - `dotnet test --solution`：621 通過。
    - 新增整合測試 `OneDriveConnectionTests`：完整連結流程、token 不外洩、根資料夾建立、稽核、未開放回 403、帳號不符、偽造 state、撤銷後 NeedsReauth 與重新連結、refresh token 輪替、429 重試、不合法路徑；
    - 新增單元測試 `OneDriveRulesTests`。
  - OpenAPI 快照與 schema 已更新；前端 lint、125 個 Vitest、build 通過。
  - 新的 `npm run e2e:onedrive` 在沙箱用瀏覽器跑完：管理員開放 → 使用者連結（真的經過授權導向）→ 設定資料夾 → 解除連結。
- **與計畫的差異**：Fake Graph 沒有獨立成 `Ymir.Testing.FakeGraph` 專案，改放在 Fake OIDC 同一個 server，access token 由同一個 issuer 驗證，e2e 少啟動一個服務。
- **未驗證、待使用者環境確認**：真實 Entra 的 `Files.ReadWrite` 同意、Microsoft Graph 與 OneDrive for Business（沙箱連不到）。
- **下一步**：O2 同步（執行前下載、執行後上傳、衝突保留兩份、雲端保存狀態）。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #008 · CI 修正：`dotnet format` 檢查

> 👤 **Claude（AI）** · 🕒 2026-10-08 03:20 · `🔧修正`

- **問題**：#46（O1）的 `.NET build & test` 在 `dotnet format --verify-no-changes` 失敗。原因是 main 上其他人的變更留下的格式問題，O1 本身的程式沒有違規：
  - `ExecutionArtifact.cs` 一行有兩個初始化；
  - 3 個測試檔的 using 順序不對。
- **修正**：執行 `dotnet format Ymir.slnx`，只有空白與 using 排序的機械式變更，沒有改到任何行為。
- **驗證**：
  - 本機 `dotnet format --verify-no-changes` 0 錯誤；
  - `dotnet build` 0 警告；
  - `AuthorizationMatrixTests`、`RemoteRuntimeApiTests` 通過。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #009 · CI 修正：準備階段逾時誤報為執行環境錯誤

> 👤 **Claude（AI）** · 🕒 2026-10-08 03:40 · `🔧修正`

- **問題**：#46 重跑 CI 時，`ExecutionTimeoutTests` 失敗：預期 `AGENT_TIMEOUT`，實際是 `AGENT_RUNTIME_ERROR`。
  - log 顯示逾時發生在 main 新增的 `PrepareDeliveryAsync`（Agent 啟動前建立交付目錄）。
  - 這一步拋出的 `TaskCanceledException` 被通用 catch 當成執行環境錯誤。
  - 這是 main 既有的時序問題，不是 O1 的變更造成的；只在準備階段剛好碰上逾時時才會發生，本機跑了 3 次都通過。
- **修正**：`ExecutionRunner` 在通用 catch 之前，先處理 `runToken` 已取消的 `OperationCanceledException`，轉為 `AgentCancelled`，再由既有邏輯判斷是逾時（`AGENT_TIMEOUT`）還是使用者取消。
- **測試**：新增 `ExecutionTimeoutDuringPreparationTests`（約 6 毫秒的逾時，涵蓋 Agent 啟動前的各階段）。
- **驗證**：
  - `dotnet build`、`dotnet format --verify-no-changes` 0 錯誤；
  - `ExecutionTimeout*`、`ExecutionFlowTests` 共 9 個通過。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---
