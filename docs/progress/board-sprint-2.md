# Sprint 2 · 正式認證

[← 回看板列表](README.md) ・ 規則與格式見 [README](README.md#留言規則)

---

## 📌 置頂：狀態總覽

> 最後更新：2026-10-04 10:14 ・ 狀態：**⏳ 尚未開始（等待決定）**

**目標**：以企業帳號登入（OIDC / Entra ID，經由 BFF，ADR-0002），完成 Admin / User 權限與帳號停用流程。

**Done Definition**：使用者以企業帳號登入、瀏覽器不持有任何 token；Admin 可以停用帳號，被停用的帳號立即無法使用 API 與建立 execution（SA 驗收條件 #1）。

| 工作項目 | 狀態 | 備註 |
|---|---|---|
| OIDC 登入（Authorization Code + PKCE，後端換 token、只發 cookie） | ⏳ | **需先確認 IdP 類型** |
| `IIdentityProvider`：IdP claims → `ExternalIdentity`（issuer + subject / oid） | ⏳ | 傳統 AD / LDAP 改用 adapter |
| 每個請求驗證使用者狀態（停用後既有 cookie 立即失效） | ⏳ | Sprint 1 只在建立 execution 時檢查 |
| Admin API：使用者列表、停用 / 啟用、角色設定（寫 audit） | ⏳ | SA §4 |
| Angular：企業帳號登入按鈕、Admin 使用者管理頁 | ⏳ | |
| 授權矩陣加入角色維度（User 不能呼叫 Admin API） | ⏳ | |
| 正式主機用完整 Containerfile 重跑 Podman 驗證 | ⏳ | 從 Sprint 1 移入；需要實際主機（見 [runtime/agent/README](../../runtime/agent/README.md#手動驗證)） |

**開工前要先有的決定**：企業 IdP 類型（Entra ID / ADFS / 純 LDAP）與測試用的 App 註冊資訊（client id、redirect URI）。

---

## 💬 留言區

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
