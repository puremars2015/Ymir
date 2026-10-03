# Sprint 1 · Walking Skeleton

[← 回看板列表](README.md) ・ 規則與格式見 [README](README.md#留言規則)

---

## 📌 置頂：狀態總覽

> 最後更新：2026-10-03 23:04 ・ 狀態：**🚧 進行中**

**目標**：打通最細的端到端鏈路 —— Dev 登入 → 建立 Workspace / Conversation → 送訊息 → Container 內的 Pi → SSE 顯示在 Angular。
之後的 Sprint 只在這條鏈路上「加厚」，不再改結構（[開發規劃](../planning/development-plan.md#修訂後的路線圖)）。

**Done Definition**：在瀏覽器以 Dev 帳號登入，建立 Workspace 與 Conversation，送出 `[create-file]` 訊息，
看到串流回應、檔案出現在該 Workspace，重新整理後歷史訊息仍在；取消與逾時會把 execution 正確結束。

| 工作項目 | 狀態 | 備註 |
|---|---|---|
| EF Core + SQL Server（`platform` / `vibemaker` schema）、migrations、Aspire 加入 SQL Server | ✅ | 資料模型補強見開發規劃 §9；見 [#002](#002--s1-資料層完成) |
| Dev Authentication Handler（只在 Development）+ `/api/me` | ✅ | 正式 OIDC 在 Sprint 2（ADR-0002）；見 [#003](#003--s2-dev-登入cookie--antiforgery完成) |
| Workspace / Conversation / Message API（server 端擁有者檢查） | ✅ | SA §9；`POST runtime/stop` 移到 Sprint 4；見 [#004](#004--s3-workspace--conversation-api-與授權矩陣完成) |
| Execution pipeline：背景 worker、狀態機（QUEUED→RUNNING→終止）、cancel、timeout | ✅ | SA §14；含啟動 reconciliation；見 [#005](#005--s4s5-execution-pipeline-與-sse-完成) |
| `POST /messages` 冪等（`client_request_id`）+ 同 Conversation 只能有一個執行中 | ✅ | filtered unique index |
| SSE 端點 `/api/executions/{id}/events`（事件持久化、`Last-Event-ID` 續傳） | ✅ | 開發規劃 §5；心跳尚未實作 |
| Angular：Workspace / Conversation 列表、Chat 頁改用正式 API | ⏳ | OpenAPI 產生 TypeScript client |
| 授權矩陣測試（非擁有者 → 403/404） | ✅ | 驗收條件 #2；涵蓋全部 `/api` 端點 |
| 正式主機用完整 Containerfile 重跑 Podman 驗證 | ⏳ | 從 Sprint 0 移入，見 [Sprint 0 #003](board-sprint-0.md#003--驗證中的發現與環境限制) |

**開工前要先有的決定**：無（Sprint 0 [#005](board-sprint-0.md#005--需要決定的四件事) 的四件事影響 Sprint 2～4，不擋 Sprint 1）。

---

## 💬 留言區

### #005 · S4+S5 Execution pipeline 與 SSE 完成

> 👤 **Claude（AI）** · 🕒 2026-10-03 23:04 · `✅完成`

後端主鏈路打通：**送出訊息 → 背景執行 Agent → SSE 即時串流 → 保存歷史**。

- `POST /api/conversations/{id}/messages`：同一個 transaction 保存 USER 訊息與 QUEUED execution，回 202（含 `eventStreamUrl`）。
  - 相同 `clientRequestId` 重送 → 回傳同一個 execution
  - 同對話已有執行中 → 409 `EXECUTION_CONFLICT`（資料庫保證）
  - 停用的使用者 → 403
- **背景 worker** 執行（與 HTTP request 解耦）；同一 workspace 依序執行（SA §14）。流程：準備 Runtime → AgentSession（Pi `--session-id`）→ Agent → 保存。
- **每個事件先寫 `execution_events` 再推送**；不論成功、失敗、取消、逾時，最後一定寫入終止事件並保存 ASSISTANT（或 ERROR）訊息，不會卡在 RUNNING。
- 取消（QUEUED 直接取消、RUNNING 中止 Agent）、逾時 → `AGENT_TIMEOUT`、Runtime 啟動失敗 → `RUNTIME_START_FAILED`。
- **啟動時 reconciliation**：上次中斷的 RUNNING → FAILED；QUEUED → 重新排入佇列。
- `GET /api/executions/{id}/events`：先訂閱即時事件、再從資料庫補 `Last-Event-ID` 之後的事件，以序號去重，**斷線重連不漏不重**。
- `POST /api/executions/{id}/cancel`。
- 移除 Sprint 0 的 `/api/dev/agent-stream`。

驗證：
- 全部 84 個測試 ✅；整合測試連跑 3 次皆穩定
- 新增：完整流程（事件順序、序號、歷史訊息關聯 execution）、冪等、409、取消、`Last-Event-ID` 續傳、停用使用者 403、空白內容 400、逾時 `AGENT_TIMEOUT`、reconciliation
- **真實 Pi 經過正式 API**：`[create-file]` 在 workspace 建出檔案，第二則訊息續接上下文（模型收到 2 則使用者訊息）
- 授權矩陣涵蓋全部 `/api` 端點（新端點一加就被測試擋下要求分類，這次也確實擋到）

📝 尚未做：SSE 心跳（長時間沒有事件時代理可能斷線；有續傳可補救，排到 Sprint 3）。
⚠️ 目前 Angular 的開發用聊天頁還在呼叫已移除的端點，下一步 S6 換成正式流程。
🔧 另外修正了 S3 commit 的 import 排序（`dotnet format` 檢查沒過），之後每次 commit 前都會先確認格式。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #004 · S3 Workspace / Conversation API 與授權矩陣完成

> 👤 **Claude（AI）** · 🕒 2026-10-03 22:54 · `✅完成`

- SA §9 端點：`GET/POST /api/workspaces`、`GET /api/workspaces/{id}`、`GET /api/workspaces/{id}/runtime`、`GET/POST /api/conversations`（可依 workspaceId 篩選）、`GET /api/conversations/{id}`、`GET /api/conversations/{id}/messages`。
- 所有查詢都限定目前使用者；**別人的資源一律回 404**（不讓人知道它存在），錯誤帶 SA §13 錯誤碼（例如 `CONVERSATION_NOT_FOUND`）。
- 領域驗證錯誤（例如空白名稱）回 400 `VALIDATION_FAILED`。
- **授權矩陣測試**：列舉所有 `/api` 端點，沒有分類的端點會讓測試失敗；對每個資源端點驗證「入侵者 404、擁有者成功、匿名 401」。

驗證：整合測試 30 個 ✅（新增 5 個功能測試 + 3 個授權矩陣測試）；`dotnet format` ✅。

📝 範圍調整：`POST /api/workspaces/{id}/runtime/stop` 需要 runtime id 與資料庫一致的生命週期管理，移到 Sprint 4（Runtime 完整生命週期）一起做。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #003 · S2 Dev 登入（Cookie + antiforgery）完成

> 👤 **Claude（AI）** · 🕒 2026-10-03 22:51 · `✅完成`

- 依 ADR-0002 採 HttpOnly、SameSite=Lax 的登入 cookie；API 未登入回 401、無權限回 403（ProblemDetails，帶 `code`），不做 redirect。
- **所有端點預設都要登入**（fallback policy），只有 health、OpenAPI、Dev 登入明確標成匿名。
- `POST /api/dev/login`（只在 Development 註冊）、`POST /api/auth/logout`、`GET /api/me`；登入與登出寫入 audit log。
- Antiforgery：GET `/api/*` 會發 `XSRF-TOKEN` cookie，所有 POST 必須帶 `X-XSRF-TOKEN` header（Angular 會自動處理）。
- API JSON 的 enum 改成字串（例如 `"role":"User"`）。

驗證：全部 68 個測試 ✅（新增 7 個認證測試：未登入 401 帶 `AUTH_REQUIRED`、重複登入同一使用者、cookie 是 HttpOnly、缺 XSRF header 被擋、登出後 401、空帳號 400、已登入打不存在的路由回 404）；`dotnet format` ✅。

📝 行為變更：未登入時打不存在的路由回 **401** 而不是 404（不讓匿名者探測有哪些路由），測試已更新。
📝 暫時保留：Sprint 0 的 `/api/dev/agent-stream` 暫時標為匿名，S5 換成正式 SSE 端點時移除。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #002 · S1 資料層完成

> 👤 **Claude（AI）** · 🕒 2026-10-03 22:48 · `✅完成`

Sprint 1 開工，第一個切片（資料層）完成：

- EF Core 10 + SQL Server：`PlatformDbContext`（schema `platform`：users、audit_log）與 `VibeMakerDbContext`（schema `vibemaker`：workspaces、conversations、messages、agent_sessions、agent_runtimes、agent_executions、execution_events），兩組各自的 migrations。
- 依開發規劃 §9 補強：execution 加 `client_request_id`、`user_id`、`runtime_id`；message 加 `execution_id`；新增 `execution_events`（SSE 續傳用）。
- **並行規則由資料庫保證**：同 Conversation 只能有一個 QUEUED/RUNNING、`(user_id, client_request_id)` 唯一、每個 workspace 只有一個未刪除 runtime（filtered unique index）。
- `AgentExecution` 狀態轉移有防呆，非法轉移直接拋例外。
- Audit log 與使用者 upsert（issuer + subject）改寫資料庫。
- Development 啟動時自動 migrate；Aspire AppHost、CI（service container）、雲端 session hook 都會提供 SQL Server。

驗證：
- `dotnet build`、`dotnet format --verify-no-changes` ✅
- 單元測試 46 個 ✅（新增狀態機、命名轉換）
- 整合測試 15 個 ✅（對真實 SQL Server 2022：filtered index 擋住第二個執行中 execution、冪等鍵、enum 存成 `QUEUED`、使用者 upsert）；測試資料庫結束後確認已刪除

⚠️ 發現：沙箱的 rootful podman 沒有 bridge 網路（port mapping 不通），所以 **Testcontainers 不能用**。改成測試讀 `YMIR_TEST_SQLSERVER` 連線字串、每個 fixture 自建自刪資料庫；沙箱用 `--network host` 跑 SQL Server，CI 用 GitHub Actions service container。

下一步：S2 Dev 登入（Cookie + antiforgery）。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #001 · Sprint 1 看板開張

> 👤 **Claude（AI）** · 🕒 2026-10-03 00:04 · `📢公告`

Sprint 0 已完成（[Sprint 0 看板](board-sprint-0.md)），這裡開始記錄 Sprint 1。
工作項目列在置頂區；開工的 session 請先把要做的項目標成 🚧，收工前依 [留言規則](README.md#留言規則) 回報。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---
