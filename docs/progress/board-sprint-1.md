# Sprint 1 · Walking Skeleton

[← 回看板列表](README.md) ・ 規則與格式見 [README](README.md#留言規則)

---

## 📌 置頂：狀態總覽

> 最後更新：2026-10-03 22:48 ・ 狀態：**🚧 進行中**

**目標**：打通最細的端到端鏈路 —— Dev 登入 → 建立 Workspace / Conversation → 送訊息 → Container 內的 Pi → SSE 顯示在 Angular。
之後的 Sprint 只在這條鏈路上「加厚」，不再改結構（[開發規劃](../planning/development-plan.md#修訂後的路線圖)）。

**Done Definition**：在瀏覽器以 Dev 帳號登入，建立 Workspace 與 Conversation，送出 `[create-file]` 訊息，
看到串流回應、檔案出現在該 Workspace，重新整理後歷史訊息仍在；取消與逾時會把 execution 正確結束。

| 工作項目 | 狀態 | 備註 |
|---|---|---|
| EF Core + SQL Server（`platform` / `vibemaker` schema）、migrations、Aspire 加入 SQL Server | ✅ | 資料模型補強見開發規劃 §9；見 [#002](#002--s1-資料層完成) |
| Dev Authentication Handler（只在 Development）+ `/api/me` | ⏳ | 正式 OIDC 在 Sprint 2（ADR-0002） |
| Workspace / Conversation / Message API（server 端擁有者檢查） | ⏳ | SA §9 |
| Execution pipeline：背景 worker、狀態機（QUEUED→RUNNING→終止）、cancel、timeout | ⏳ | SA §14；不要放到 Sprint 5 |
| `POST /messages` 冪等（`client_request_id`）+ 同 Conversation 只能有一個執行中 | ⏳ | filtered unique index |
| SSE 端點 `/api/executions/{id}/events`（事件持久化、`Last-Event-ID` 續傳） | ⏳ | 開發規劃 §5 |
| Angular：Workspace / Conversation 列表、Chat 頁改用正式 API | ⏳ | OpenAPI 產生 TypeScript client |
| 授權矩陣測試（非擁有者 → 403/404） | ⏳ | 驗收條件 #2 |
| 正式主機用完整 Containerfile 重跑 Podman 驗證 | ⏳ | 從 Sprint 0 移入，見 [Sprint 0 #003](board-sprint-0.md#003--驗證中的發現與環境限制) |

**開工前要先有的決定**：無（Sprint 0 [#005](board-sprint-0.md#005--需要決定的四件事) 的四件事影響 Sprint 2～4，不擋 Sprint 1）。

---

## 💬 留言區

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
