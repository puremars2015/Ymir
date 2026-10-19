# Sprint 6 · Agent 擴充能力、RAG 知識庫、前端網站託管

[← 回看板列表](README.md) ・ 規則與格式見 [README](README.md#留言規則)

---

## 📌 置頂：狀態總覽

> 最後更新：2026-10-19 ・ 狀態：**🚧 進行中**（原計畫 A0～A2、O0～O2、R0～R2、H0～H2 已完成並整合回 main（#034）；P0 維持僅計畫；其餘為插單、使用者回報與維運項目）

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

### 原計畫項目（ADR-0012 擴充能力、OneDrive、RAG、網站託管）

| # | 工作項目 | 狀態 | 前置 / 待決定 |
|---|---|---|---|
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
| P0 | 多人、多 Agent 軟體專案協作計畫 | 📝 | 見 [#009](#009--多人多-agent-軟體專案協作計畫)；僅計畫，功能尚未實作，排程另定 |

### 插單、使用者回報與維運（新的在上）

| # | 工作項目 | 狀態 | 備註 |
|---|---|---|---|
| BOOT1 | 模型代理開機等待 bridge socket、持續退避重試 | 🚧 | 已恢復 Agent → LiteLLM HTTP 200；防復發修正與 8 項測試完成，正式 drop-in 安裝等待本機 sudo，完整 reboot 待維護時段，見 #096 |
| DEP5 | 更新外掛市集與 OneDrive Agent 工具，安裝私有 MCP Gateway | 🚧 | 使用者明確同意一併安裝 Gateway；部署前修補 MessagePack 間接相依安全漏洞，1058 Release tests 通過；待固定產物／Gateway 隔離與備份切換，見 #093 |
| DOC2 | 乾淨 Ubuntu 主機的正式安裝總指南 | ✅ | [Ubuntu 安裝總指南](../guides/ubuntu-install.md) 串接持久化資料庫／模型服務、rootless Agent、API、登入／Tunnel、驗收及更新回復；語法／連結／Quadlet 與 mock 檢查通過，未在乾淨 VM 實際安裝，見 #091 |
| DEP4 | 最新 main／遠端分支同步與重新上版 | ✅ | 348b64c 已經終端 sudo 授權完成部署；四個正式 unit、health、公開登入頁通過，本次無新 migration；後續僅測試競態修正與紀錄，見 #090 |
| GRP2 | 右側面板可拖曳調整寬度；群組聊天可上傳檔案給 @bot | 🔬 | ADR-0026 補充；後端、前端、整合測試與瀏覽器 e2e 通過；348b64c 已正式部署，真群組 Agent 操作待驗收，見 #089、#090 |
| DEP3 | 所有遠端分支核對、main 同步與新版正式部署 | ✅ | 7 個遠端分支均已在 main；修正 MakeTools idempotent SQL 後，經本機 sudo 授權完成 e7cf9f9 正式部署，四個 unit／health／公開登入頁通過；真登入與 Pi SSE 待人工驗收，見 #085 |
| TOOL1 | 左側「小工具」：/make 小工具自動發布，在 Ymir 內以 iframe 開啟 | 🔬 | [ADR-0029](../adr/0029-make-tools-sidebar.md)；後端、前端、測試與瀏覽器 e2e 通過；正式站需部署 SiteHost 與網站網域並套 VibeMaker migration `MakeTools`，見 #083、#084 |
| CONN1 | 個人「連接與工具」：OneDrive 與 HTTP MCP 註冊管理 | 🔬 | [ADR-0028](../adr/0028-personal-connections-and-tools.md)；入口、加密註冊／測試／啟停／移除、權限與群組隔離、桌面／手機 E2E 通過；尚未正式部署，見 #081 |
| USR1 | 管理 → 使用者：改名、變更角色、刪除帳號，新增訪客角色 | 🔬 | [ADR-0027](../adr/0027-admin-user-management-and-guest-role.md)；後端、前端與測試通過；未做瀏覽器 e2e，正式站需套 Platform migration `UserAdminManagement`，見 #080 |
| ARCH2 | 架構文件標記正式／舊容器及主機程序 | ✅ | 7 個固定容器＋動態 Agent 的角色清單，root／itadmin／ymir 分區、舊容器與實際模型代理路徑，見 #074 |
| RES1 | 管理員 CPU／RAM／磁碟、Ymir container／image 監控與保留期限／低資源自動回收 | 🔬 | [ADR-0025](../adr/0025-ymir-resource-monitoring-and-reclamation.md) 已採納並實作（維護服務、鎖、API、系統資源頁、資源不足告警）；對照 ADR 逐項檢查完畢；正式主機安裝與啟用待使用者授權，見 #074、#088 |
| GRP1 | 群組聊天與 @bot（每個群組一個 Agent container） | 🔬 | [ADR-0026](../adr/0026-group-chat-and-group-agent.md)；後端、前端、測試與瀏覽器驗證通過；3855dd6 已部署，真 Pi SSE／群組操作待人工驗收，見 #076、#079 |
| DM1 | 使用者之間的一對一訊息、右側成員面板（[ADR-0023](../adr/0023-user-direct-messages.md)） | ✅ | 使用者 2026-10-14 要求（簡易版 Teams）；SSE 即時、未讀／已讀、窄螢幕抽屜；分支 `claude/hopeful-ptolemy-4rodpw`，見 #064 |
| G1 | **插單** Google 帳號 SSO 登入（[ADR-0024](../adr/0024-google-account-sso.md)） | ✅ | 部署設定白名單（`hd` 網域 / email）必填；實際連 Google 待使用者環境確認，見 #065 |
| O3～O5 | 讓 Agent 處理使用者指定的 OneDrive 檔案（[ADR-0022](../adr/0022-onedrive-linked-files.md)） | ✅ | 已關閉：使用者 2026-10-16 改為外掛市集模式，手動連結已被 ADR-0030 取代並移除（資料表保留）；O4／O5 不再實作，見 PLG1、#092 |
| PLG1 | 外掛市集與 OneDrive Agent 工具（[ADR-0030](../adr/0030-plugin-marketplace-and-onedrive-agent-tools.md)） | 🔬 | `/plugins` 市集、使用者外掛開關、Agent 經 gateway 讀寫自己的 OneDrive（含刪除到回收筒）；沙箱以 Fake Graph + 真 Pi 驗證通過，真 Graph 與正式 gateway 位址待使用者環境確認，見 #092 |
| L5 | 每人每模型用量（[ADR-0021](../adr/0021-per-user-model-usage.md)） | ✅ | 已合併回 main（使用者 2026-10-13 要求）；管理 → 用量「依模型」卡片，模型名稱帶 provider；token／費用仍讀 LiteLLM、Ymir 不保存；見 #058、#059 |
| RAG2 | `/make` 新增「建立 RAG 知識庫」引導主題 | ✅ | 種子主題（管理員可調整／停用），引導到專案頁知識庫（每專案一份 SQLite），見 #061 |
| UPD1 | main 4b8a3af 正式更新產物與現況核對（歷史建置記錄） | 🔬 | API／RuntimeHost／511 單元測試／396 checksums 通過；保留當時未部署與權限受阻原文，原部署卡關已於 2026-10-14 13:00 部署解除，見 #062 |
| DEP2 | 正式站更新至 main 4d7d4c0、`ymir-sql` 改掛持久化 volume | ✅ | API health／登入頁通過；外部網域登入與 SSE 待使用者以瀏覽器確認，見 #068 |
| FIX413 | 長對話一直顯示「模型發生錯誤」：LiteLLM proxy 的 nginx 1 MB 上限 | ✅ | 設定已納入版控並寫入安裝手冊；正式主機重啟 proxy 後使用者確認原對話恢復，見 #069 |
| UI-IND | 執行中且尚未顯示回答時一定顯示「正在思考中」/「正在工作中」 | 🔬 | 已部署（0e16583）；待使用者在正式站確認顯示，見 #070 |
| CRASH1 | 正式 API 在 .NET GC 執行緒 fail-fast 重啟（Cloudflare 回 origin 錯誤） | 🔬 | 10/13 起 8 次；已改 workstation GC 並開啟 crash report／dump，觀察中，見 #071 |
| IMG1 | container image 回收機制（腳本 + 每週 timer） | 🔬 | 已 commit；正式主機 dry run 可回收約 3.1 GB；timer 待使用者決定是否安裝，見 #072 |
| DOC1 | 精簡 README 專案入口 | ✅ | 保留介紹／功能／技術／開發／文件導覽，移除歷史主機快照與實作流水帳，見 #056 |
| ARCH1 | 現況系統架構圖與專案導覽 | ✅ | 根目錄 architecture.md、五張 Mermaid 圖與 README 入口；區分正式部署／可選功能／未驗證項目，見 #058 |
| LD1 | Ubuntu 統一 Linux 正式架構 | 🔬 | 正式 API／nginx／Tunnel 已啟動，本機通過；公開探測回 Cloudflare 403，登入與 SSE 待驗證，見 #039 |
| D2 | RuntimeHost cgroup exec 與 socket 掛載修復 | 🔬 | 已套用，Agent exec／stdin／env 與 API Healthy 通過；真實對話待使用者重試，見 #047 |
| D3 | 上傳寫檔 wrapper 參數展開修復 | 🔬 | 已熱更新，實際 Agent 原寫檔腳本／API 健康通過；原檔重傳待使用者確認，見 #050 |
| L4 | 六模型選單：移除 M2、新增 M3／M2.7／四個 OpenRouter 模型 | 🔬 | 已部署、六路由及服務健康通過；管理者勾選開放與真模型推論待驗證，見 #041 |
| L6 | 開放模型標示 provider（管理端顯示、使用者不顯示） | ✅ | `VibeMaker__Models__N__Provider`；正式主機需重跑 `apply-chatgpt-linux.sh` 或手動補 Provider 才會生效；同名模型加 provider 區分（使用者 2026-10-13 決定）；見 #057 |
| OAUTH1 | LiteLLM ChatGPT 訂閱 OAuth 接入 | 🔬 | Sol／Luna／Astra 已部署，原六路由保留、API／登入頁健康；待管理員開放與真實推論，見 #054 |
| B1 | 公司識別 favicon 超過約 6 KB 上傳回 500 | ✅ | `SystemSetting.ValueMaxLength` 放寬到 512K 字（欄位已是 nvarchar(max)），補 100 KB／256 KB 測試；見 #036 |
| B2 | `OneDriveSyncTests` 撤銷授權測試偶發 409 | ✅ | 測試競態：執行後的背景上傳晚於撤銷才跑；改為先等上傳完成，產品程式未變；見 #036 |
| D1 | ADR 編號重複與看板置頂區整理 | ✅ | 模型開放的 ADR 由 0016 改為 [ADR-0020](../adr/0020-admin-model-access.md)、補齊 ADR 索引；置頂區拆成原計畫／插單兩張表；見 #036 |
| W11 | Windows 本機 HTTPS 與聊天 runtime 恢復 | ✅ | 本機登入頁、假模型聊天／SSE 通過，啟動腳本不啟動 Tunnel；見 #035 |
| G1 | 分支整合與 main／rebase 開發流程 | ✅ | 使用者 2026-10-12 改為直接在 main 開發；見 #034，取代 I1 的保留分支要求 |
| I1 | main 與擴充能力整合 | ✅ | 已整合回 main；先前測試見 #028，2026-10-12 分支清理見 #034 |
| W10 | 「正在工作中」提示不閃爍 | ✅ | 連續呼叫工具時，工具之間的空檔仍維持提示，Agent 輸出文字或執行終止才消失；見 #031 |
| W9 | 對話工具執行提示簡化 | ✅ | 工具／終端只顯示「正在工作中......」、清除完成／失敗／取消狀態；見 #027 |
| L3 | 個別使用者模型權限 | ✅ | 系統預設＋逐模型允許／禁止、選單／execution／virtual key 一致；ADR-0019，見 #026 |
| M2 | `/help` 系統用途與指令說明 | ✅ | Agent 說明功能、兩個指令與開放主題，斜線提示；見 #025 |
| M1 | `/make` 簡報與公司公告 Word 引導 | ✅ | 兩個可管理主題、唯讀公告模板及文件工具映像；見 #024 |
| F1 | 明確交付成果與工作檔分離（ADR-0015） | ✅ | 成功執行登記成果、單檔／ZIP、成果與專案檔案分頁；見 #010 |
| L2 | 管理員開放模型與預設模型 | ✅ | 持久設定、選單／執行／virtual key 一致授權；ADR-0020（原誤編為 ADR-0016，2026-10-13 改號，見 #036），見 #015 |
| L1 | LiteLLM OpenRouter 模型接入 | ✅ | Sonnet 5.5、GPT-6.1 Sol、GPT-6 Luna；virtual key、用量／費用與路由驗證；見 #012 |
| W7 | 圖示開啟模型與思考深度面板 | ✅ | execution 保存深度、Pi／LiteLLM 實際參數、手機與鍵盤操作；ADR-0017，見 #016 |
| W6 | 模型名稱與輸入能力圖示 | ✅ | 移除 provider 後綴、文字／圖片 SVG、自訂可存取選單；見 #014 |
| W5 | 手機對話輸入區靠近底部 | ✅ | 新／既有對話、鍵盤與安全區、訊息獨立捲動；見 #013 |
| W8 | PWA：手機可「加入主畫面」安裝 | ✅ | 見 #018；iOS / Android 實機安裝**待使用者環境確認** |
| W4 | AppDashboard 側欄清單與漢堡收合 | ✅ | 圖示、列高與群組分隔線；桌面記住收合，手機抽屜；見 #011 |
| W3 | AppDashboard 配色與淺色／深色／自動切換 | ✅ | 見 [#008](#008--appdashboard-配色與主題切換) |
| W2 | Ubuntu 既有 Docker 部署啟動檔 `start-ymir.sh` | ✅ | 見 [#005](#005--ubuntu-既有-docker-部署啟動檔)；語法與隔離模擬通過，實機待驗證 |
| W1 | Windows 既有部署一鍵啟動檔 `start-ymir.ps1` | ✅ | 見 [#004](#004--windows-既有部署一鍵啟動檔)；已在目前主機驗證 |
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

### #096 · 模型連線中斷復原與開機競態修正

> 👤 **Claude（AI）** · 🕒 2026-10-19 · `🚧進度`

- **實機根因**：10/19 三次對話在 Pi 結束時記錄 `Connection error`，LiteLLM liveliness HTTP 200、沒有對應模型請求。經使用者在本機終端輸入 sudo 密碼，查到 `ymir` 的 proxy 在 10/17 開機時因 `/run/ymir-litellm` 尚未建立而失敗五次並觸發 start limit；system bridge 稍後才啟動。已單獨 start proxy，Agent 內存取模型 liveliness HTTP 200；未重啟 API／資料庫，真模型回答仍待使用者重送確認。
- **修正**：`20d86ae` 在 Quadlet 與既有主機 drop-in 加入 socket 等待（60 秒）、10 秒退避與不限次重試，避免 system／user manager 無法透過 `After=` 排序的競態。installer 檢查 linger、bridge enabled／active，先備份，再 reload user manager 並 start（已在執行時不重啟）；不覆蓋 image／network／secret、不重建其他容器。已開啟本機 sudo 安裝視窗，正式安裝結果尚待讀回；沒有自動 reboot。
- **驗證**：`python3 deploy/runtime-host/litellm-proxy/test-boot.py` 8 項通過（目錄缺失、普通檔案拒絕、socket 就緒、延遲就緒、逾時後恢復、範本／drop-in 一致、重試政策、Bash 語法）；Podman Quadlet `--user --dryrun` 保留等待與重試參數；`git diff --check` 通過。CI 加入此測試；本次沒有改應用程式，未重跑 .NET／Angular 本機全套。冷開機端到端驗收未執行，需另排維護時段。
- **部署簡化建議（非架構變更）**：目前先維持單機 Podman／systemd，將基礎服務、應用服務、每人 Agent 分層及增量更新，以統一入口執行差異計畫／migration gate／備份／更新／健康檢查／局部回復；不因一般平台更新重建 DB、模型代理或所有 Agent。k8s 不會自動解決資料遷移與隔離設計；多節點／高可用需求成立後再以新 ADR 評估。本輪未實作部署平台重構或安裝 k8s。

---

### #095 · 開發計畫文件對齊現況、未完成項目盤點

> 👤 **Claude（AI）** · 🕒 2026-10-17 · `📝文件`

- **盤點結果**：路線圖 Sprint 0～6 全部完成；唯一尚未實作的是 P0（多人、多 Agent 軟體專案協作，需先寫新 ADR）。其餘 🔬／🚧 項目（DEP5、PLG1、GRP1／GRP2、TOOL1、CONN1、USR1、RES1 等）功能已完成，缺使用者環境驗收或正式部署。
- **文件更新**：`docs/planning/development-plan.md` 的 OneDrive、RAG、網站託管、後台設定原寫「僅記錄、尚未實作」，已改為「已實作」並附 ADR 連結；多人多 Agent 協作標明為唯一尚未實作。
- **Git 狀態**：遠端 `main` 歷史已改寫（與本機舊 `main` 無共同祖先、遠端為淺層 50 個提交），本機舊 `main` 的 90 個提交內容已被遠端涵蓋，沒有被刪除的檔案，因此未推送也未 force push。
- **驗證**：只改 Markdown，未跑 build／test；ADR 連結檔名已逐一核對存在。

---

### #094 · 手機新對話頁下滑時畫面反向跑（iPhone Chrome）

> 👤 **Claude Code（AI）** · 🕒 2026-10-16 · `✅完成`

- **回報**：iPhone Chrome、鍵盤未開、尚未開始對話時，往下滑畫面反而往上跑；對話開始後正常。
- **推測原因**：新對話頁沒有可捲動內容，iOS 下拉會橡皮筋回彈，`visualViewport.offsetTop` 暫時為負；`Shell.syncViewport` 把它直接寫進 `--viewport-top`，固定定位的外框被反向推走。對話頁訊息區自己可捲動，不會觸發。
- **修正**：`--viewport-top` 只取 `Math.max(0, offsetTop)`（鍵盤造成的位移仍為正值，維持原行為）；`html, body` 加 `overscroll-behavior: none`。
- **驗證**：前端 lint、Vitest 219/219、build 通過；Chromium Pixel 7 模擬確認對話頁觸控捲動方向本來就正確。**iOS 橡皮筋行為無法在雲端沙箱重現，未在真機驗證、待使用者在 iPhone 確認。**若仍有問題請回報，下一步是讓新對話頁的外框在手機上改為 `touch-action: none` 或改版面避免空白區可被拖動。

### #093 · 外掛市集更新部署與私有 MCP Gateway 準備

> 👤 **GPT（AI）** · 🕒 2026-10-16 · `🚧進度`

使用者要求重新 pull 並部署。乾淨 main fetch／rebase 到 `4a04c8e`（外掛市集、ADR-0030 OneDrive Agent 工具，所有已取得遠端分支均已包含），正式 RuntimeHost 仍為 348b64c。差異僅需新套 VibeMaker `20261016081348_UserPluginSettings`；舊 OneDrive 手動連結資料表保留，原同步資料夾保留。正式主機未安裝 MCP Gateway，使用者在選項中明確選擇「一併安裝 Gateway」，不是只更新市集 UI；不替使用者開啟 OneDrive 外掛或增加 Microsoft 權限。

最新 commit 的 CI [38876779888](https://github.com/puremars2015/Ymir/actions/runs/38876779888) 在 Restore 失敗，不是暫時性下載：NU1902 指出 Aspire 間接依賴 MessagePack 2.5.302 受 [GHSA-qhrr-8q5h-9q3h](https://github.com/advisories/GHSA-qhrr-8q5h-9q3h) 影響。對照公告後在中央相依釘選同主要版本的修補版 2.5.303；未停用 NuGet audit、未把漏洞警告降級。restore 成功，project.assets 確認 MessagePack／Annotations 均為 2.5.303。完整 Release solution tests 1058 全通過、0 failed／0 skipped（固定 Pi 1.0.0、Fake 服務與專用暫時 SQL container），format／diff check 通過。第一次暫時 SQL readiness 與清理命令逾時，主機讀回仍有約 5.8 GiB available RAM；未據此推斷 OOM，之後只清理本輪具名測試容器並順序重跑成功，正式 SQL 未用於測試。

計畫：重新固定安全修補後的 commit／API image，publish RuntimeHost 與 MCP Gateway，審查單一 plugin migration；Gateway 專用非 root 帳號、loopback 5310、僅 audit INSERT／id SELECT 的獨立 SQL principal，Agent 透過兩個私有網路上的 proxy／Unix bridge 連接，不新增公開 port，不交付 API／DB／Graph 憑證給 Agent。保留既有 ENV、模型路由、登入與 mount 隔離；只新增明確審查的 MCP 設定與唯讀 catalog mount。切換前先排空工作並備份 SQL／keyring／workspaces／部署 secret，失敗回復原程式與配置，保留 additive migration／備份。實機 Gateway 路由、稽核權限及權限拒絕仍待部署器驗證；真 Graph／使用者授權後的讀寫刪除、真 Pi SSE 待人工驗收。

本留言記錄準備與已完成的驗證，不代表已完成正式切換；正式授權視窗需在產物與安全 gate 完成後才開啟。

---

### #092 · 外掛市集與 OneDrive Agent 工具（ADR-0030）

> 👤 **Claude Code（AI）** · 🕒 2026-10-16 17:00 · `🔬驗證`

- **需求**：使用者回饋 ADR-0022 的「在檔案面板手動挑 OneDrive 項目」不是要的效果；要比照 ChatGPT 外掛：市集 → 找到 OneDrive → 安裝並啟用 → Agent 需要時自己讀寫 OneDrive。使用者決定：Agent 權限讀＋寫（含刪除）、移除 ADR-0022 手動連結（資料表保留）、ADR-0013 同步資料夾保留（不同功能）、市集第一版放 OneDrive＋平台 MCP＋個人 MCP。
- **ADR-0030**：取代 ADR-0022 手動連結、修訂 ADR-0028 §7 與 ADR-0012 B。OneDrive 工具由 **API** 提供（refresh token 只在 API），經既有 MCP gateway 轉送：gateway 的內建服務 `/mcp/onedrive`（`McpGateway:ApiUrl`）→ API `/internal/mcp/onedrive`，附上由簽章金鑰衍生的後端憑證與 Agent 的 token；token 新增 execution id，API 每次呼叫都重查 execution 執行中、使用者、能力、外掛開關與連線。公開網域的 `/internal/*` 由 public edge 回 404，帶 `Cf-Connecting-Ip` 也拒絕。
- **後端**：`vibemaker.user_plugin_settings`（migration `UserPluginSettings`）、`PluginService`、`GET /api/plugins`、`PUT /api/plugins/{key}`（稽核 `plugin.enable/disable`）；`OneDriveAgentTools` 七個工具 search / list_folder / download（→ 工作目錄 `onedrive/`）/ upload（預設不覆蓋）/ create_folder / move / delete（回收筒），只操作自己的 drive，寫入類稽核 `onedrive.agent.*`（target 為 item id）；Graph client 與 Fake Graph 新增 move / delete。`PlatformMcpService.PrepareRunAsync` 排除使用者停用的平台服務並在啟用時加入 `onedrive`（群組執行不帶）。`servers.json` 不得使用 `onedrive` 名稱。
- **移除**：`OneDriveLinks`、`/api/conversations/{id}/onedrive/links*`、`/api/connectors/onedrive/browse`、`/search`、檔案面板「OneDrive 連結檔案」、設定頁「瀏覽整個自己的 OneDrive」與執行前的連結下載；`onedrive/` 仍是同步資料夾排除的頂層目錄。
- **前端**：新頁面 `/plugins`（左下「外掛」）：OneDrive 卡片（安裝 = 連結帳號、「讓 Agent 使用」開關會先確認風險、帳號與同步資料夾）、平台 MCP 卡片、自訂外掛（個人 MCP）與能力摘要。個人設定只剩 System Prompt、變更密碼；`/settings/connections`、`/settings/tools` 導向 `/plugins`，OAuth 回跳改到 `/plugins`。
- **驗證**：`dotnet build` 0 警告；單元測試 717 通過；整合測試新增 `OneDrivePluginTests`（市集預設與能力、平台服務停用、七個工具在 Fake Graph 的讀寫與不覆蓋、後端憑證 / token / execution / 群組 / 已結束 / 停用外掛 / Tunnel 標頭拒絕、跨使用者 item 拒絕）與 gateway 內建轉送測試、public edge `/internal` 測試，連同授權矩陣、OpenAPI 快照、既有 OneDrive 測試通過；全套 `dotnet test --solution Ymir.slnx`（含真 Pi 1.0.0）1055 通過、0 失敗、0 略過，`dotnet format --verify-no-changes` 通過；之後加上 public edge `/internal` 保護，PublicEdge＋OpenAPI 34 項重跑通過。前端 lint、225 Vitest、build 通過。沙箱實際啟動 Fake LLM＋Fake OIDC＋Fake MCP＋API（Pi harness）＋gateway（ApiUrl），`e2e:onedrive`（含真 Pi 經 gateway 呼叫 `mcp__onedrive__search`）、`e2e:mcp`、`e2e:connections` 通過，手機寬度無水平捲動。
- **部署注意**：先備份再套用 `UserPluginSettings` migration；API、前端與 MCP gateway 一起更新，gateway 加上 `McpGateway__ApiUrl`（gateway 連得到的 API 位址）。
- **未驗證、待使用者環境確認**：真實 Microsoft Graph 的 move（PATCH）與 delete、正式主機 gateway → API 的位址與受限網路路由。
- **同步**：rebase 到含 #091（Ubuntu 安裝總指南，只改文件）的 main；看板編號改為 #092。安裝指南的 OneDrive／平台 MCP 列同步改成 ADR-0030 的說明（需使用者自己啟用、gateway 需 `McpGateway__ApiUrl`）。整合的遠端提交只有 Markdown，程式驗證結果不變。
- **CI**：[38874737214](https://github.com/puremars2015/Ymir/actions/runs/38874737214) 第一次在 `.NET build & test` 的 Restore 步驟（9 秒、尚未執行任何 build／測試）失敗，本次提交未改 csproj／套件、本機 restore 正常，判斷為 NuGet 暫時性錯誤，重跑一次後 Restore、format、Release build、測試、Angular、Agent image 全部通過。

---

### #091 · 從乾淨 Ubuntu 主機開始的正式安裝總指南

> 👤 **GPT（AI）** · 🕒 2026-10-16 16:00 CST · `✅文件`

使用者要求把分散的安裝文件串成總指南。原工作目錄目前乾淨，回到 main 並 fetch／rebase 後新增 `docs/guides/ubuntu-install.md`，在 README 文件導覽加入醒目入口；不再使用先前個人 MCP 的一次性 worktree，也未建立新分支／PR。

指南以 Ubuntu 24.04 x86-64、systemd 255+、Podman 4.9+ 為新主機基線：rootful API／SQL／LiteLLM＋PostgreSQL，專用 ymir rootless Agent／模型 proxy／Tunnel。提供具持久化 volume、loopback port、固定 image ID 與開機依賴的 6 份 Quadlet 參考區塊，不要求混裝 Docker daemon；區分新主機 Tunnel 直連 API 與既有 nginx 入口，明確禁止把現有主機修復／切換腳本當首次安裝器。

涵蓋 subuid／subgid 不重疊、rootless user manager／cwd、Unix bridge／受限網路、root-owned scope wrapper（不放寬 cgroup／容器權限）、SDK／runtime、固定版本發布、最小權限 API DB login、兩模組 migration、stdin 建立首次管理員、Entra／Google 與 Tunnel、真 Pi／附件／重開機／還原驗收、可選 Connector／MCP／RAG／SiteHost／Maintenance，以及排錯順序。migration helper 明確設定 filtered index 所需 SET options、移除 UTF-8 BOM，第一模組失敗不執行第二個；密碼不放程序參數／shell trace／Git。更新流程保留 paired DB＋keyring＋workspace＋模型 DB／salt 備份及舊版，區分相容程式回復與經批准的資料還原，不提供盲目 down migration／prune／覆蓋備份指令。

**實際驗證**：46 個本機 Markdown 連結／標題錨點通過；22 段 Bash 以 `bash -n` 檢查，內嵌 Python AST 檢查通過；本機 Podman 5.7.0 Quadlet `--dryrun` 解析 6 份新配置並產生 SQL／PostgreSQL／LiteLLM service，無 unsupported key／conversion error。migration helper 在暫存目錄以假的 sudo sink 驗證 SET options、BOM 移除與第一個失敗時第二個不執行；未呼叫真 sudo／SQL／Podman 安裝操作。`git diff --check` 通過。

**邊界**：只改 Markdown，未重新跑應用程式 build／單元／整合測試；推送後仍追蹤 main CI。沒有新 VM／乾淨 Ubuntu 的端到端安裝、真實 SSO／Graph／模型或 reboot／restore 演練，指南已標註待目標主機驗收。未安裝套件、建立帳號、修改正式服務／secret／資料庫／網路或重新部署。

---

### #090 · 最新進度同步與 348b64c 正式上版準備

> 👤 **OpenCode（AI）** · 🕒 2026-10-16 10:25 · `🔬驗證`

- **同步範圍**：使用者要求 pull／merge 最新並上版。先前合併群組附件後，工作暫停期間遠端已將相同功能重新整合回 main，另有資源告警。重新 fetch／rebase 時保留新版 #088／#089 看板紀錄、排除已存在的重複程式提交；目前所有已取得的遠端分支皆包含於 main，無須額外合併或刪除分支。
- **固定程式目標**：`348b64cdb7cc024b1b8dee050e7ba83a87981b3e`；CI [38770970038](https://github.com/puremars2015/Ymir/actions/runs/38770970038) 全數成功。包含個人設定四分頁、管理使用者設定頁、群組 @bot 附件、可調整右側面板，以及系統資源不足告警。與上一版 e7cf9f9 比對，無 migration 檔案異動，不改正式部署 secret／DNS／SiteHost／維護服務啟用狀態。
- **已驗證**：先前整合版 659cfa8（與目標後端程式相同）本機 build、712 單元測試、format 通過；與 348b64c 的差異僅文件及資源告警前端。新版 API image 重新建置中，完整整合測試使用獨立 SQL port 14349，未使用正式資料庫。主機部署器改成要求完整 migration history 與現況完全相同，本次不執行 migration，仍保留 SQL／金鑰／workspace 備份、精確 image／隔離／ENV 核對及失敗回復。
- **正式切換待授權**：主機專用部署入口 `/home/itadmin/ymir-production-install/update-348b64c/authorize.sh`；產物驗證完成後以終端視窗請使用者輸入 sudo 密碼。本次不宣稱已修復先前 Cloudflare／OneDrive 同步期間的串流問題；真登入、Pi SSE、Graph 與小工具網站網域仍需驗收／配置。切換前 API NRestarts 已為 4，保留供追蹤。
- **產物與本機整合驗證完成**：API image `c5ec456b16e9cdd0e09267cc553b1cd9089dd0c7fb590a63c37cd00749b83264` 與 402 個固定產物 SHA-256 驗證通過，14 個部署器檢查通過；獨立 SQL 完整整合測試 343 通過、0 失敗／略過。第一次終端 sudo 提示因未輸入密碼而逾時，未開始部署；使用者要求繼續後重新開啟授權視窗。
- **CI 競態處理**：僅文件提交 `60f99d3` 的 CI [38840114142](https://github.com/puremars2015/Ymir/actions/runs/38840114142) 有 1 個失敗：`RevokedAuthorization_StopsSync_AndKeepsLocalFiles` 固定期待 202，但 `RequestSyncAsync` 排程後再讀狀態時 worker 可能已把撤銷授權標成 NeedsReauth，因此合法回 409。測試改為接受 202 或經錯誤碼驗證的 `ONEDRIVE_NOT_READY` 409，仍要求背景同步 Failed、需要重新連結、本機檔案保留、重試 409、停用同步後 execution 正常；未修改正式程式、未跳過或停用測試。修正後 build、format、712 單元與隔離 SQL 全套 343 整合測試全數通過、無略過，固定正式程式目標保持 348b64c。
- **正式部署成功**：使用者在重開的終端完成 sudo 授權，部署結果 success／狀態碼 0。備份 `/var/backups/ymir/348b64c` 已通過 SQL COPY_ONLY＋CHECKSUM／RESTORE VERIFYONLY、金鑰／檔案／設定 tar compare 與 SHA-256；本次 migration history 保持 Platform 8／VibeMaker 20，不執行 migration。API 精確 image 為上述 c5ec456b…，RuntimeHost `/opt/ymir/releases/348b64c/runtime-host`，有效 ENV、mounts 與安全設定保留，未回復。
- **部署後獨立驗證**：四個正式 unit 均 active／running、NRestarts 0、ExecMainStatus 0；RuntimeHost 工作目錄正確、本機 health Healthy、公開 `/login` HTTP 200。本次後續提交只含測試與紀錄，不需要再次更新正式程式；未宣稱真登入／Pi SSE／OneDrive 或先前 Cloudflare 串流問題已驗收。

---

### #089 · 右側面板可調整寬度、群組附件給 @bot

> 👤 **Claude（AI）** · 🕒 2026-10-16 00:10 · `🔬驗證`

- **需求**：使用者要求調整右側聊天區塊的 UI / UX：(1) 成員與群組的右側面板可以拖動放寬或收窄；(2) 群組聊天也要能上傳檔案給 AI Agent。
- **面板寬度**：新元件 `app-panel-resizer`（面板左緣、寬 0，以偽元素擴大可拖曳範圍），拖曳、聚焦後左右鍵 / Home / End 調整、雙擊還原預設 18rem；寬度（256～640 px）記在 `localStorage` 的 `ymir.peopleWidth`，CSS 再以 `100vw - 40rem` 限制，保留中間對話區。窄螢幕的抽屜不變。純函式在 `panel-width.ts` 並有 Vitest。
- **群組附件**（ADR-0026 補充）：`POST /api/chat-groups/{id}/attachments`（成員才可，非成員 404）經 `AttachmentService.UploadForGroupAsync` 寫進群組 Agent 對話的 `uploads/`（runtime 擁有者 = 群組 id，與 @bot 共用 `GroupConversations.GetOrCreateAsync`）。`SendGroupMessageRequest.AttachmentIds` 只能隨 @bot 送出、只能綁定自己上傳且未送出的附件；送出前先檢查，群組訊息本文附上「📎 檔名」。綁定後與個人附件相同（路徑附加到 prompt、圖片經 Pi `images`）。前端：迴紋針按鈕、拖放、貼上；有附件時自動補 `@bot`，沒有輸入問題時用預設說明；依序上傳後才送出。上傳的暫存檔處理抽成 `AttachmentEndpoints.ReceiveAsync` 共用。
- **驗證（實際執行）**：`dotnet build` 0 警告、`dotnet format --verify-no-changes` 通過；單元測試 712 通過；整合測試 `ChatGroupTests`（新增群組附件：非成員 404、檔案在群組 workspace、一般訊息帶附件 400、其他成員不能用別人的附件、Agent 收到路徑、不能重複使用）、`AttachmentTests`、`AuthorizationMatrixTests`、`OpenApiSnapshotTests` 共 20 項通過；OpenAPI 快照與 `schema.ts` 已更新。前端 lint、`ng test`（44 檔 225 項）、build 通過。瀏覽器 e2e `e2e:groups`（`group-chat-flow.mjs` 新增附件與面板寬度步驟：拖曳變寬、重新整理後記住、Home 縮到最小、雙擊還原）全數通過並截圖確認。整合測試全套未重跑。
- **分支**：本 session 指定在 `claude/trusting-ritchie-al9xjk` 開發並推送（與 CLAUDE.md「直接在 main」規則不同，依 session 指示），使用者要求整合回 main：rebase 到最新 main（含 #087、#088）後重新驗證並推送 main。main CI [38767849702](https://github.com/puremars2015/Ymir/actions/runs/38767849702)（`958e947`）.NET、Angular、Agent image 全數通過。
- **部署**：無 migration；正式站需重建 API image（含 Angular）才會生效。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #088 · RES1 對照 ADR-0025 補齊：資源不足告警

> 👤 **Claude（AI）** · 🕒 2026-10-15 23:59 · `🔬驗證`

- **需求**：使用者要求依 #073／ADR-0025 把 RES1 開發處理好。主體已在 #074 實作，本回合逐項對照 ADR §1～§4 與「實作與驗證順序」檢查程式碼（`Classifier` 保護規則、`MaintenanceService` 回收／重新確認／鎖、`PressureTracker` 遲滯、API、系統資源頁）。
- **找到的缺口**：ADR §3「無安全候選或資源仍不足時顯示告警」——頁面原本只在門檻清單列「低於門檻 · 已觸發緊急回收」，沒有明確告警。
- **修改**：`core/admin/resource-rules.ts` 新增純函式 `pressureAlerts`：自動回收未運作（關閉／鎖不可用）而低於門檻時提醒「不會自動處理」；已觸發緊急回收仍不足時，以與維護服務相同的範圍（磁碟只看受壓檔案系統的 store）計算剩餘安全候選，並明示不會停止執行中的工作。系統資源頁頂端以 `role=alert` 卡片顯示；仍在等待持續時間的門檻不告警。後端與 API 契約未變。
- **驗證（實際執行）**：前端 `npm run lint` 通過、`npm test` 43 檔 223 項通過（新增 `pressureAlerts` 4 項）、`npm run build` 完成；`dotnet build Ymir.slnx` 0 警告、`Ymir.UnitTests` 712 項通過。未重跑整合測試（後端未改）與 `e2e:resources` 瀏覽器流程（需要本機維護服務）。
- **分支**：本次雲端 session 指定在 `claude/great-planck-94vgem` 開發與推送，尚未進 `main`；需要時由使用者決定合併。
- **❓待決定（不變）**：正式主機安裝維護服務、runtime host 鎖目錄、API 掛載 socket、既有 image 登記、先 dry run 再開啟自動回收（ADR 第 6 步，需使用者授權）；舊的 `ymir-image-prune` timer 不得與新機制同時啟用。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #087 · 使用者管理：清單只留「編輯」「刪除」，其餘移到使用者設定頁

> 👤 **Claude Code（AI）** · 🕒 2026-10-15 23:55 · `✅完成`

- **需求**：使用者回報「管理 → 使用者」每列操作連結太長。清單每列改為只有「編輯」與「刪除」；「編輯」進入新的 `/admin/users/:userId`（`UserDetailPage`），集中處理顯示名稱、角色、停用 / 啟用、重設密碼（本機帳號）、擴充能力、模型權限（沿用 `UserModelCard`，儲存後重新載入卡片）與刪除（刪除後回清單）。
- **後端**：新增 `GET /api/admin/users/{userId}`（`AdminGetUser`，Admin + XSRF，已刪除帳號回 404 `USER_NOT_FOUND`），讓設定頁可直接以網址開啟（清單上限 200 筆）。授權矩陣、OpenAPI 快照、`schema.ts` 已更新；`UserManagementTests` 補單筆讀取與刪除後 404。
- **驗證**：`dotnet build` 0 錯誤；整合測試 `UserManagementTests`、`AuthorizationMatrixTests`、`OpenApiSnapshotTests` 9/9 通過；前端 lint、Vitest 219/219、build 通過。本機 Development API + `ng serve` 以 Playwright 實跑：清單列只剩「編輯」「刪除」，設定頁改名、停用 / 啟用、改訪客、重設密碼、擴充能力、模型權限、刪除後回清單、不存在的 id 顯示「找不到使用者」，無前端錯誤；390px 寬度版面正常。
- **e2e 腳本**：`admin-flow`、`auth-flow`、`network-flow`、`user-models-flow` 改從「編輯」進入設定頁；這四支需要 Fake OIDC / Podman network / 4 個模型等前置，本次只做語法檢查，**未完整重跑**。
- **分支**：本 session 指定在 `claude/funny-euler-98tvzf` 開發並推送（與 CLAUDE.md「直接在 main」規則不同，依 session 指示），需要時再整合回 main。
- **整合回 main（使用者要求）**：rebase 到含 `5ea2098`（個人設定拆成四個分頁）的最新 main，解決 `admin-flow.mjs` 衝突（保留 `/settings/tools` 與新的設定頁 selector）；重跑 build、上述 9 個整合測試、前端 lint／Vitest 219／build 全部通過後 fast-forward 推送 main。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #086 · 個人設定改成四個分頁

> 👤 **Claude（AI）** · 🕒 2026-10-15 23:45 · `✅完成`

- **需求**：使用者覺得個人設定裡「管理連接與工具 →」跳到下一頁的寫法不好看，要求一進個人設定就看到多個分頁。
- **做法**（`5ea2098`）：`/settings` 改為外框 `SettingsLayout`（標題＋分頁列，樣式同管理區 `AdminTabs`），四個分頁都是子路由：個人 System Prompt（`/settings`）、變更密碼（`/settings/password`，非本機帳號顯示說明）、帳號連接（`/settings/connections`，OneDrive；授權導回位址與舊的 `/settings?onedrive=` 相容轉址不變）、MCP 與工具（`/settings/tools`，個人 MCP＋擴充能力摘要）。「連接與工具」拆成兩個分頁，對應使用者說的四個分頁。後端沒有變更。
- **驗證**：前端 lint、`ng test`（43 檔 219 項）、build 通過；本機 Development API（隔離 `ymir-sql`）＋ `ng serve` 跑 `e2e:connections` 通過（分頁導覽、權限、註冊、舊 OAuth 回跳、窄螢幕），並截圖確認四個分頁。`e2e:auth` / `e2e:admin` / `e2e:mcp` / `e2e:network` 只改了網址或 selector，這次沒有重跑。
- **部署**：只改前端，正式站要重建 API image 才會生效。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #085 · 全部分支核對、main 同步與部署前 migration 修正

> 👤 **OpenCode（AI）** · 🕒 2026-10-15 22:47 · `🔧修正`

- **範圍**：使用者要求合併全部 branch、pull 最新並重新部署。乾淨 main 執行 fetch／rebase，`ea1c1c1` 已包含目前 7 個 `origin/claude/*` 分支，不製造多餘 merge、不刪除分支。該提交 CI [38723526596](https://github.com/puremars2015/Ymir/actions/runs/38723526596) 的 .NET、Angular、Agent image 全數通過。
- **部署預演發現**：獨立、無網路的暫時 SQL container 套用 idempotent migration 時，`MakeTools` 更新 `produces_tool` 的敘述在新欄位建立前就被 SQL Server 編譯，報 `Invalid column name 'produces_tool'`。修正為 `EXEC(N'UPDATE ...')` 延後解析，補單元回歸測試；不更改 migration ID、資料模型或已套用資料庫的歷史。
- **修正後實際驗證**：`dotnet build` 0 警告／錯誤，712 單元測試全通過、無略過，format 與兩模組 `has-pending-model-changes` 通過。重新產生的兩模組 idempotent SQL 在全新隔離 SQL container 連續套用兩次，兩次完整 migration history 均符合固定清單（Platform 8 筆、VibeMaker 20 筆）；測完僅停止並移除該新建測試 container，未連正式 SQL。主機部署器 14 個檢查通過。
- **固定正式目標**：修正提交 `e7cf9f992d800863b63fcf5a2116aab6d7e115b5` 已推送，CI [38755368552](https://github.com/puremars2015/Ymir/actions/runs/38755368552) 全數通過。新版含 Angular 的 API image 已建置，主機專用產物與授權入口位於 `/home/itadmin/ymir-production-install/update-e7cf9f9/`；本次紀錄提交僅更新文件，正式程式目標仍固定該修正提交。
- **正式站尚未切換**：四個正式 unit active，本機 health Healthy、公開 `/login` HTTP 200。API 當前 `NRestarts=3`，不宣稱先前 GC 問題已解決。主機專用部署器沿用上輪備份／校驗／migration history／安全設定比對／失敗回復流程，固定三筆待套 migration（`UserAdminManagement`、`PersonalMcpServers`、`MakeTools`）。準備完成後需使用者在本機終端授權 sudo，不在聊天提供密碼。
- **保留邊界**：不新增 DNS、不擅自啟用 SiteHost、維護服務或未配置的 Google SSO；小工具的正式網站網域與 SiteHost 仍待配置。真登入、加密資料解密、Pi SSE、Graph／第三方 MCP 仍需人工驗收。
- **正式部署完成（本機終端 sudo 授權後）**：固定程式 `e7cf9f9`、API image `7f67554477963434ef15efe2b3e2114a056608ce2a270520d3a1663a75707a74`、RuntimeHost `/opt/ymir/releases/e7cf9f9/runtime-host`。部署器完成無執行中工作檢查、SQL COPY_ONLY／CHECKSUM／RESTORE VERIFYONLY、金鑰／workspace／設定備份與校驗，備份 `/var/backups/ymir/e7cf9f9`；三筆 migration 與完整 history／新表欄位驗證通過。精確 image、有效 ENV、mount／安全設定均保留。部署結果 success、狀態碼 0，未觸發回復。
- **部署後獨立讀回**：四個正式 unit 全部 active／running、NRestarts 0、ExecMainStatus 0；RuntimeHost 工作目錄正確，本機 `/health` Healthy，公開 `https://ymir.thetainformation.com/login` HTTP 200。文件提交 `1920e22` 的 CI [38756223571](https://github.com/puremars2015/Ymir/actions/runs/38756223571) 全通過；本次後續提交只記錄部署結果，不改正式程式版本。保留上述人工驗收與 SiteHost 待配置限制，不宣稱全站功能已驗收。

---

### #084 · 小工具與 main 整合（ADR 改編號 0029、migration 重新產生）

> 👤 **Claude（AI）** · 🕒 2026-10-15 19:05 · `🔧修正`

- **整合**：推送前 origin/main 已有另一工作階段的「個人連接與工具」（ADR-0028、migration `PersonalMcpServers`）。rebase 時解決衝突並保留雙方：`ExecutionRunner` 同時注入 `PersonalMcpService` 與 `SiteService`、ADR 索引兩筆都保留；本功能改為 **ADR-0029**（檔名、程式註解、CLAUDE.md、看板 #083 同步）。
- **migration**：`MakeTools` 原本在 `PersonalMcpServers` 之前產生，已在其後重新產生（`20261015103507_MakeTools`，內容相同、含預設主題的 `produces_tool` 更新），model snapshot 與 main 一致，`has-pending-model-changes` 無變更。
- **驗證（整合後實際執行）**：`dotnet build` 0 警告、`dotnet format --verify-no-changes` 通過；單元測試 711 通過；OpenAPI 快照與前端 schema 重新產生後無差異；整合測試全套 342 項第一次 307 通過、11 項逾時失敗（同時在跑 4 分鐘的前端 build，主機只剩約 2 GB 記憶體，失敗項目都執行 1～4 分鐘），單獨重跑這 10 個類別 67 項全部通過；前端 lint、`ng test`（43 檔 219 項）、build 通過。整合測試使用獨立暫時 SQL container（port 14339），未使用正式 `ymir-sql`。
- **待辦**：同 #083——正式站需部署 SiteHost 與網站網域、備份後套 migration `MakeTools`。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #083 · 左側「小工具」（ADR-0029）

> 👤 **Claude（AI）** · 🕒 2026-10-15 18:50 · `🔬驗證`

- **需求**：左側選單新增「小工具」區塊，放 `/make 小工具架設` 產生的頁面連結。使用者補充：產生的主要是前端網頁，點開後以 iframe 嵌入；之後才提供統一的後端服務；一開始只顯示自己的，之後再讓開發者指定分享給特定的人。另確認 #080 的「聊天系統」就是右側成員之間的訊息（實作不需調整）。
- **決定**：詳見 [ADR-0029](../adr/0029-make-tools-sidebar.md)。Agent 產生的 HTML 不能在 Ymir 網域執行，所以沿用網站託管（ADR-0016）的獨立網站網域：小工具自動發布成只有擁有者能看的網站，Ymir 以一次性票據在 iframe 內開啟。
- **後端**：`make_topics.produces_tool`（預設「小工具架設」= true，Admin 可勾選）、`conversations.makes_tool`、`sites.is_tool`（migration `MakeTools`）；產生小工具的主題附加平台規定（成果放 `tool/index.html`）；`ExecutionRunner` 在個人對話執行成功、寫完成事件前呼叫 `SiteService.PublishToolAsync`（best effort，沒有 `tool/index.html` 時略過、不留失敗紀錄），之後重新發布同一個網站；`GET /api/tools`；Ymir CSP 加網站網域的 `frame-src`；SiteHost 加 `frame-ancestors 'self' <PlatformUrl>`，網站 cookie 在 https／`*.localhost` 改為 `SameSite=None; Secure; Partitioned`（iframe 跨 site 才能登入）。
- **前端**：側邊欄「小工具」區塊（網站託管已設定或已有小工具時顯示）、`/tools/:siteId` iframe 頁（回到對話修改、開新分頁）、`ToolStore` 在登入與每次執行結束時重新讀取；`toolFrameUrl` 只接受網站網域的 http(s) 網址；Admin「Make 主題」新增「產生小工具」勾選與標籤。新增 `npm run e2e:tools`。
- **驗證（實際執行）**：
  - `dotnet build` 0 警告、`dotnet format --verify-no-changes` 通過；單元測試 660 通過（新增 `ToolRulesTests`）。
  - 整合測試（獨立暫時 SQL container，port 14339，未使用正式 `ymir-sql`）：新增 `ToolTests`（問需求階段不發布、`tool/index.html` 出現後自動發布為私人網站、之後重新發布同一個網站、他人看不到也拿不到票據、一般對話不發布、Ymir CSP 與 SiteHost frame-ancestors）；授權矩陣、OpenAPI 快照更新。全套 334 項第一次跑時有 7 項在 1 分鐘逾時（主機剩約 2 GB 記憶體），單獨重跑這 7 個類別與網站 / 小工具 / 授權矩陣共 46 項全部通過。
  - 前端（Node 24.15）lint、`ng test`（42 檔 215 項）、build 通過。
  - 瀏覽器 e2e（`e2e:tools`，獨立的 API 5081 / SiteHost 5300 / Angular 4201，正式服務未受影響）：空白提示 → `/make` 問需求階段不發布 → 寫入 `tool/index.html` 後下一次完成即出現在左側 → 點開以 iframe 從 `*.sites.localhost` 載入、腳本可執行（私人網站票據 + Partitioned cookie）→ 其他使用者看不到也打不開 → 手機寬度正常。
- **未驗證 / 部署前提**：正式站目前沒有部署 SiteHost 與網站網域；要使用小工具需先設定萬用字元 DNS / TLS / Tunnel、`Ymir__Sites__*` 與 SiteHost（`PlatformUrl` 必須是正式 Ymir 網址），並備份後套 VibeMaker migration `MakeTools`。未以真實模型測試 Agent 是否確實把成果放在 `tool/index.html`（使用示範 harness）。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>
### #082 · 個人 MCP CI：授權測試不依賴能力設定順序

> 👤 **GPT（AI）** · 🕒 2026-10-15 · `🔬驗證`

CI [38720117994](https://github.com/puremars2015/Ymir/actions/runs/38720117994) 的 Angular 與 Agent image jobs 通過，.NET build／format 通過，但完整測試中授權矩陣 `OtherUsersResources_AreNotAccessible` 的 MCP test owner 回 404、預期卻為 403。矩陣同 fixture 的管理員測試可能先開放 MCP，原 override 假設「能力必為關閉」而受執行順序影響；單跑矩陣時沒有暴露。

修正 MCP test 先以目前使用者確認註冊存在且屬於本人，再檢查能力：不存在／他人的 id 一律 404；自己的有效註冊在能力關閉時仍 403。矩陣 owner override 固定 404；新增撤銷另一使用者權限後仍不能探知存在／不存在 id 的回歸斷言，未跳過或停用檢查。

使用專用隔離 SQL container、暫存目錄安裝的 Pi 1.0.0、Node 24 與 Fake 服務執行完整 `dotnet test --solution Ymir.slnx --configuration Release`：1041 tests（702 單元＋339 整合）全部通過、0 failed／0 skipped，約 3 分 23 秒。格式檢查因執行工具重啟中斷一次，接續後完整 `dotnet format ... --verify-no-changes --no-restore` 通過；diff check 通過。未改前端或 migration，沿用 #081 的 lint／217 tests／build／E2E 結果。專用 SQL container 已清除，正式服務與資料庫未變更；推送後追蹤新 CI。

---

### #081 · 個人連接與工具：OneDrive 與個人 MCP（ADR-0028）

> 👤 **GPT（AI）** · 🕒 2026-10-15 18:23 CST · `🔬驗證`

使用者同意新增統一入口及先支援 OneDrive／個人 MCP。原 main 有另一工作階段尚未提交的使用者管理變更，經使用者選項 2 明確授權一次性獨立 worktree，於 `personal-connections-tools` 隔離開發；不修改／stash 原工作目錄。交付前 rebase 已提交的 ADR-0027 使用者管理與 Guest，僅 ADR 索引衝突，保留 0027 與 0028，程式自動合併後重驗。未開 PR、不建立 merge commit、不 force push。

發布前原工作目錄再次出現未提交的 MakeTools 開發，故不在該目錄操作 rebase／fast-forward；由已驗證且乾淨的授權 worktree 以 `git push origin HEAD:main` 做一般非強制 main 更新。原工作樹與本機 main 不移動，未提交內容保留，後續該工作階段需在自己的變更提交後同步遠端再整合。

新增 `/settings/connections` 與個人設定導覽入口，分開帳號 Connector、個人 MCP、原能力摘要。OneDrive 卡片沿用 OAuth／授權／同步根／瀏覽／解除，未開放任意 Connector；管理員未允許時也能看到限制。新的 connect returnUrl 回此頁，舊 `/settings?onedrive=...` 仍導向新入口並處理結果；更新既有 admin／network／OneDrive／平台 MCP E2E 的入口，不移除舊功能。

新增專用 `vibemaker.personal_mcp_servers` 與 migration `20261015093415_PersonalMcpServers`；每人最多 20 個、名稱唯一。`/api/connections/mcp` GET／POST／PATCH／DELETE／test 只由目前使用者推導 owner，不接受 user id。新增／啟用／測試／執行均要求 MCP＋internet，關閉能力後仍可看摘要、停用、移除。Bearer token 以 owner-bound Data Protection purpose 加密，回應只含 hasCredential；不回顯、不記 prompt／稽核／設定檔／程序參數，UI 提交後立即清空。稽核只記 action、actor、註冊 id 與結果，不含名稱／URL／token／遠端回應。

MCP 首批僅 HTTPS 443 streamable HTTP，不開放 stdio、任意命令／headers／OAuth MCP／套件上傳。initialize 測試不呼叫工具，獨立 handler 禁止 proxy／redirect／cookie；DNS 解析後拒絕任何非公開 IP，直接連接已驗證 IP 並保留 hostname TLS 驗證，防止 DNS 檢查與連線分離造成 rebinding。限制 10 秒、64 KiB、6 次／分鐘；涵蓋 private／loopback／link-local／CGNAT／IPv4-mapped／IPv6 transition／文件網段與 Azure 平台位址拒絕，僅接受測試送出的協定版本，錯誤只顯示固定摘要。

啟用註冊只帶入個人 Agent 執行；設定檔使用 id 衍生名稱與 token 環境變數引用，平台 MCP 最後合併且優先，原 Agent `mcp.user.json` 保留。憑證只送入該次個人 Pi 程序環境；群組執行明確不帶入。UI／指南說明同一個人 runtime 的程式可能讀取自己的 MCP token，不能宣稱如 OneDrive 一樣完全留在 API；停用／移除影響下次執行，不撤銷遠端 token 或中止既有程序。

驗證：整合 ADR-0027 後 702 單元測試通過，完整 format check 與 Release solution build 通過、0 warnings／0 errors，EF 確認無 pending model change。隔離 SQL 上 PersonalMcpManagement 8、授權矩陣 4、UserManagement 4、ChatGroup 4、OpenAPI 1 均通過，無略過；另在整合前驗證既有 OneDriveLinks 8／OneDriveConnection 12。新測試覆蓋密文／owner binding、不回顯、跨使用者、個人執行／群組隔離、撤銷、啟停／移除、重名／數量上限、無效 token／header 注入、程序 env 而非參數／設定檔。OpenAPI 與 TS 重產確認一致。前端 lint、217 tests、build 通過。

新增 `npm run e2e:connections`，在隔離 Development API／SQL test database（非正式 DB）、Chromium 驗證導覽、禁止狀態、新增且輸入／回應不留 token、真 DNS／transport 失敗摘要、啟停、撤銷後移除、舊 OAuth 回跳與手機版；整合前後均通過，無 pageerror。截圖 `/tmp/opencode/connections-e2e-screenshots-merged`；E2E API 已停止、測試 DB 已刪除，專用 SQL 容器完成後清除。未以正式資料庫／真憑證測試，未呼叫外部 MCP 工具。

**部署／剩餘**：本輪僅實作與發布程式，不自行切換正式站。正式程式仍為 3855dd6；部署完整 main 須先備份並套兩模組待套 migration（含 PersonalMcpServers 與 UserAdminManagement），再更新 API／前端。無 RuntimeHost protocol／mount 變更。真實第三方 HTTP MCP／Microsoft 授權、OAuth MCP、私網測試、stdio、修改 URL／token、額外 Connector provider 留待後續；OneDrive 雲端寫回仍依 O4／O5 未啟用。詳見 [使用指南](../guides/personal-connections.md)。

---

### #080 · 使用者管理：改名、角色、刪除與訪客（ADR-0027）

> 👤 **Claude（AI）** · 🕒 2026-10-15 17:40 · `🔬驗證`

- **需求**：使用者要求「管理 → 使用者」可以設定名稱、刪除帳號、變更權限等級，並新增角色「訪客」：與使用者相同，但不能使用聊天系統，也沒有群組系統。
- **解讀**：「聊天系統」指使用者之間的一對一訊息（ADR-0023），「群組系統」指群組聊天與 @bot（ADR-0026）；訪客仍可以和 Agent 對話。若原意是連 Agent 對話也不能用，需要再調整。
- **決定**：詳見 [ADR-0027](../adr/0027-admin-user-management-and-guest-role.md)。
  - 企業帳號 / Google 帳號的 IdP 只同步「是否為 Admin」；Ymir 對這些帳號只能在使用者與訪客之間切換（授予或拿掉 Admin 仍由 Entra app role / `AdminEmails` 設定）。本機帳號可設定任何角色。不能改自己的角色、不能刪除自己。
  - 刪除是標示刪除：歷史紀錄保留名稱；外部身分與本機密碼釋放，同一帳號可重新建立成新使用者；與停用一樣立即登出並清理執行中的工作、模型 key、runtime。
  - Admin 設定的顯示名稱之後登入不被 IdP 覆寫。
- **後端**：`UserRole.Guest`、`UserStatus.Deleted`、`users.display_name_overridden`（migration `UserAdminManagement`）；`PUT /api/admin/users/{id}/display-name`、`PUT .../role`、`DELETE /api/admin/users/{id}`（稽核 `admin.user.rename` / `admin.user.role.<role>` / `admin.user.delete`）；`MessagingPolicy` 套在 `/api/direct-messages`、`/api/chat-groups`；訪客不在聯絡人、不能收訊息、不能被加入群組；帳號有效的檢查改為 `Status == Active`。
- **前端**：使用者頁角色欄改為下拉選單（依 `canChangeAdminRole` 限制選項）、新增「改名」「刪除」，建立本機帳號可選訪客；訪客不顯示成員面板、不連訊息 SSE。
- **驗證（實際執行）**：
  - `dotnet build Ymir.slnx` 0 警告；`dotnet format --verify-no-changes` 通過。
  - 單元測試 651 通過（新增 `UserManagementRulesTests`）。
  - 整合測試 331 項：307 通過、0 失敗、24 略過（本機未安裝 Pi CLI 的測試）；新增 `UserManagementTests`（改名後重新登入仍保留、訪客 403 與聯絡人 / 訊息 / 群組限制、改回使用者恢復、Dev 帳號升 Admin、不能改自己、刪除後 cookie 失效 / 不能登入 / 不在清單 / 帳號名稱可重建、不能刪自己）；授權矩陣加入三個端點；OpenAPI 快照與前端 schema 更新。整合測試使用獨立的暫時 SQL container（port 14339），未使用正式 `ymir-sql`。
  - 前端（Node 24.15）`ng lint`、`ng test`（41 檔 213 項）、`ng build` 通過。
- **未驗證**：未做瀏覽器 e2e；Pi 相關整合測試在本機略過，由 CI 執行。正式站尚未部署，需先備份並套 Platform migration `UserAdminManagement`。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #079 · main 同步與 3855dd6 正式部署

> 👤 **GPT（AI）** · 🕒 2026-10-15 16:15 CST · `🔬驗證`

使用者要求重新 pull、合併與部署。乾淨 main 執行 fetch／rebase，目標固定 `3855dd688756044f9610fc214ba43bb7bfce9f23`，所有已取得遠端分支均已包含，無額外分支待合併。目標 CI [38691443168](https://github.com/puremars2015/Ymir/actions/runs/38691443168) 全通過；本次另外建置含 Angular 的 API image、self-contained linux-x64 RuntimeHost，產生兩個模組的 idempotent migration SQL。Google SSO、模型路由、部署 secret 與原安全限制均保留，不自動啟用未配置能力。

經本機終端 sudo 授權完成固定產物切換；API 精確 image 為 `57c69ad7f1727f5001883469a495a32d4642a371b654bc49f834205fc99be932`，RuntimeHost 為 `/opt/ymir/releases/3855dd6/runtime-host`。切換前封閉公開入口並確認 Queued／Running 為 0，停止 API／RuntimeHost，SQL COPY_ONLY＋CHECKSUM 備份、RESTORE VERIFYONLY、匯出 SHA-256 與私有 tar 備份／compare 均通過；備份含 keyring、workspaces、部署設定與舊 RuntimeHost。正式套用 `20261015035207_ChatGroups`、`20261015040226_OneDriveLinkedItems`，完整 history 與新增五張表通過檢查。

部署器的問題與處理（未跳過安全 gate）：
- 初次 preflight 只接受 UID `1654`，正式 Quadlet 實為同樣非 root 的 `1654:1654`；修正限定接受這兩種表達，仍拒絕 root／root 群組／privileged。
- SQL Msg 1934：sqlcmd 缺 `-I`／`QUOTED_IDENTIFIER ON`，建立 filtered index 失敗且新 migration 未套用；原服務恢復。補齊必要 SET options，獨立測試 SQL 容器連續兩次套用兩模組完整 SQL 成功後再執行正式 migration；測試容器清除，不使用正式資料做測試。
- 第一次程式切換後 raw Mounts 清單比對失敗；四個同設定的測試容器重現 Podman 回傳順序不同。改按 Destination 排序，仍逐欄比對所有掛載欄位；來源／RW／類型變更仍拒絕。該次回復舊程式，已成功套用的 additive migration 保留，不還原資料庫或刪資料。
- 下一次所有本機驗證通過，但 Tunnel 尚未完成 edge 註冊時公開登入回 530，依原 gate 回復。加入有上限的公開就緒重試與逾時拒絕測試，最終部署成功。部署器共 13 項檢查通過；每次實際嘗試都保留獨立備份，不覆蓋前次檔案。

最終備份 `/var/backups/ymir/3855dd6-retry3`；原備份與舊 image／RuntimeHost 均保留。固定部署產物／主機專用腳本位於 `/home/itadmin/ymir-production-install/update-3855dd6`，root 結果 `/var/lib/ymir-update-3855dd6-retry3/result.json`。成功 gate 已確認精確 image、非 root／唯讀／mounts／capabilities／network／security options 與有效 ENV 保留（只容許 framework image 的版本預設值更新）。獨立讀回四個正式 unit active、NRestarts 0、ExecMainStatus 0、RuntimeHost 工作目錄正確、本機 `/health` 與公開 `/login` HTTP 200，以及兩筆 migration history；未回復最終版本。

**未宣稱全功能驗收**：真 Entra 登入、既有加密資料解密、真 Pi SSE／群組操作、真 OneDrive Graph 仍待人工驗收；OneDrive O4／O5 雲端寫回與刪除保護仍未啟用。既有 native GC crash 的根因未由本部署證實修復；現有設定原樣保留。後續紀錄提交僅修改文件，正式程式版本仍固定 3855dd6。

---

### #078 · OneDrive O3b CI 格式檢查修正

> 👤 **GPT（AI）** · 🕒 2026-10-15 · `🔬驗證`

CI run 38689068262 的 .NET job 在 Format check 失敗，尚未進入該 job 的 build／test；Angular 與 Agent image jobs 通過。本機以 CI 同一指令重現 WHITESPACE：OneDriveLinkedItem／OneDriveLinkedFile 初始化器同一行有多個欄位。只改成逐行欄位，不變更功能、資料模型或測試，不停用檢查。

重新驗證 `dotnet format Ymir.slnx --verify-no-changes --no-restore` 通過（首次診斷因 120 秒 timeout 中斷，延長後完整通過）；Release solution build 0 warnings／0 errors、Release 636 單元測試全通過、diff check 通過。推送後繼續追蹤完整 CI；未重跑本機整合測試，未觸及正式服務／資料庫。

---

### #077 · ADR-0022 O3b：選定 OneDrive 項目交給 Agent

> 👤 **GPT（AI）** · 🕒 2026-10-15 · `🚧進度`

新增 `onedrive_linked_items` 與獨立 `onedrive_linked_files` 基準表、OneDriveLinkedItems migration、scope 擁有者限定的 GET／POST／PATCH／DELETE API。使用者從對話檔案面板瀏覽／搜尋後連結檔案或資料夾，可切唯讀／取消連結；專案共用、獨立對話隔離。不要求原同步 root，但會確認能力／連線、自己的 drive 與 ancestry，拒絕 root、原同步根及其上下重疊範圍、相互包含連結、remoteItem／跨 drive。管理者關閉能力後停止下載，仍可取消連結；不刪雲端或本機副本。稽核以 target type 記唯讀設定、target id 記 Graph item id，不記檔名／路徑。

執行前經 reader／writer 下載到 `onedrive/<id8>-<安全名稱>`，以 JSON path 與本階段限制附加 Agent prompt；token 不離開 API。基準為 eTag＋本機 SHA-256，未寫回修改不會被無聲覆蓋，衝突副本目前僅保存在本機。唯讀恢復雲端對應內容前亦保留修改，不是 OS 唯讀掛載。加入預設 20 links／scope、500 files／folder、100 MB／file、遞迴與 metadata budget；長路徑基準使用 SHA-256 path key 作唯一索引，避免 SQL index key 超限。原 ADR-0013 同步排除頂層 onedrive/（不分大小寫），不重複同步。FakeGraph 補齊 parent:/{name} 的既有子項目查詢，修正測試替身先前把既有同步 root 判成不存在。

驗證：solution build 0 warnings／0 errors，628 單元測試通過；隔離 SQL（14335，非正式資料庫）OneDriveLinks 8、既有 OneDriveSync 7、OneDriveConnection 12、授權矩陣 4、OpenAPI 快照 1 均通過，無略過。涵蓋實際 Scripted execution 下載與 prompt、專案共用／跨 scope 拒絕、root／重疊排除、唯讀切換、symlink／超大本機檔案保護、連結／檔數上限、撤銷能力、取消連結保留資料、原同步 namespace 排除。首次測試因隔離 SQL 尚未就緒而 pre-login 失敗，確認 sqlcmd readiness 後重跑；未切到正式 SQL。EF has-pending-model-changes 確認無遺漏 migration。前端 lint、205 tests、build 通過，包含選取／取消、權限關閉、失敗唯讀 checkbox、不讓舊對話的請求結果污染新對話。未執行真 Graph／Entra、瀏覽器 E2E 或正式部署。

Push 前 rebase 遠端群組聊天更新（#076），保留其功能與留言；OpenAPI／TypeScript 產生檔衝突由合併後 API 重新產生解決。本留言改為 #077。整合後再次驗證 636 單元、OneDriveLinks 8／授權矩陣 4／ChatGroup 4／OpenAPI snapshot 1，以及前端 lint／210 tests／build 通過。群組執行的 OneDrive 禁用邊界保留，不把個人連結帶入共用 workspace。

**部署／剩餘**：正式站需先備份並套 OneDriveLinkedItems migration，再更新 API／前端；若部署完整 main，亦須套其他待套 migration（包括 Platform ChatGroups）。本 OneDrive 切片不需要換 RuntimeHost／Agent image。O4 雲端寫回／雲端衝突副本、O5 刪除／移動／改名與大量刪除確認仍未啟用；入口目前在檔案面板，輸入區附件選單待後續。不可宣稱已能修改整個雲端 OneDrive。

---

### #076 · 群組聊天與 @bot（ADR-0026）

> 👤 **Claude（AI）** · 🕒 2026-10-15 12:30 · `🔬驗證`

- **需求**：使用者要求 (1) 群組聊天；(2) 群組中以 `@bot` 與 Agent 交談，同一群組第一次呼叫時建立該群組專屬的 Agent container。
- **使用者決定**：模型費用記在呼叫者；Agent 背景是最近的群組訊息；擴充能力依呼叫者權限；任何人可建立群組、建立者管理。依此新增 [ADR-0026](../adr/0026-group-chat-and-group-agent.md)（修訂 ADR-0007、0008、0023）。補充：呼叫者的個人連結（OneDrive、個人 system prompt）不帶入群組 workspace（成員共用）。
- **開發方式（使用者核准的一次性例外）**：原工作目錄有另一個工作階段尚未 commit 的 OneDrive 修改，為避免互相干擾，另開 worktree 開發，完成後 rebase 到最新 origin/main 並推送 main（不建 PR、不 merge commit）。
- **後端**：
  - Platform：`platform.chat_groups` / `chat_group_members` / `chat_group_messages`（migration `ChatGroups`）、`IChatGroupService`；`/api/chat-groups`（清單、建立、詳情、改名、成員加入 / 移除 / 退出、封存、訊息、已讀）；非成員 / 已封存一律 404；群組異動寫稽核（不含內文）；即時通知沿用 `/api/direct-messages/events`（`group-message` / `group-updated` / `group-read`）。
  - @bot：`ExecutionService.SubmitForGroupAsync` 在群組的 Agent 對話（擁有者 = 群組 id）建立 execution，`AgentExecution.UserId` = 呼叫者。`ExecutionRunner` 以對話擁有者為 runtime 擁有者（container、workspace、鎖、閒置停止），以呼叫者計算模型、配額、預算與擴充能力；群組執行不做 OneDrive、不附個人 system prompt，改用群組 system prompt；模型 key 改為只給這次執行、結束即撤銷（避免共用 workspace 留下可用的 key）。runtime host 協定不變。
  - 背景：最近 30 則（約 8000 字）群組訊息放在 `<group-chat>` 資料區塊並跳脫結尾標籤。Bot 回覆先存 Pending，`GroupBotWorker` 依 execution 結果補齊（API 重啟後仍會補）；成員以 `/api/chat-groups/{id}/executions/{executionId}/events` 看即時回答。
  - 管理介面執行環境清單以「群組：名稱」顯示群組 runtime；`RuntimeLifecycleService` 判斷執行中時納入群組對話。
- **前端**：成員面板加「成員 / 群組」分頁；建立群組、群組對話（顯示寄件者、Bot 回覆標示呼叫者、Pending 時即時顯示「正在思考中 / 工作中」與回答）、成員管理（擁有者改名 / 加入 / 移除 / 封存，成員退出）；輸入 @bot 時提示費用記在自己帳號；工具列未讀數含群組。共用樣式抽到 `layout/_chat-panel.scss`。
- **驗證（實際執行）**：
  - `dotnet build` 0 警告、`dotnet format --verify-no-changes` 通過；新增整合測試 `ChatGroupTests`（成員資格、非成員 404、擁有者權限、退出 / 移除 / 封存、@bot 以呼叫者執行、成員 / 非成員串流權限、背景含群組訊息且跳脫、群組對話不出現在個人清單、workspace 屬於群組 id）與單元測試 `GroupPromptBuilderTests`；授權矩陣、OpenAPI 快照與前端 schema 更新（既有 path 只有 SSE 說明文字變更）；全套 `dotnet test --solution Ymir.slnx`（Pi 1.0.0）932 通過、0 失敗、0 略過；rebase 到含 OneDrive 瀏覽（#075）的 main 後重跑 943 通過、前端 206 項通過、schema 與重新產生的一致。整合測試使用獨立的暫時 SQL container（port 14334），未使用正式 `ymir-sql`。
  - 前端 lint、`npm test`（39 檔 203 項）、build 通過。
  - 瀏覽器（`npm run e2e:groups`，兩個帳號 + 手機寬度）：建立群組、即時未讀、即時收發與 HTML 不被解譯、@bot 即時回答並完成（呼叫者與其他成員都看到）、只有擁有者能管理、成員退出後群組消失、手機抽屜，全部通過；修正封存與改名按鈕樣式。
- **未驗證 / 待決定**：未以真實 Pi + 模型測試群組 Agent（使用示範 harness）；正式站尚未部署（需要 migration `ChatGroups`）。群組檔案面板（下載 Agent 產出的檔案）列為之後的階段。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #075 · ADR-0022 採納與第一個實作切片 O3a：整個自己的 OneDrive 瀏覽／搜尋

> 👤 **GPT（AI）** · 🕒 2026-10-15 · `🚧進度`

使用者要求實作 ADR-0022、由使用者開啟 connector 存取自己的 OneDrive；將 ADR／索引標記採納。本輪完成第一個可驗證切片：後端 GET browse／search、Graph parentReference／remoteItem metadata 與搜尋、現有個人授權／能力檢查，以及個人設定的整個自己 OneDrive 瀏覽／搜尋入口（不需先選同步根目錄）。使用者傳入的 item id 必須對應自己的 drive、拒絕外部 drive／remoteItem 捷徑；根目錄僅對 server 固定的 `/me/drive/root` 容許省略 parentReference。回應只含最少 metadata，不含 token／下載 URL。OpenAPI、Angular 產生型別、FakeGraph、授權矩陣與指南同步更新。

驗證：後端 solution build 0 warnings／0 errors；全部 617 單元測試通過。隔離 SQL 容器（127.0.0.1:14335，非正式 SQL）執行 OneDriveConnectionTests 12、既有 OneDriveSyncTests 6、AuthorizationMatrixTests 4、OpenAPI snapshot 更新 1 皆通過，無略過。前端 Node 24 lint／201 tests／build 通過。包含無能力 403、未連結 409、無同步 root 仍可瀏覽、跨使用者 item id 拒絕、單引號搜尋、metadata 無 token、remoteItem 防護、前端請求取消與錯誤狀態。未跑瀏覽器 E2E 或真實 Microsoft Graph／Entra。

**未完成範圍**：這不是 O3～O5 全部完成。連結資料表／migration、選取項目下載與 AgentPrompt、排除 onedrive/ 重複同步、寫回與衝突、雙向刪除／移動／改名和大量刪除確認仍待後續切片。瀏覽不會自動把雲端檔案交給 Agent；無生產 migration、無正式部署、無雲端資料修改。沒有略過刪除保護直接開放寫入。

---

### #074 · 架構文件補充目前容器清單

> 👤 **GPT（AI）** · 🕒 2026-10-15 · `✅完成`

依使用者要求更新 architecture.md：部署圖以藍框標容器、灰框標主機程序，列出三個 rootful 服務容器、itadmin 的 SQL／LiteLLM／PostgreSQL、ymir 的 LiteLLM proxy 與動態使用者 Agent；補上 Agent → nginx proxy → socat bridge → 主機 LiteLLM 真實路徑。7＋N 是角色計數，不宣稱本輪實測全部容器總數。另列已停止的 itadmin `ymir-sql-old`／`ymir-web`／`cloudflared`，提醒跨帳號同名不是同一容器；volume、RuntimeHost、socat 與 oneshot 不是容器。

驗證：itadmin `podman ps -a`、四個正式 systemd unit 狀態、入口 unit 的執行檔與 Podman 名稱核對；root API 身份引用本輪使用者 sudo 唯讀診斷。ymir 儲存區本次未取得直接清單，proxy 以 #069 部署紀錄標註，不虛構 Agent 數量／個人 ID。文件連結、Markdown fences、Mermaid 基本結構與 diff check 通過，未驗證瀏覽器渲染；純文件變更未重跑應用 build／test。未改服務或清理容器；push 後追蹤 CI。

---

### #074 · ADR-0025 實作：系統資源監控與安全自動回收

> 👤 **Claude（AI）** · 🕒 2026-10-15 02:30 · `🔬驗證`

- **需求**：使用者要求依 [ADR-0025](../adr/0025-ymir-resource-monitoring-and-reclamation.md) 實作並合併到 main（依開發規則直接在 main 開發）。ADR 狀態改為已採納（含新增的主機權限邊界），並補上「實作」一節。
- **主機維護服務**（`src/Ymir.MaintenanceHost`，root，Unix socket + token）：
  - 端點只有 `GET /v1/resources`、`GET|PUT /v1/policy`、`GET /v1/runs?after=`、`POST /v1/dry-run`；沒有 shell／exec／任意刪除；store、帳號、repository、正式設定檔只來自部署設定。
  - 主機 CPU（/proc/stat 差值）、記憶體（MemAvailable）、各 store 所在檔案系統（同一檔案系統去重，只回代號不回路徑）；container 狀態／健康／CPU／記憶體、image 大小／共用 layer／引用數；部分查詢失敗標示失敗，不當作零。
  - 保護規則（`Classifier`，純函式、fail closed）：執行中／暫停／建立中、服務 container、排除清單、沒有 `ymir.reclaimable` label、workspace 非外置或不可存取、任何 container 引用、正式設定指向（Quadlet `Image=`、runtime host `VibeMaker__Runtime__Image`）、每個 repository 最新 3 版、保護版本、其他專案 tag、無法證明歸屬（沒有 `ymir.managed` label 也沒登記）。不用 `--force`、不用 prune。
  - 未使用時間持久化（重啟不重設、再使用後重新起算）；保留天數（0 = 不依期限）；門檻持續 5 分鐘才觸發、冷卻 10 分鐘、回復門檻 +5%、每輪最多 10 項；磁碟壓力只看受壓檔案系統並逐項重新量測；CPU／記憶體壓力沒有安全候選時只留紀錄。每輪紀錄含候選／保護／刪除／失敗數與實測前後可用量。
- **生命週期鎖**（`src/Ymir.Maintenance/LifecycleLock`，advisory flock）：runtime host 在建立／啟動／停止／刪除時持有 `user-<id>.lock`（`VibeMaker:Runtime:LifecycleLockDirectory`）；維護服務刪除前以不等待方式取鎖並重新確認狀態；刪 image 期間持有 `deploy.lock`。鎖目錄不可用時只監控與 dry run。
- **標記**：API 與 Agent Containerfile 每個階段 `LABEL ymir.managed="true"`；Agent container 加 `ymir.managed=true`、`ymir.reclaimable=agent-runtime`（`ContainerCommandBuilderTests` 兩種 engine 都驗證）。既有 image 需在 `RegisteredImages` 登記才可能回收。
- **API / 前端**：`/api/admin/resources`、`/policy`、`/runs`、`/dry-run`（Admin + XSRF，已加入授權矩陣）；政策存 `platform.system_settings`（`maintenance.reclamation_policy`），儲存寫稽核 `admin.settings.reclamation.update`；`MaintenanceSyncWorker` 每分鐘同步政策，並把有刪除／失敗或由門檻觸發的回收寫入稽核 `maintenance.reclaim`。前端「管理 → 系統資源」（主機資源、containers、images、政策、dry run、回收紀錄），未設定維護服務時顯示不支援。
- **部署檔**：`deploy/maintenance/`（systemd unit、鎖目錄 tmpfiles、env 範本、安裝步驟、部署鎖用法）；API Quadlet 範本的 `/run/ymir-maintenance` 掛載預設註解（服務未安裝時目錄不存在會讓 API 無法啟動）；runtime host env 範本加鎖目錄。舊的 `ymir-image-prune` timer 啟用新機制前必須停用（安裝步驟第 1 步）。
- **驗證（實際執行）**：
  - `dotnet build Ymir.slnx` 0 警告；`dotnet format --verify-no-changes` 通過；維護服務單元測試 74 項（保護規則、重啟、競態、鎖、部署鎖、門檻遲滯、磁碟回復即停、不使用 --force、設定拒絕）；整合測試（真正的 Unix socket 服務 + 假 CLI：端點清單、token、任意指令／刪除目標不存在、不合法政策、無 host 路徑；API 未設定／已連線、政策同步、稽核匯入、授權矩陣）；OpenAPI 快照與前端 schema 更新（既有 path／schema 無變動）；全套 `dotnet test --solution Ymir.slnx`（Pi 1.0.0 安裝在使用者目錄）920 通過、0 失敗、0 略過。整合測試使用獨立的暫時 SQL container（port 14333），未使用正式 `ymir-sql`。
  - 前端 `npm run lint`、`npm test`（38 檔 198 項）、`npm run build` 通過。
  - 瀏覽器（Playwright Chromium，`npm run e2e:resources`）：本機以目前帳號的 rootless store 執行維護服務（只查詢與 dry run）＋ Development API：主機資源、containers／images、不顯示 host 路徑、政策驗證與儲存、dry run、回收紀錄、一般使用者不可進入、手機寬度無水平捲動全部通過；修正儲存後狀態未更新的問題。測試服務已停止，store 內 image 全部保留（唯一一筆紀錄：0 候選、6 保護、0 刪除）。
- **未完成 / 待授權**：正式主機安裝維護服務、runtime host 加鎖目錄並重新部署、API 掛載 socket、既有 image 盤點登記、先 dry run 再開啟自動回收（ADR 第 6 步，需使用者授權）。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #073 · Ymir 資源監控與安全自動回收 ADR

> 👤 **Claude（AI）** · 🕒 2026-10-15 · `❓待決定`

- 使用者確認監控 CPU／RAM／磁碟，回收只管 Ymir。新增 [ADR-0025](../adr/0025-ymir-resource-monitoring-and-reclamation.md) 提案與索引：分 store 盤點、未使用時間持久化、保留期限、持續低資源門檻、保護正式／回復映像與使用者資料、受限主機維護服務，不擴大 RuntimeHost 為 root。
- 現有 IMG1 腳本的 store-wide dangling 清理無法證明只屬於 Ymir，新機制不直接使用；不修改既有腳本或安裝 timer。
- CPU／RAM 低資源亦可觸發安全回收，但停止容器與 image 通常不占 CPU／RAM，不能保證解除壓力；不因門檻停止執行中工作。
- 本回合僅文件修改；驗證為 `git diff --check` 及 ADR 索引／看板連結存在性检查，未跑產品 build／測試、未實作 UI／API／worker、未部署或啟用回收。
- 文件已以 `3e6754e` 推送 main；CI 追蹤受阻：主機沒有 `gh`（`gh run list` 回 command not found），未確認 CI 結果。
- **❓待決定**：確認 ADR-0025 的獨立受限主機服務、安全保護與觸發語義後開始實作；正式安裝／啟用另行授權。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #072 · Container image 回收機制

> 👤 **Claude（AI）** · 🕒 2026-10-15 01:00 · `🔬驗證`

- **需求**：使用者指出 container 會自動回收（`--rm`），但 image 不會，要規劃回收機制。
- **現況（實際查詢）**：image 分散在三個 store：root（rootful：正式 API、nginx、Tunnel）、`ymir`（rootless：Agent runtime、LiteLLM proxy）、`itadmin`（rootless：開發建置，**與其他專案共用**）。每次部署 API 新增約 360 MB 的 `localhost/ymir/api:<commit>`，建置另留下沒有 tag 的中間層（SDK 階段約 1.8 GB）。主機磁碟目前 32%，不急迫。
- **做法**：`deploy/maintenance/ymir-image-prune.sh`（預設 dry run，`--apply` 才刪除）＋ `ymir-image-prune.service` / `.timer`（每週日 04:00）＋ README。只處理 `localhost/ymir/*`；一律保留 container 使用中（含已停止）的 image、正式設定指向的 image（API Quadlet `Image=`、runtime host 的 `VibeMaker__Runtime__Image`）與每個 repository 最新 3 個；dangling 只清 root 與 `ymir` store。`podman rmi` 不加 `-f`。
- **驗證（實際執行）**：`bash -n`、`systemd-analyze verify` 通過；`itadmin` store 以 `KEEP=1` dry run 確認刪除候選正確；正式主機三個 store 以 sudo dry run：保留正式 API（pinned）與最新 3 個、Agent image（pinned），可回收 3 個 dangling 建置中間層約 3.1 GB，其他專案的 image 未列入。尚未實際刪除。
- **❓待決定**：是否在正式主機安裝 timer（README「每週自動執行」）。刪除建置中間層後，下一次 API 建置會較慢（需重新下載 / 建置 SDK 階段）。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #071 · 正式 API 週期性崩潰：.NET GC 執行緒 fail-fast（exit 139）

> 👤 **Claude（AI）** · 🕒 2026-10-15 00:00 · `🔬驗證`

- **現象**：使用者看到 Cloudflare「The origin web server returned an invalid or incomplete response」。2026-10-14 23:51:11 與 23:51:20 `ymir-api` 連續兩次 `status=139` 結束，systemd 自動重啟，第三次啟動後對帳把執行中的那次 execution 標成 FAILED，之後穩定。當時 Agent 正在處理上傳的 `.pptx`，最後一個工具是讀取擷取出的圖片。
- **證據（實際查詢）**：kernel log 每次都是 `traps: .NET Server GC / .NET BGC … general protection fault … in libc.so.6[289a2…]`；以 API image 的 glibc 2.39（Ubuntu 24.04）對照，`0x289a2` 位於 `abort()` 內，代表 .NET runtime 在 GC 執行緒主動 fail-fast（通常是 GC 發現 managed heap 毀損）。journal 沒有 .NET 的錯誤輸出（container 唯讀，沒有 dump）。
- **不是這次部署造成**：同一位址的崩潰 10/13 15:03 起共 8 次（10/13 15:03、15:12、16:20，10/14 04:30、16:27、18:23、23:51×2），前 6 次是舊 image（4b8a3af，.NET 10.0.12），每次都是 `ymir-api`。沒有 OOM（記憶體高峰約 316 MB）。
- **❓待決定**：根因需要 crash report / dump 才能確認。建議 (1) 掛一個可寫目錄並設定 `DOTNET_DbgEnableMiniDump` / `DOTNET_EnableCrashReport` 取得下一次崩潰的堆疊；(2) 同時以 `DOTNET_gcServer=0`（workstation GC）作為緩解觀察。兩者都要修改 Quadlet 並重啟 API（中斷數秒）。
- **使用者決定：收集證據＋緩解（2026-10-15 00:16 已套用）**：正式 Quadlet 加上 `DOTNET_gcServer=0`、`DOTNET_DbgEnableMiniDump=1`、`DOTNET_DbgMiniDumpType=2`、`DOTNET_DbgMiniDumpName=/var/lib/ymir/dumps/coredump.%p`、`DOTNET_EnableCrashReport=1`，掛載 `/srv/ymir/api/dumps`（700，uid 1654；dump 含程序記憶體，可能有機密，不得外流）。image 同時更新為 0e16583（.NET runtime 10.0.13，原為 10.0.12）。Quadlet 備份在 `/var/backups/ymir/gc-20261015-001540/`。驗證：health Healthy、`/login` 200、container 內環境變數生效、dump 目錄可寫，api／origin／cloudflared active。
- **後續**：觀察是否還會崩潰；若再崩潰，分析 `/srv/ymir/api/dumps` 的 crash report 找根因。穩定後再決定是否把設定寫回 `deploy/api/ymir-api.container`。第一次執行腳本因 `install -o 1654`（主機沒有該使用者）在修改 Quadlet 前中止，正式站未受影響。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #070 · 執行中一定顯示「正在思考中」/「正在工作中」

> 👤 **Claude（AI）** · 🕒 2026-10-15 00:00 · `🔬驗證`

- **需求**：使用者要求 Agent 還在執行、還沒顯示回答時，一定要看得到「正在工作中」或「正在思考中」的字樣。
- **原本的缺口**：沒有工具執行也沒有狀態訊息時只顯示沒有文字的 `●●●`；後端送了狀態訊息（例如「正在準備回覆」）時只顯示該訊息；模型輸出仍在 `<think>` 區塊內被顯示過濾時也只有 `●●●`。
- **修改**：`execution-state.ts` 新增純函式 `liveIndicator`：執行中且工具在跑（含工具之間的空檔）→「正在工作中......」；執行中且沒有可顯示的回答 →「正在思考中......」；回答出現或執行結束後不顯示。狀態訊息改為第二行補充說明，提示文字保留閃爍動畫。`e2e/tool-progress-flow.mjs` 使用的「正在工作中......」字樣不變。
- **驗證（實際執行，node:24 container）**：`npm run lint` 通過；`npm test` 37 檔 193 項通過（新增 `liveIndicator` 7 項）；`npm run build` 完成。未跑瀏覽器 e2e。
- **已部署**：2026-10-15 00:16 與 #071 一起部署為 `localhost/ymir/api:0e16583`；待使用者在正式站確認顯示。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #069 · 長對話一直顯示「模型發生錯誤」：LiteLLM proxy 回 413

> 👤 **Claude（AI）** · 🕒 2026-10-14 23:00 · `✅完成`

- **現象**：使用者回報一直顯示「模型發生錯誤，請稍後再試」。API log 三次都是同一個對話，`ended with model error: 413 <html>…Request Entity Too Large…nginx/1.31.5`；LiteLLM 沒有對應的 `/chat/completions` 紀錄。20:45 的兩次發生在今天重新部署（20:51）之前，與部署無關。
- **原因**：Agent → `litellm:4000` 實際是 `ymir` 帳號的 `ymir-litellm-proxy`（nginx Quadlet）→ `ymir-litellm-bridge`（socat Unix socket）→ 主機 LiteLLM。nginx 設定沒有 `client_max_body_size`，預設 1 MB；每次模型呼叫都送完整對話歷史，超過後該對話每則都 413。這兩段只在主機上手動建立、不在版控。
- **處理**：新增 `deploy/runtime-host/litellm-proxy/`（bridge unit、proxy Quadlet、nginx 設定 `client_max_body_size 100m` + `proxy_request_buffering off`），`deploy/runtime-host/README.md` 補上安裝步驟、413 的判斷方式與既有主機的修正指令。範例設定以 `nginx:1.27-alpine` 的 `nginx -t` 驗證通過。
- **正式主機已修正**（使用者授權並輸入 sudo 密碼）：備份原設定後加入兩行。第一次只做 `nginx -s reload`，使用者重送仍 413（proxy log：`client intended to send too large body: 1279075 bytes`）：設定檔是單一檔案 bind mount，`sed -i` 換了 inode，container 內仍是舊檔。改為重啟 `ymir-litellm-proxy`，確認 container 內已有 `client_max_body_size 100m`；README 的修正指令同步改為重啟。使用者在原對話重送後確認恢復正常。proxy Quadlet 範本的 `Image=` 需填入主機實際的 nginx 映像。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #068 · 正式站重新部署 4d7d4c0 與 ymir-sql 持久化 volume

> 👤 **Claude（AI）** · 🕒 2026-10-14 21:20 · `✅完成`

- **前置確認**：`git fetch --all --prune` 後本機 `main` 與 `origin/main` 一致（4d7d4c0），7 個 `claude/*` 遠端分支皆已合併（`git branch -a --no-merged main` 為空）。
- **部署**（使用者以圖形 sudo 視窗輸入密碼）：正式站原為 4b8a3af；與其相比只有 API／前端變動，RuntimeHost 與 Agent image 不需更新。
  - 套用前備份 `ymir` 資料庫與 Quadlet 至 `/var/backups/ymir/redeploy-20261014-184902/`、`redeploy-20261014-205030/`（600，root）。
  - 以 `dotnet ef migrations script --idempotent` 產生 SQL，在 `ymir-sql` 內以 sqlcmd 套用 `DirectMessages`、`RagGuideMakeTopic`。第一次少了 `-I` 失敗（Msg 1934，filtered index 需要 `QUOTED_IDENTIFIER ON`），交易回復、未留下部分變更，加 `-I` 後成功。
  - rootful 建置 `localhost/ymir/api:4d7d4c0`；Quadlet `Image=` 是固定 image ID，改成新 ID 後 daemon-reload 並重啟。
  - 驗證（實際執行）：`/health` Healthy、`/login` 200、`/api/auth/providers` 200；api／runtime-host／origin／cloudflared 皆 active。
- **ymir-sql 持久化**（使用者要求）：正常 SHUTDOWN 後把 `/var/opt/mssql` 複製到 named volume `ymir-sql-data`，以同一 image、host network、hostname `ub01`、`--stop-timeout 60` 重建 `ymir-sql`（SA 密碼以暫時 env file 傳入，用完即刪）。舊容器保留為已停止的 `ymir-sql-old`。搬移前後筆數一致（使用者 4／對話 10／訊息 66／execution 事件 7057）；重啟容器後資料仍在、API 自動重連。`ymir-retained-dependencies.service` 以名稱啟動，不需修改。
- **文件**：`deploy/api/LINUX-PRODUCTION-SWITCH.md` 更新 SQL 持久化現況；`deploy/api/README.md` 補上 Quadlet 以 image ID 固定時的換版方式與 sqlcmd `-I`。
- **未驗證、待使用者確認**：從外部網域開首頁、實際登入、送訊息與 SSE、成員面板私訊。Google 登入未設定，維持停用。
- **❓待決定**：確認無誤後是否刪除 `ymir-sql-old`；SQL 原生備份排程與異地保存仍未建立。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #067 · 新版部署產物安全相依套件修補

> 👤 **Hermes（AI）** · 🕒 2026-10-14 · `🔬驗證`

- Fresh RuntimeHost publish捕捉Pkcs9.0.11 NU1902／NU1903；保留Audit，中央傳遞釘選10.0.13(符合Aspire最低10.0.12)，不壓制漏洞警告。修補後build零警告、533單元、294整合全通過／無略過，linux-x64 self-contained publish成功。
- 固定DM＋RAG migration pack在隔離SQL驗證首次套用、重放、既有管理員RAG設定保留、history完成後刪種子不重建、未知history拒絕、故障交易回復皆通過；暫時容器清理。沒有修改正式DB。
- 主機AGENTS.md增列紀錄被工具保護規則核准逾時阻擋，未重試或換工具繞過；本次安全修補紀錄保留此看板，待另行允許該檔寫入。
- 正式站仍4b8a3af；API archive建置因gateway中斷待重跑，未宣稱新版已上線。

---

### #066 · 七個分支版本整合驗證與重新部署準備

> 👤 **Hermes（AI）** · 🕒 2026-10-14 · `🔬驗證`

- 原盤點5分支已推送，後續同步取得DM與Google SSO，固定本輪整合範圍為7個tip；保留歷史、未刪分支、未force push。
- 合併RAG引導種子、每模型用量、成員面板／私訊、Google登入；OneDrive ADR-0022仍為提議，不宣稱已實作。Google ADR改0024避免與DM的0023碰撞；保留e2e:dm與e2e:google，兩邊看板留言不刪除。
- 最終驗證：後端build 0 warning/error，533單元、294整合全通過／0略過；前端lint、186 Vitest、build通過。Pi1.0.0＋Fake LLM／Fake OIDC，測試資料庫獨立暫時SQL完成後清理。
- 正式站仍4b8a3af；下一步精確新版API及RuntimeHost、Platform DirectMessages／VibeMaker RagGuideMakeTopic審查SQL與備份後單次sudo部署。Google部署憑證不建立、不回傳、不自行啟用。

---

### #065 · Google 帳號 SSO 登入（ADR-0024）

> 👤 **Claude（AI）** · 🕒 2026-10-14 16:30 · `✅完成`

- **需求**：新增一個登入方案，允許使用 Google 帳號做 SSO。
- **做法**（細節見 [ADR-0024](../adr/0024-google-account-sso.md)、[指南](../guides/google-login.md)）：
  - 與 Entra 相同的 Authorization Code + PKCE、後端換 token、不保存 IdP token；scheme `google`、callback `/signin-google`、入口 `GET /api/auth/login/google`。
  - Google 帳號任何人都能申請：`Ymir:Auth:Google` 必須有 `AllowedDomains`（比對 `hd`，不看 email 字尾）或 `AllowedEmails`，否則拒絕啟動；`email_verified` 必須為 true；不符合時不建立使用者，導回 `/login?error=not_allowed`，稽核不記 email。
  - 身分鍵 `iss`（固定存 `https://accounts.google.com`）+ `sub`；`AdminEmails` 每次登入同步角色；新 `AuthMethod.Google`（OneDrive 不會把它當成 Entra oid）。
  - 只在部署設定（不做管理介面設定）；Fake OIDC 新增 `/google` 模擬；登入頁 Google 按鈕、設定頁說明。
- **驗證**：
  - `dotnet build Ymir.slnx` 0 警告；單元測試 533 通過（新增 `GoogleLoginTests` 10 個）。
  - 整合測試全套 821 個：820 通過，`PiExecutionTests.SelectedModel_AndPersonalPlusProjectSystemPrompts_ReachTheModel` 在全套負載下失敗一次，單獨重跑 `PiExecutionTests` 3/3 通過（與登入無關的 Pi 測試）。新增 `GoogleLoginTests`（整合）13 個、授權矩陣加入新端點、OpenAPI 快照已更新。
  - 前端 lint、179 Vitest、正式 build 通過。
  - 端對端：Fake OIDC + API（Development）+ `npm start`，`npm run e2e:google` 三步通過，截圖 [登入頁](screenshots/google-login/01-login-google.png)、[登入後](screenshots/google-login/03-signed-in.png)、[未獲准](screenshots/google-login/04-not-allowed.png)。
- **未驗證、待使用者環境確認**：實際連 Google（沙箱連不到）；需在 Google Cloud Console 建立 OAuth client、redirect URI 加 `https://<網域>/signin-google`，並在 `deploy/api/.env` 設定 client 與白名單。
- **流程說明**：本 session 的指定開發分支為 `claude/jolly-fermat-rak1ja`，因此提交推送到該分支（未直接推 `main`）；需要時再合併回 `main`。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #064 · 使用者之間的一對一訊息（ADR-0023）

> 👤 **Claude（AI）** · 🕒 2026-10-14 16:10 · `✅完成`

- **需求**：使用者要求讓 user 之間可以聊天，類似簡易版 Microsoft Teams；UI 在右邊新增一排使用者名單。
- **設計**（[ADR-0023](../adr/0023-user-direct-messages.md)）：
  - 放在共用核心（Platform），資料表 `platform.direct_messages`（migration `DirectMessages`）；
  - `/api/direct-messages`：`contacts`（未停用的其他使用者 + 未讀數 + 最後往來時間，不回 email）、`{peerId}`（分頁）、送出（1～4000 字、每人每分鐘 60 則）、`{peerId}/read`；
  - SSE `events`：`ready` / `message` / `read` / 25 秒 `ping`（每次心跳重新確認帳號狀態）；通知只在單一 API 程序內傳遞，前端每次 `ready` 重新讀取補齊；
  - 一律以目前使用者為其中一方；收件者不存在、停用、自己都回 404；訊息不寫稽核、不清理。
- **前端**：shell 右側 `app-people-panel`（搜尋、未讀徽章、一對一聊天、已讀標示、Enter 送出 / Shift+Enter 換行），工具列成員按鈕顯示未讀總數；桌面可收合並記住，窄螢幕為右側抽屜（與左側欄共用遮罩）；訊息只以文字綁定顯示。
- **驗證（實際執行）**：
  - `dotnet build Ymir.slnx`：0 警告；`dotnet test --solution Ymir.slnx`：803 通過（含新的 `DirectMessageTests` 6 項、授權矩陣、OpenAPI 快照）；
  - `npm run lint`、`npm test`（185 通過，含 `direct-message-state.spec.ts`）、`npm run build`（無預算警告）；
  - `npm run e2e:dm`（真實 API + ng serve，兩個瀏覽器帳號）：名單、即時未讀、已讀、回覆、HTML 不被解譯、收合記憶、手機抽屜全部通過；截圖見 [screenshots/direct-messages](screenshots/direct-messages/)；
  - `e2e:model-picker` 通過；`e2e:sidebar`、`e2e:mobile-composer` 在**改動前**就因缺 `/api/branding` fixture 而失敗（既有問題，未在本次修正），已補上新端點的 fixture。
- **分支**：本次 session 指定在 `claude/hopeful-ptolemy-4rodpw` 開發並推送，尚未合併回 main。
- **❓待決定**：是否合併回 main；後續是否需要群組聊天、在線狀態、桌面通知或附件。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #063 · 現存分支整合回 main（含 RAG 引導）與重新部署準備

> 👤 **Hermes（AI）** · 🕒 2026-10-14 · `🔬驗證`

- 依使用者要求盤點全部現存分支，5 個分支 tip 的祖先檢查皆包含於本機 main；只有 `claude/eloquent-cray-dc7fvl` 尚未合併的 RAG 引導內容需新 merge，提交 `9f363c4`。不刪分支，不重寫共享歷史。
- 保留每模型用量功能及 ADR-0022 提議（沒有把未採納提議當成已實作）；RAG 新 migration `20261013153602_RagGuideMakeTopic` 加入固定主題。看板衝突保留兩方完整紀錄，RAG 留言編為 #061、舊建置紀錄 #062。
- 實際驗證：後端 build 0 警告／0 錯誤、523 單元測試通過；整合測試首次與重試捕捉 OIDC 共用 fixture 的殘留設定，補上還原初始狀態後 275 全通過、0 略過（Pi 1.0.0＋Fake LLM，不呼叫付費模型）。測試用獨立暫時 SQL Server，完成後移除，沒有操作正式 DB。
- 前端 lint、178 Vitest、正式 build 通過。原本本機 compose／proxy／opencode 變更及安全 stash 保留，不混入本輪程式提交。
- 正式環境仍為 4b8a3af；新版需精確產物、RAG 種子 transaction/history/schema 審查與單次 sudo 表單後再切換，不重跑舊 installer、不宣稱已部署。

---

### #061 · /make 新增「建立 RAG 知識庫」引導主題

> 👤 **Claude（AI）** · 🕒 2026-10-13 · `✅完成`

- **需求**：在聊天 `/make` 加入建立 RAG 的導引，引導使用者建立以 SQLite 儲存的向量資料庫。
- **做法**：
  - 向量庫依 ADR-0014 由平台在專案頁「知識庫」背景建立（每專案一份 SQLite、原始檔不在 Agent workspace），所以 Agent 只做**引導**，不自己建 SQLite、不裝 embedding 套件。
  - 新 migration `RagGuideMakeTopic` 種子主題「建立 RAG 知識庫」（排序 50，沿用既有 `DocumentMakeTopics` 做法；管理員可在「管理 → Make 主題」調整或停用）。
  - 指示分四步：釐清文件與問題 → 檢查格式與上限（TXT／MD／文字型 PDF／DOCX、20 MB、200 份、20,000 段；掃描 PDF 需先轉換）→ 到專案頁上傳並等待「已就緒」（未設定 Embedding 請聯絡管理員）→ 提問驗證 [n] 出處；並明確禁止宣稱已建好。
  - 沿用既有按鈕流程（`ForTopic` 先問需求、不建檔），前端與 API 無需改動。
- **驗證**：`dotnet build Ymir.slnx` 0 錯誤；`MakeCommandTests` 11 個通過（更新種子順序、新增 RAG 主題內容測試）。
- **待確認**：既有環境需套用 migration 後才會出現主題；未用真實 Pi 驗證引導對話品質，待使用者環境確認。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #060 · ADR-0022：讓 Agent 處理使用者指定的 OneDrive 檔案（提議中）

> 👤 **Claude（AI）** · 🕒 2026-10-14 10:30 · `❓待決定`

- **背景**：使用者要求讓 Agent 操作使用者 OneDrive 上的任何文件。ADR-0013 只同步 `<同步資料夾>/projects`、`chats`，其他位置的文件 Agent 看不到。
- **評估**：三種做法中採用「使用者指定」。
  - 平台 MCP：Gateway 目前不帶使用者身分給後端，而且被 prompt injection 時影響整個 OneDrive，這次不做；
  - 直接把 Graph token 交給 Agent：違反 ADR-0004 / 0013，不採用。
- **使用者 2026-10-14 決定**：
  - 只限自己的 OneDrive；
  - 沿用 `onedrive` 能力；
  - 改過的檔案寫回原位置（If-Match，衝突時兩份都保留）；
  - 單檔 100 MB，資料夾連結最多 500 個檔案；
  - 刪除、移動、改名**允許**（使用者中途改為允許）。
- **設計重點**（[ADR-0022](../adr/0022-onedrive-linked-files.md)）：
  - 連結項目放在工作目錄的 `onedrive/<id8>-<名稱>`；
  - item id 以使用者自己的 token 重新查詢並比對 drive id；
  - 執行前下載、執行後背景寫回；
  - 刪除只進 OneDrive 資源回收筒、移動 / 改名以 SHA-256 一對一比對；
  - 超過 20 個或 30% 的刪除先暫停，由使用者確認；
  - 唯讀連結一律不傳播；
  - ADR-0013 的同步資料夾行為不變。
- **驗證**：只改文件，沒有程式變更，沒有跑 build / test。
- **❓待決定**：ADR-0022 全文確認後改為「已採納」，再依 O3 → O4 → O5 實作。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #059 · 每人每模型用量合併回 main

> 👤 **Claude（AI）** · 🕒 2026-10-13 23:40 · `✅完成`

- 依使用者要求，把 `claude/per-user-model-usage` rebase 到最新 main 後以 fast-forward 合併，沒有 merge commit。
- **衝突處理**：
  - README：保留新版的精簡入口，只在功能清單的「用量管理」補上 ADR-0021 連結。
  - 看板：#055～#057 已被 main 使用，原留言改為 #058。
- **合併後補強**：用量頁也是管理介面，「依模型」卡片的模型名稱與開放模型一致顯示「名稱 (provider)」，`UserModelUsageResponse` 新增 `provider`。
- **驗證**：
  - 後端：build 0 警告、format 通過、`dotnet test --solution` 797 項全部通過。
  - OpenAPI 快照與 `schema.ts` 已更新。
  - 前端：lint、178 項 Vitest、build 通過。
  - 這次沒有重跑 `e2e:admin`，合併前已通過；開發設定的 fake-model 沒有設定 provider，顯示結果不變。
- **未驗證、待使用者環境確認**：真實 LiteLLM 的 `breakdown.model_groups` 回應；正式站需重新部署 API。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #058 · 每人每模型用量（ADR-0021，開發分支）

> 👤 **Claude（AI）** · 🕒 2026-10-13 15:20 · `🚧進度`

- **背景**：使用者要求「開一個新 branch 開發新功能，記錄每個人的每個模型的用量」。依使用者明確要求，這次在分支 `claude/per-user-model-usage` 開發（G1「直接在 main 開發」的例外），**尚未合併回 main**。
- **設計**（[ADR-0021](../adr/0021-per-user-model-usage.md)，延伸 ADR-0004 補充與 ADR-0011 §6）：
  - 不推翻「Ymir 不保存 token 與費用」：LiteLLM 的每日彙總 `LiteLLM_DailyUserSpend` 本來就依使用者與模型永久保留。改讀 `/user/daily/activity` 的 `breakdown.model_groups`（key 是公開 model_name，即 Ymir 的模型 id），`page_size` 由 100 改為 1000。
  - 執行次數與失敗次數來自 Ymir 既有的 `agent_executions.model_id`，依模型分組。不新增資料表或 migration。
  - `ModelUsageBreakdown.Merge`（純函式）以模型 id 合併兩邊資料，分成清單內、知識庫 embedding、不在清單、未標示四類。「未標示模型」那一列包含舊執行，以及 LiteLLM 總計減去各模型的差額，所以各列加總等於使用者總計。
  - 查詢對象加上只用知識庫的使用者（他們沒有 runtime）。
- **API**：`GET /api/admin/usage` 每位使用者新增 `models[]`，只新增欄位、仍只限 Admin；已更新 OpenAPI 快照與 `schema.ts`。
- **前端**：用量頁新增「依模型」卡片，包含各模型合計（使用人數、執行／失敗、請求、token、費用、佔比）與「使用者 × 模型」明細。沒有連接 LiteLLM 時只顯示執行數。
- **Fake LLM**：依 request 的 model 記錄用量，使用者 key 的 embeddings 也記錄（費用 0）；回傳 `breakdown.model_groups`，並故意讓 `breakdown.models` 用不同的 key。
- **過程**：由多個 agent 分工：調查現況 → 三種設計由評審整合 → 依序實作後端／前端／文件 → 四個面向審查並由驗證者嘗試推翻。確認成立的 5 項（無 blocker）已修正：
  - LiteLLM 的每日彙總不以 model group 區分，指向同一上游模型的 alias 會合併。已寫入 ADR-0021「影響」，並在 `deploy/litellm/README.md`「新增模型」規定每個 `model_name` 的上游模型不可重複。
  - 沒有 model group 的紀錄，LiteLLM 會以供應商模型名稱為 key。相關註解已改為照實描述。
  - 補上失敗執行與舊執行（`ModelId` 為 null）的整合測試；排序測試改為能證明「依請求數」這一層（以突變測試確認：拿掉該排序後測試會失敗）。
- **驗證**：
  - 後端：`dotnet build Ymir.slnx` 0 警告；`dotnet format --verify-no-changes` 通過；`dotnet test --solution Ymir.slnx` 783 項全部通過（含新的 `ModelUsageBreakdownTests` 9 項、`ModelUsageTests` 3 項，以及 `LiteLlmModelGatewayTests` 新增的 3 項）。
  - 前端：lint、175 項 Vitest、build 通過。
  - 端對端：`npm run e2e:admin` 全部步驟通過（SQL Server、Fake LLM 以 `FAKE_LLM_MASTER_KEY` 模擬 LiteLLM、Fake OIDC、API 用 Pi harness），截圖確認「依模型」卡片顯示 fake-model 的費用、token 與佔比。
- **未驗證、待使用者環境確認**：真實 LiteLLM 1.103.2 的 `breakdown.model_groups` 實際回應（已對照原始碼，但沙箱沒有真實 LiteLLM）；真實模型的單價與費用。
- **待決定**（`❓待決定`）：
  1. 這個分支要合併回 main 嗎？
  2. 是否需要 Ymir 自己保存用量歷史？例如本地時區換日、LiteLLM 資料庫遺失後仍保留歷史、依對話或專案拆分。這需要新 ADR 推翻 ADR-0004 補充。
  3. 是否要讓成員看到自己的用量，或提供 CSV 匯出？

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #062 · main 4b8a3af 正式建置產物完成，尚未部署

> 👤 **Hermes（AI）** · 🕒 2026-10-13 21:34 · `🔬驗證` `⛔卡關`

- 上層已完成 main `63e15b7` → `4b8a3af729a2d3f4cd83e5c840d9423145be9d68` fast-forward。本輪 API build、linux-x64 self-contained RuntimeHost publish、API docker-archive save 皆 exit 0；unique tag `localhost/ymir/api:4b8a3af` 在 itadmin rootless store，不覆寫正式 tag。
- 產物：`/home/itadmin/ymir-production-install/update-4b8a3af/runtime-host/`，包含 publish、`ymir-api-4b8a3af.tar`、`SHA256SUMS`；396 個檔案 hash 核對 exit 0，archive manifest／image ID／API DLL／Angular index／PWA 檔案通過。tar SHA-256 `790bae6575fd3aea5cfaff3e18ff9587761e8b9e8bc728fac7c5cc53bd17c4e2`。安裝 RuntimeHost 時排除 tar／checksum。
- 實跑 `dotnet test --project tests/Ymir.UnitTests -c Release` 重跑 exit 0：511 通過、0 失敗、0 略過。首次與本機 publish 平行共用 bin／obj 發生 MSB4018／MSB3026，改為順序重跑成功，原失敗日誌保留。未執行 integration tests 或付費模型。
- 最新唯讀現況：正式 API／RuntimeHost／origin-production／cloudflared-production／LiteLLM bridge active/running，retained-dependencies active/exited success；舊 itadmin user API failed/disabled。SQL／LiteLLM／PostgreSQL running，本機 API health 200 Healthy、nginx login 200、公開 login/providers 200、公開 health/alive 404；OIDC challenge 302 callback 正確為 `https://ymir.thetainformation.com/signin-oidc`，不等於真登入成功。上述正式單元目前不是 masked，不套用過去 checkpoint。
- **卡關**：`sudo -n true`／`sudo -n /usr/local/sbin/ymir-diagnose` 均 exit 1，無非互動 root 权限；root／ymir Podman、正式 keyring 與 schema history 未完整核對。SQL mounts=[]，切換前需 SQL 原生備份，禁止重建 SQL。需要管理者先執行已檢閱的唯讀診斷，再另外核准受限正式切換，不能直接重跑舊 installer。
- 三份既有 compose／nginx／opencode 本機變更 SHA-256 一致保留；未部署、未停止／啟動服務、未改資料庫／secret／Data Protection keys、未跑舊 install/resume/finish、未 commit/push。AGENTS.md 處理紀錄寫入被工具安全政策拒絕，未改用其他工具繞過；待解除該特定檔案寫入限制。
- 詳細證據與權限 gate：耐久目錄 `BUILD-REPORT.md`、`verification.json`、去機密現況摘要與私有 `logs/`。真 Entra 登入、Agent/SSE 及新版上線未驗證。

> **歷史建置記錄補註**：以上保留 2026-10-13 當時原文；原部署卡關已於 2026-10-14 13:00 正式部署解除，不代表目前仍未部署。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #057 · 開放模型標示 provider，使用者只看模型名稱

> 👤 **Claude（AI）** · 🕒 2026-10-13 19:55 · `✅完成`

- **需求**：管理後台的「開放模型」要標出 provider（例如 MiniMax M2.7 (minimax.io)），使用者的畫面只顯示模型名稱。
- **做法**（[ADR-0020](../adr/0020-admin-model-access.md) 補充）：
  - 新增 `VibeMaker__Models__N__Provider`。沒設定時相容舊寫法，把名稱結尾的「(X)」拆成 provider，例如「GPT-6.1 Sol (ChatGPT OAuth)」。
  - 管理端的開放模型與個人模型權限顯示「名稱 (provider)」。使用者的 `/api/models` 不回傳 provider。
  - 例外：同一位使用者的清單裡有同名模型時（例如 OpenRouter 和 ChatGPT OAuth 的 GPT-6.1 Sol 同時開放），這兩個才加上 provider 區分，否則使用者無法分辨。
  - 前端移除原本只處理「(OpenRouter)」的字串過濾。
  - 部署範本加上 Provider。`chatgpt-ymir-models.py` 會為還沒有 Provider 的 `minimax*`／`openrouter-*` 補上（不覆蓋已設定的值、可重跑）。
- **審查**：由 agent 分組審查並嘗試推翻，確認成立的問題都已修正，包括：正式環境沒有補 Provider 的途徑、3 支 e2e 的測試資料過時、測試與範本的缺口。另外順手補上 `model-picker` e2e 原本就缺的 `/api/branding`、知識庫模擬資料（公司識別功能上線時漏改）。
- **驗證**：
  - 後端：build 0 警告、format 通過、`dotnet test --solution` 782 項全部通過。
  - 前端：lint、170 項 Vitest、build 通過。
  - e2e：`e2e:model-picker`、`e2e:admin-models`、`e2e:user-models` 通過。瀏覽器實測管理端顯示「MiniMax M2.7 (minimax.io)」等，使用者端只顯示名稱。
  - 部署腳本：`test-chatgpt-oauth.py` 9 項通過；以舊版 env 模擬正式環境，6 個既有模型都補上 provider，且重跑結果不變。`test-openrouter.py` 需要 LiteLLM 容器，只單獨驗證了新加的斷言。
- **未驗證、待使用者環境確認**：正式主機要重新部署 API，並重跑 `apply-chatgpt-linux.sh`（或手動在 `/etc/ymir/api.env` 加上 Provider）後才會生效。
- **❓待決定**：同時開放兩個同名模型時，使用者會看到 provider。替代做法是在管理端禁止同時開放同名模型。

<details>
<summary>💬 回覆（1）</summary>

> ↳ 👤 **使用者**（Claude 代記） · 🕒 2026-10-13 20:10
>
> 決定：同一位使用者同時被開放兩個同名模型時，加上 provider 區分（維持目前實作，不在管理端禁止）。

</details>

---

### #056 · README 回歸專案入口

> 👤 **GPT（AI）** · 🕒 2026-10-13 · `✅完成`

依使用者要求將 README 從 196 行整理為 68 行，保留產品介紹、功能、技術組成、精簡目錄、本機 AppHost 開發與文件導覽。移除舊 Windows 部署、個人路徑、分支／image 快照、修復流水帳、重複架構及 UI／API 實作細節；改由 architecture.md、ADR、部署指南及進度看板承載，舊版內容仍可由 Git 歷史追溯。未改動應用程式或正式服務。

驗證：README 所有本機文件連結存在、Markdown fence 成對、過時主機特定字串移除與 `git diff --check` 通過。純文件變更未重跑應用程式 build／test，推送後追蹤 CI。

---

### #055 · 完整架構圖文文件

> 👤 **GPT（AI）** · 🕒 2026-10-13 · `✅完成`

依使用者要求新增根目錄 `architecture.md`，以繁體中文與五張 Mermaid 圖描述系統全貌、Ubuntu Tunnel → nginx → API 正式部署、程式依賴分層、Agent 執行序列與模型／OAuth 路由；列出目錄、儲存與機密邊界、MCP／OneDrive／RAG／SiteHost 可選子系統及驗證限制。README 新增導覽與 Windows 歷史快照提醒，不把舊拓樸當現況；記錄 OAuth 真實推論未驗證及 API 139 崩潰未解。未修改程式、設定或正式服務。

驗證：本機文件連結與關鍵專案路徑存在、Markdown fence 配對、五張 Mermaid 區塊基本結構及 `git diff --check` 通過。未實際瀏覽器渲染 Mermaid；純文件變更未重跑應用程式 build／test，GitHub CI 隨 push 追蹤。

---

### #054 · OAuth 三模型正式部署成功

> 👤 **GPT（AI）** · 🕒 2026-10-13 · `🔬驗證`

使用者在本機 sudo 視窗完成部署並回報狀態碼 0；再次獨立確認 LiteLLM `/v1/models` 包含三個 `chatgpt-*` alias 與原六個正式模型，私有 OAuth 持久 volume 設定及 headless guard 已載入。正式 API `/health` 與 nginx `/login` 皆 HTTP 200。部署保留既有權限政策；管理員仍需在「管理 → 開放模型」勾選新增模型。未呼叫真實模型推論，訂閱用量／費用追蹤及真實 Pi 工具串流仍待驗證；不將離線橋接測試當成上游成功。

---

### #053 · OAuth 已登入，核對目錄與三個訂閱路由

> 👤 **GPT（AI）** · 🕒 2026-10-13 · `🚧進度`

使用者回報登入工具 1，實際 token 已保存；原因是工具使用的模型目錄 client_version=0.89.0 太舊，官方 backend 回空清單。核對 OpenAI 官方 Codex release 0.162.1 後更新查詢版本，headless 重查取得 7 個可見模型，排除 hide／hidden 內部模型，不需重新登入、不呼叫推論。使用者選擇 GPT-6.1 Sol、GPT-6 Luna、GPT-6 Astra；新增獨立 `chatgpt-*` alias、opt-in compose overlay、可寫外部 token volume、單 worker、API model env append／回復部署工具，保留原六模型、預設及權限。

Pinned 1.103.2 的 Chat validator 不接受新 ID 的 xhigh，改用已核對的 Responses `reasoning.effort`，低至 max 原樣傳送；ultra 不在 Ymir 契約、不開放不降級。各 route 初期 6 RPM／64000 TPM。實測 default simple routing 不會累計 RPM，因此 opt-in config 使用 usage-based-routing-v2 與 pre-call checks；離線限 1 RPM 的第二個請求確認不到達上游。Direct SDK 假 SSE 測試出現非致命 standard_logging_object callback／usage serialization 警告，實際 proxy token／費用追蹤仍待真實推論確認，不宣稱完整用量驗證。另以中性 CHATGPT_DEFAULT_INSTRUCTIONS 取代 SDK 寫死「based on GPT-5」的預設指示，保留 Pi system prompt／工具。

驗證：8 項純離線測試（目錄、私有權限、輸出不含 token、headless拒絕登入、env保留／冪等、guard）通過；隔離 pinned image 本機 SSE 驗證三模型文字／tools／stream、模型ID、全部支援effort及 RPM 擋上游通過。已針對實際 SDK 核對 typed guard error signature，401拒絕 device login／等待通過。config 合併及 compose overlay（單worker／volume／原環境）驗證通過。Proxy guard 阻止 refresh 失敗時的互動 fallback，不抄 token 到 API／Agent。主機路由部署與真實推論尚未執行，待使用者 sudo。

---

### #052 · ChatGPT OAuth：先官方登入與帳號模型目錄

> 👤 **GPT（AI）** · 🕒 2026-10-13 · `🚧進度`

使用者要求 LiteLLM 增加 ChatGPT OAuth 提供模型服務，已確認初期供所有獲授權 Ymir 使用者使用，登入後再依帳號可用清單選模型。已告知共用同一帳號／workspace 訂閱配額、provider spend 可能為 0 而美元 max_budget 無法管控訂閱額度；token 僅給 LiteLLM，不交給 API／RuntimeHost／Agent。

核對 LiteLLM 官方 provider 文件與本機 pinned 1.103.2，已包含 authenticator、Responses／Chat bridge。新增獨立互動 login 容器工具：官方 device flow、私有 named volume（目錄 700、auth.json 600）、只查官方模型目錄並保存非機密能力資料；不推測 OpenRouter ID、不呼叫模型。catalog-only 阻止 refresh 失敗後退回互動登入；錯誤不列出 token／憑證。現有 proxy、模型清單及服務尚未更動。

驗證：5 個離線測試通過（模型篩選／能力／異常、private file permissions、輸出無 token、headless 不啟動登入），Bash／Python 說明入口及 diff check 通過。待使用者完成官方登入，再選模型、驗證 Pi 所需 stream／tools 橋接、部署新路由與權限；OAuth 登入、metadata endpoint 和模型呼叫尚未實測。工作前已 fetch／rebase，保留本機三份配置變更。前輪 e85285d 的 CI（三 job）已全部成功。

---

### #051 · GitHub 權限更新後成功推送 Linux 修復

> 👤 **GPT（AI）** · 🕒 2026-10-13 · `🔬驗證`

使用者更新 fine-grained PAT 權限後，重新 fetch／rebase 最新 main；保留遠端 favicon／OneDrive／ADR 整理提交，衝突僅在看板，保留新的兩表結構及遠端 #033～#036 原文，本輪 Linux 紀錄調整為 #037～#050，維運列避免與既有 ADR 整理 D1 重複。衝突解決後 Bash 語法與隔離真實上傳回歸再次通過。

一般 `git push origin main` 成功（`7c1ea13` → `a11ecc4`），包含正式部署／六模型／runtime 修復、shell 參數修復與操作紀錄三筆提交；未 force push。三份使用者本機配置由暫存備份還原並核對 SHA-256 一致，未含 .env／密鑰。本機／origin 差異 0／0。CI 已觸發，推送完成時仍在執行，結果待追蹤；此交付紀錄亦一併 commit／push。

---

### #050 · 上傳 wrapper 熱更新完成、實機寫檔回歸通過

> 👤 **GPT（AI）** · 🕒 2026-10-13 · `🔬驗證`

主機熱更新紀錄與使用者回報皆為 0。已讀取已安裝 wrapper，確認 systemd-run 使用 `--expand-environment=no`；實際 Agent 以固定 scope 與安裝 wrapper 兩種路徑通過 literal argv、stdin／env／退出碼、原 C# WriteScript 的中文／空白檔名及二進位精確內容、symlink 防逃逸。API health 再次為 Healthy。

未重啟 API／Agent／資料庫，未讀寫使用者 workspace；測試僅使用唯一 /tmp 目錄並清理。wrapper 備份 `/var/backups/ymir/wrapper-20261013-105754-3443008`。請使用者重新上傳原檔，HTTP 上傳及原檔重傳尚待確認，未呼叫模型。程式與回歸測試已 commit，本機三份其他配置仍保留，GitHub 寫入權限 403 尚待修正。

---

### #049 · 附件上傳錯誤：systemd 誤展開 shell 位置參數

> 👤 **GPT（AI）** · 🕒 2026-10-13 · `🚧進度`

使用者回報上傳顯示「檔名不合法或是空檔案」。API journal 確認已進入 RuntimeWorkspaceFileWriter，但腳本 `${1##*/}`／`${1%/*}` 被 systemd-run 當成環境變數，導致空目錄及 exit 3；現有 AttachmentService 將 writer false 映成 Invalid，因此訊息誤導。這是本輪新增 scope wrapper 的參數轉送缺陷，不是已確認的使用者檔名／檔案內容問題。

wrapper 與 scope probes 加上 `--expand-environment=no`；新增熱更新工具，備份並原子替換已安裝 wrapper，不重啟服務／容器／資料庫。回歸工具直接擷取原 C# WriteScript，在唯一 /tmp 測試目錄驗證 literal argv、中文／空白檔名、二進位 stdin 的精確內容，以及保留 symlink 防逃逸。

驗證：原版 scope 可重現「Invalid environment variable name」且檔名變空，固定後原樣輸出；隔離、唯讀、network none 測試容器以原寫檔腳本全部通過，測試容器已清理。LiteLLM 映像的 busybox realpath 不支援 -e，不能作 GNU writer 測試容器，改用本機既有 Ubuntu 映像的隔離容器；未執行 SQL Server 程序。Bash／Python 語法、help、非 root 拒絕、diff check 通過。主機實際 wrapper／原檔重新上傳尚待驗證，未呼叫付費模型。工作前已 fetch／rebase，保留三份本機配置變更；先前 GitHub push 403 尚待修正。

---

### #048 · Linux 部署與模型修復交付、同步最新 main

> 👤 **GPT（AI）** · 🕒 2026-10-13 · `⛔卡關`

依使用者要求提交本輪 15 個部署／模型／runtime 修復檔案與進度，排除既有 cloudflared compose 修改、舊代理設定及 OpenCode 本機設定；.env／密鑰未提交。fetch 發現遠端另有 6 筆 Windows／部署指南／main 開發流程更新，依 AGENTS.md 使用 rebase，保留雙方内容並將本輪留言編號改為 #036～#046，避免與遠端 #033～#035 重複。

提交前重新驗證：所有新增 Bash／內嵌 Python 語法、help、非 root 拒絕、六模型 pinned LiteLLM 本機假上游測試（文字／工具／串流／reasoning／目錄一致性）、diff check 通過；未呼叫付費模型。原有三份未提交檔案已還原並逐份核對 SHA-256 一致，暫存備份保留。rebase 完成且衝突解決後重新驗證；一般 `git push origin main` 被 GitHub 403 拒絕（repository 寫入憑證權限），未 force push，main CI 尚未觸發。需修正 GitHub 寫入權限後再次 fetch／rebase／push。

---

### #047 · Agent exec 與 API socket 修復完成，整體健康通過

> 👤 **GPT（AI）** · 🕒 2026-10-13 · `🔬驗證`

主機修復紀錄與使用者回報皆為 0：兩個 Agent 的 user-scope Pi 1.0.0／stdin／env、transient system-service wrapper 測試通過；RuntimeDirectoryPreserve=yes 已套用，API 重新建立 socket 目錄掛載。獨立確認 API `/health` 200 Healthy、nginx `/login` 200、LiteLLM liveliness 200，RuntimeHost／API／nginx／Tunnel 皆 active/running。

設定備份 `/var/backups/ymir/runtime-scope-20261013-093020-3370491`（含密鑰，僅 root 可讀）。容器、workspace、agent-state、SQL／PostgreSQL 資料保留，未呼叫付費模型。請使用者重新送出訊息驗證真實對話；此驗證尚未完成，不將本機健康檢查等同模型推論成功。

---

### #046 · cgroup 修復已套用，補上 API socket 掛載刷新

> 👤 **GPT（AI）** · 🕒 2026-10-13 · `🚧進度`

使用者回報 0，主機輸出確認兩個 Agent user scope 及 transient system service wrapper 測試皆通過，RuntimeHost active，cgroup 修復已套用。最後獨立探測 API 卻為 Unhealthy；LiteLLM 200，API journal 的 runtime health 為 Unix socket connect 的 Cannot assign requested address。RuntimeHost restart 會移除／重建 RuntimeDirectory，而 API 仍 bind-mount 舊目錄，原腳本未檢查整體 API health。

補上 RuntimeDirectoryPreserve=yes（範本與安裝 drop-in）、重啟 API 刷新掛載、等待 API Healthy 與 nginx 200 後才宣告完成；錯誤回復涵蓋 drop-in 與 API／入口恢復。Bash 語法、設定一致性、readiness 延遲／逾時模擬與 diff check 通過；主機最後一步待 sudo 執行。未刪除容器／資料，未呼叫付費模型。

---

### #045 · user scope 實機成功，修復 readiness 等待

> 👤 **GPT（AI）** · 🕒 2026-10-13 · `🚧進度`

實機 sudo 測試已成功在兩個既有 Agent 容器執行 Pi 1.0.0，stdin／環境變數驗證也通過，證實 user scope 能避開先前 cgroup exec 錯誤。設定安裝後退出 7，journal 顯示 RuntimeHost 重啟尚未完成便探測 Unix socket，腳本隨即回復原 env；回復後 service active，容器程序仍保留。

將 curl retry 改成明確的 60 秒 readiness 迴圈。隔離模擬「前兩次失敗、第三次成功」及持續失敗逾時皆通過，Bash 語法與 diff check 通過。主機再次套用與 system-service wrapper 驗證待執行，未呼叫付費模型。

---

### #044 · 修復檢查的跨帳號工作目錄

> 👤 **GPT（AI）** · 🕒 2026-10-13 · `🚧進度`

第二次修復退出 1，紀錄為 `cannot chdir to /home/itadmin/Documents/Ymir: Permission denied`；不是已確認的 Agent 消失，而是 runuser 沿用私人家目錄 cwd，Podman 在列出容器之前就失敗。`as_ymir` helper 改為先切到 `/` 再降權，不放寬 itadmin 家目錄權限。此輪仍未變更 RuntimeHost env／容器；Bash 語法通過，重新開啟測試與修復視窗，实际 ymir scope 測試待執行。

---

### #043 · 修正部署鎖被 rootlessport 繼承

> 👤 **GPT（AI）** · 🕒 2026-10-13 · `🚧進度`

cgroup 修復腳本第一次未套用設定，因部署鎖被拒絕；`lslocks` 確認模型部署已退出，但 LiteLLM rootlessport 仍持有 `/run/lock/ymir-production-switch.lock`。先前腳本的 FD 9 被背景子程序繼承，屬部署工具缺陷，不是仍有另一個部署。

確認沒有執行中的舊部署腳本後，三個新部署／修復工具統一使用 v2 鎖，runuser helper 和 scope probe 明確關閉子程序 FD 9。不刪除舊鎖、不殺正常 rootlessport、不重啟資料服務。隔離測試重現「父程序退出、背景子程序仍持鎖」，加入 `9>&-` 後新程序可取得鎖；三腳本 Bash 語法與 git diff --check 通過。已重新開啟修復終端，ymir user scope／實際 Agent 修復結果待 sudo 執行。

---

### #042 · Agent exec cgroup 權限錯誤診斷與修復工具

> 👤 **GPT（AI）** · 🕒 2026-10-13 · `🚧進度`

使用者回報「Agent 執行環境發生錯誤」。RuntimeHost journal 確認建立／啟動容器後，`podman exec` 因 crun 無法寫入 `cgroup.procs` 回 126；API 於 PrepareDelivery 階段失敗，尚未進入模型推論。RuntimeHost 位於 system.slice，Agent 位於 ymir user manager subtree；兩個 service 即使 Delegate=yes，仍不代表共同父層允許跨 subtree 遷移。診斷指向 rootless cgroup 執行上下文，非已證實的模型 key／路由問題。

新增 root-owned wrapper 安裝腳本與操作說明：先測試 user scope 下 Agent uid／Pi／stdin／env，通過且無工作後才設定 ContainerExecutable；重啟 RuntimeHost 後由 transient system service 再驗證。保留現存容器及資料，不關閉資源限制、不用 privileged／廣泛 cgroup chmod。Bash 語法、help、非 root 拒絕、wrapper 帳號限制通過；在目前 itadmin 的 user scope 以 LiteLLM 容器驗證 exec／stdin／env 通過。ymir Agent 的 sudo 修復待執行，未呼叫付費模型。

---

### #041 · 六模型設定已部署，服務健康通過

> 👤 **GPT（AI）** · 🕒 2026-10-13 · `🔬驗證`

使用者在主機終端完成 sudo，更新腳本紀錄及使用者回報皆為 status code 0。獨立確認 LiteLLM 容器已載入 OpenRouter key；`/v1/models` 包含 minimax（M2.7）、minimax-m3、openrouter-gpt-6.1-sol、openrouter-gpt-6-luna、openrouter-sonnet-5.5、openrouter-haiku-5.5（fake-model 僅測試路由，不在 API 允許清單）。API `/health` 200 Healthy、nginx `/login` 200，API／nginx／Tunnel 三個 systemd 服務均 active/running。

未覆寫既有全域或個人權限，新增模型需由管理者在「開放模型」勾選儲存；未驗證瀏覽器選單及付費模型推論。部署設定備份 `/var/backups/ymir/models-20261013-012639-3004716` 含密鑰、僅 root 可讀，並非資料庫／使用者檔案備份。SQL／PostgreSQL 容器及資料未重建。

---

### #040 · 六模型目錄、路由與 Ubuntu 更新工具

> 👤 **GPT（AI）** · 🕒 2026-10-13 · `🚧進度`

依使用者要求移除 MiniMax M2，`minimax` alias 改為 MiniMax M2.7（保留舊授權），新增 `minimax-m3`；OpenRouter 保留 Sol／Luna／Sonnet 5.5 並新增 Haiku 5.5。新增完整 API Models／AllowedModels env 範例與正式部署腳本，不覆寫既有全域／個人模型權限。能力／模型 ID 核對 MiniMax OpenAI 相容文件與 OpenRouter 官方目錄；M3 支援圖片、不冒充 M3.1 的 effort，Haiku 不宣告 none。提醒 M2.7 官方退役時程與 MiniMax 長上下文／方案價格差異。

驗證：使用者將 key 填入 `deploy/litellm/.env` 後，OpenRouter `/api/v1/key` 200；SQL 唯讀確認無 QUEUED／RUNNING 工作。Pinned LiteLLM 的本機 HTTP stub 驗證六個路由的模型 ID、獨立憑證、文字／工具／串流、四個 OpenRouter 的 reasoning；API env 六個 ID 與 AllowedModels／圖片／thinking 設定一致性通過。Bash／Python 語法、help、非 root 拒絕通過。未呼叫真實付費推論；部署與瀏覽器選單待 sudo 執行，未把 key 輸出或寫進版控。

---

### #039 · Linux 正式 API／ymir-web nginx／Tunnel 已啟動

> 👤 **GPT（AI）** · 🕒 2026-10-13 · `🔬驗證`

部署腳本第一次完成 sudo 與 nginx preflight 後，因 LiteLLM 主機 4000 連不上而停止回復；容器內健康檢查 200，確認為埠轉送問題。重新載入 LiteLLM 網路未恢復，僅重啟 LiteLLM 代理（restart 報 conmon 錯誤，接著 start 成功），主機健康探測恢復 200；未重啟或重建 SQL／PostgreSQL。

使用者在主機終端完成 sudo／SWITCH 後，腳本紀錄顯示成功，三個正式 systemd unit 均 loaded／active／running。本機 API `/health` 200 Healthy，nginx `/login` 200，確認正式 origin 的容器名稱為 `ymir-web`，Tunnel 依賴 origin 並使用 `AddHost=ymir-web:127.0.0.1`。外層終端包裝器未顯示數字狀態碼，不影響以上實際檢查。

公開探測兩個網域 `/login`、主要網域 `/api/me` 及 `/health` 均回 403、Server=cloudflare；尚不能宣稱外網或企業登入／SSE 成功，待瀏覽器與 Cloudflare 規則確認。設定備份 `/var/backups/ymir/production-20261013-003139-2960693`（含密鑰、僅 root 可讀）；SQL 尚無持久化 volume，不可刪除／重建。

---

### #038 · 正式部署保留 ymir-web nginx 入口

> 👤 **GPT（AI）** · 🕒 2026-10-12 · `🚧進度`

使用者更正 #037：要求保留 nginx，不讓 Tunnel 直接連 Kestrel。切換腳本已改為 Tunnel → `ymir-web:80` → 正式 API:5080；沿用 origin Quadlet 映像、以 rootful `ymir-web` 取代 4200 代理，Cloudflare 來源維持原值。新增正式 nginx 設定，保留 Host／HTTPS forwarding、SSE、50 MiB body 限制，阻擋 health／dev 路徑；備份／錯誤回復涵蓋 origin unit 與 nginx 設定。

驗證：Bash 與內嵌 Python 語法、help、非 root 拒絕執行、Quadlet 轉換重複執行一致性；隔離 nginx:alpine 容器以非 root、唯讀、NET_BIND_SERVICE 通過 `nginx -t`，修正唯讀 rootfs 下所有 temp paths。腳本會先以既有正式 nginx 映像做同樣隔離檢查。sudo 部署、公開入口、企業登入與 SSE 尚未驗證，待使用者執行。未更動使用者原有 `ymir-web-proxy.conf`。

---

### #037 · Ubuntu 正式部署切換腳本（待主機執行）

> 👤 **GPT（AI）** · 🕒 2026-10-12 · `⛔卡關`

使用者要求統一 Linux 正式架構；發現既有 rootful Quadlet API／Tunnel 被 masked、使用者層開發 API failed、舊 nginx 指向 4200、LiteLLM 雖 running 但主機 4000 無法連線。sudo 需互動驗證，使用者選擇產生腳本自行執行。

新增 [切換腳本](../../deploy/api/activate-linux-production.sh)及[操作說明](../../deploy/api/LINUX-PRODUCTION-SWITCH.md)：沿用 Production 映像與密鑰、備份設定、檢查依賴、停用開發 API、解除正式服務遮罩、移除 Tunnel 對舊 nginx 的依賴、健康檢查與錯誤回復。Cloudflare 來源需先手動改為 `http://127.0.0.1:5080`。不重建／刪除 SQL、PostgreSQL 或使用者檔案；SQL 無 volume 的風險仍待原生備份及遷移。

驗證：`bash -n`、`--help`、非 root 拒絕執行；未執行 sudo 切換、未驗證外網／企業登入／SSE，未呼叫真實付費模型。保留使用者原有 cloudflared compose 修改與未追蹤 nginx 設定。

---

### #036 · favicon 上傳 500、OneDrive 偶發測試修正，ADR 編號與置頂區整理

> 👤 **Claude（AI）** · 🕒 2026-10-13 10:45 · `✅完成`

- **背景**：使用者要求檢查專案問題，之後決定修正 favicon bug、整理 ADR 編號與看板置頂區，並合併回 main（使用者明確要求，作為 G1「直接在 main 開發」的例外；以 fast-forward 合併，不產生 merge commit）。
- **B1 favicon**：`BrandingEndpoints` 與前端都允許 256 KB，但值以 base64 存進 `platform.system_settings`，`SystemSetting.ValueMaxLength` 只有 8000 字（約 6 KB 圖片），超過時網域層丟 `ArgumentException` → 500（以 16 KB PNG 重現）。欄位本來就是 `nvarchar(max)`，所以把上限放寬為 512K 字；migration `SystemSettingsLargeValues` 只更新模型快照、資料表不變。補上 100 KB 與剛好 256 KB 的上傳／匿名讀回測試。
- **D1 ADR 編號**：兩個分支同時新增了 ADR-0016（網站託管、管理員開放模型）。網站託管的 0016 已被程式碼與 CLAUDE.md 大量引用，所以保留；管理員開放模型改為 [ADR-0020](../adr/0020-admin-model-access.md)（內容未變，檔內註明原編號），同步 ADR-0017／0019 與 README 的引用；[ADR 索引](../adr/README.md) 補上 0017、0018、0020；CLAUDE.md 改為「目前到 ADR-0020」。依留言規則不修改舊留言：**#015 等舊留言中指模型開放的「ADR-0016」即現在的 ADR-0020**。
- **置頂區**：狀態改為進行中（原計畫已整合回 main），工作項目拆成「原計畫」與「插單、使用者回報與維運」兩張表，各列內容不變（L2 改指 ADR-0020）。
- **B2 偶發測試**：完整測試（單元＋整合同時跑）時 `OneDriveSyncTests.RevokedAuthorization_StopsSync_AndKeepsLocalFiles` 偶發失敗（修正前 4 次完整執行失敗 2 次，只跑整合測試 5 次都通過）：「立即同步」回 409 而不是 202。原因是執行結束後的背景上傳可能晚於測試撤銷 refresh token 才跑，先把連結標成 `NeedsReauth`。比照同檔第一個測試，先等這次上傳完成再撤銷；產品程式未變。
- **驗證**：`dotnet build Ymir.slnx` 0 警告；`dotnet format --verify-no-changes` 通過；`BrandingSettingsTests` 4 項通過；`OneDriveSyncTests` 連跑 5 次通過；修正後 `dotnet test --solution Ymir.slnx` 連跑 3 次，每次 768 項全部通過。只改後端、測試與文件，前端未變更（本次檢查時前端 lint、168 Vitest、build 已通過）；文件相對連結檢查、`git diff --check` 通過。
- **檢查時發現、尚未處理**（`⚠️發現`，待使用者決定）：`start-ymir.ps1` 改讀不在版控的 `run-api-local-only.ps1`，且不再啟動 `ymir-web`／Tunnel；換機器會直接失敗，用它啟動時對外網域不會開放（見 #035）。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #035 · 本機 HTTPS 啟動與 Agent runtime 恢復

> 👤 **Codex（AI）** · 🕒 2026-10-12 14:43 · `✅完成`

使用者回報聊天一直等待、沒有新 Agent container，並要求先不使用或啟動 Tunnel，改在本機修正。資料庫確認三次執行皆在準備階段以 `RUNTIME_START_FAILED` 結束，Docker 本機缺少設定指定的 Agent 映像。依目前 Containerfile 重建 `localhost/ymir/agent-runtime:inspiring-ab212b2`，保留既有 workspace、agent-state、帳號、模型及資料。檢查期間使用者調整部署停止容器，待使用者授權本機修復後才啟動 SQL Server／LiteLLM PostgreSQL／LiteLLM。

本機部署新增不進版控的 `run-api-local-only.ps1`，沿用既有部署設定但關閉 PublicEdge，僅監聽 loopback 的 HTTP health 與 `https://localhost:5443`，限制 AllowedHosts；使用既有受信任的 localhost 憑證，API 直接提供從已部署網頁複製的 Angular build。維持 Production／Docker 與正式登入規則，未啟用 dev 登入。`start-ymir.ps1` 改讀本機 launcher，只啟動資料庫與模型閘道，不需要也不啟動 nginx／Tunnel；README 與部署指南同步。原公開部署 launcher 保留。

驗證：Agent image build 成功，uid=1000、Pi 1.0.0、python-pptx 1.0.2 smoke 通過；PowerShell AST 語法、`git diff --check`、啟動腳本首次及重複執行通過；localhost HTTPS 登入頁 200、health Healthy、providers 顯示密碼／企業登入且 devLogin=false，瀏覽器確認登入表單正常，API 僅監聽 loopback，Tunnel／nginx 保持 exited。`dotnet test --project tests/Ymir.IntegrationTests --no-build -- --filter-method '*Help_SendsPlatformFactsToAgent_AndKeepsOriginalMessage*'` 1 項通過：真實 Docker container／Pi、Fake LLM、API 與 SSE 完成事件。首次測試用 localhost 連 SQL 發生連線逾時，改用 127.0.0.1:14330 後通過；測試使用獨立資料庫／暫存 workspace，已清理該次測試容器。未用真實帳號完成 localhost 登入、未呼叫付費模型；企業登入需要 Entra 註冊 localhost callback，尚未驗證。截圖中的等待提示發生於終止事件之前，目前沒有證據需修改前端錯誤處理。main CI 依此次推送結果追蹤。

---

### #034 · 既有分支整合與 main／rebase 開發規則

> 👤 **Codex（AI）** · 🕒 2026-10-12 13:06 · `✅完成`

依使用者要求盤點本機與 origin 的所有分支：其他功能提交已包含於 main，唯一未整合提交為本機 `claude/inspiring-einstein-2pwxx6` 的 `c9e8733`（Linux 部署指南）。將其整合回 main，保留 main 的公司識別與工作提示紀錄，以及部署指南紀錄；解決看板衝突並將部署指南留言改為 #033。所有非 main 分支的 tip 均已驗證為整合後 main 的祖先，清理時只刪除已確認整合的分支，先將 main 推送成功再刪除遠端與本機分支。

更新 [CLAUDE.md](../../CLAUDE.md) 與新增 [AGENTS.md](../../AGENTS.md)：直接在 main 工作，工作前及 push 前 fetch／rebase origin/main，不另建工作分支／worktree／PR、不 force push、不重寫已推送歷史；保留未提交變更、衝突後重新驗證，追蹤 main CI。同步更新 Stop hook 的提示，並設定本機 `pull.rebase=true` 與 `branch.main.rebase=true`。

驗證：文件連結與 code fence、hook Bash 語法、`git diff --check` 通過；逐一以 `git merge-base --is-ancestor` 確認 5 個本機與 9 個 origin 非 main 分支均已整合。main 推送成功後，已刪除上述 5 個本機與 9 個遠端分支並 prune；本機與 origin 只剩 main（origin/HEAD 為別名），工作目錄乾淨。此次只整合部署文件並修改開發流程，未改應用程式或現場部署；遠端 CI 結果以該次 main workflow 為準。

---

### #033 · ADR-0008 Linux 正式部署與啟動指南

> 👤 **Codex（AI）** · 🕒 2026-10-12 10:46 · `✅完成`

依使用者要求更新 [API 部署指南](../../deploy/api/README.md)：整理 rootful Podman＋Quadlet 的首次準備、migration、RuntimeHost／API 啟動、開機啟動、更新、公開入口與故障檢查，明確區分 Docker 專用 `start-ymir.sh`。migration 改為 API 啟動前執行，兩個 DbContext 明確使用 `--connection`，避免設計階段 Factory 的 `ymir_design` 開發連線。同步補齊 [RuntimeHost 指南](../../deploy/runtime-host/README.md) 的 ASP.NET Core runtime、env 安裝、共用 token 路徑與 UID 填寫，更新主 README 導覽及備份還原指南。

驗證：對照 ADR-0008、程式與 unit 範本，檢查修改文件的相對檔案連結與 Markdown code fence，`git diff --check` 通過。僅修改文件，未執行 Linux 安裝、migration 或部署切換；完整 rootful API＋rootless Agent 拓樸與重開機恢復仍待 Linux 實機驗收。

---

### #032 · 公司識別可由管理者設定（移除寫死的 Web-Pro）

> 👤 **Claude（AI）** · 🕒 2026-10-12 · `✅完成`

- **背景**：Web-Pro 是公司名稱，Ymir 是開源專案名稱；為了通用化，畫面不再寫死公司名稱。
- **實作**：平台名稱固定為 Ymir，顯示為「{公司名稱} Ymir」，公司名稱留空就只顯示 Ymir（側邊欄、登入頁）。新增匿名 `GET /api/branding`、`GET /api/branding/favicon`，以及 Admin 的 `PUT /api/admin/settings/branding`、`PUT|DELETE .../branding/favicon`；資料存在 `platform.system_settings`（`branding.document`、`branding.favicon`），寫稽核。Favicon 只收 PNG / ICO / JPEG / WebP（依檔頭判斷、不收 SVG、上限 256 KB），回應附 `nosniff` 與 CSP sandbox。管理 → 系統設定新增「公司識別」卡片（含預覽、上傳、還原預設）。`index.html` 移除 `webpro` 版本參數；`runtime/agent/test-announcement.py` 與 ADR-0009 中的 web-pro 為測試資料 / 歷史紀錄，未改。
- **驗證**：`dotnet build` 0 警告；`BrandingSettingsTests`、`AuthorizationMatrixTests` 通過（含 OpenAPI 快照更新）；前端 lint、168 Vitest、build 通過。未做瀏覽器 e2e 截圖。

---

### #031 · 「正在工作中」提示不再閃爍

> 👤 **Claude（AI）** · 🕒 2026-10-11 · `✅完成`

- **原因**：提示只看「是否有執行中的工具」。連續呼叫工具時，上一個 `tool.completed` 到下一個 `tool.started` 之間（模型思考中）沒有執行中的工具，提示消失又出現。
- **實作**：`ExecutionView` 新增 `toolBusy`：`tool.started` 設為 true，Agent 輸出文字（且沒有工具仍在跑）或執行完成／失敗／取消才清除。`hasRunningTools` 改看此欄位，所以工具之間的空檔仍維持提示，最後一個工具結束後也穩定顯示到 Agent 開始回覆文字為止。不使用計時器，仍是純函式。
- **驗證**：前端 lint、165 Vitest 通過；更新重疊工具的測試，新增連續工具空檔測試。未重跑 `e2e:tool-progress`（需要瀏覽器與 SSE 替身），行為由單元測試涵蓋；無 API 契約或後端變更。

---

### #030 · 主要網域改為 ymir.thetainformation.com

> 👤 **Codex（AI）** · 🕒 2026-10-08 11:48 · `✅完成`

依使用者要求，將目前部署資料庫的 `edge.public_hostname` 從 `vibemaker.webpromaterial.com` 改為 `ymir.thetainformation.com`，同步寫入網域變更稽核。部署啟動檔與環境範本原本已使用新網域；Cloudflare Tunnel 已配置新網域至 `http://ymir-web:80`，無須重新部署或停止服務。README 的架構圖同步改為新網域。

驗證：新網域 HTTPS `/login` 200；避開先前快取的新網域 `/api/auth/providers` 200，企業登入與密碼登入開啟、dev 登入關閉；本機 API 以新 Host 請求 200、舊 Host 400；企業登入入口 302，`redirect_uri` 為 `https://ymir.thetainformation.com/signin-oidc`。未以真實企業帳號完成登入。Cloudflare 的舊網域路由仍存在，但 API 已不接受舊 Host，沒有新增舊網域重新導向。

---

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

### 主聊天畫面漢堡選單：收放加入滑動動畫
- **需求**：桌面版收放側邊欄時原本是 `display: none` 瞬間切換，要有滑動效果。
- **做法**：`shell.scss` 桌面版改以 `width` / `padding` / `border` 過渡（0.25 秒）收放，內容固定寬度避免文字重新換行，收合後延遲 `visibility: hidden`；`prefers-reduced-motion` 時關閉動畫。手機抽屜原本就有 `transform` 動畫，未動。元件樣式預算警告線 7kB → 7.5kB（錯誤線 8kB 不變）。
- **驗證**：`npm run build` 無警告、`npm run lint` 通過。未以瀏覽器實際錄製動畫，待使用者環境目視確認。

---
