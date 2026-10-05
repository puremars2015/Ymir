# ADR-0007：一個使用者一個 Runtime，專案是 Runtime 內的檔案群組

- 狀態：已採納（取代 SA §6.1「一個 Active Workspace 對應一個 Agent Container」，以及 ADR-0003 第 1、4 點中「每個 Workspace」的部分）
- 日期：2026-10-05

## 背景

SA §6.1 採「一個 Workspace 一個 container」：Workspace 同時是檔案群組與執行環境，使用者每建一個 Workspace 就多一個 container。
使用者決定：

- **一個使用者只有一個 container**，不會出現一個使用者多個 container。
- 這個 container 可以載入不同的**檔案群組**，也就是像 ChatGPT 一樣的「專案」。
- 沒有分組的對話可以不分組。

## 決策

1. **Runtime 屬於使用者**：
   - Container 名稱為 `ymir-user-{userId}`，label 為 `ymir.user-id`。
   - Host 目錄只由 user id 推導：`{WorkspaceRoot}/users/{userId}/workspace` 與 `agent-state`（`UserDirectories`）。
   - `agent_runtimes` 以 `user_id` 建 filtered unique index，每位使用者最多一筆未刪除的紀錄。
   - `IAgentRuntimeManager.EnsureRuntimeAsync(userId)`。
2. **Workspace 改名為 Project（專案）**，只代表 container 內的一個目錄 `/workspace/projects/{projectId}`。同一專案的對話共用這些檔案。
3. **對話可以不屬於專案**（`conversations.project_id` 可為 null）。未分組的對話在自己的目錄 `/workspace/chats/{conversationId}` 工作，聊天之間不共用檔案。
4. **工作目錄的傳遞與檢查**：
   - 每次執行由 `RuntimePaths.WorkingDirectoryFor(conversationId, projectId)` 決定工作目錄，經 `AgentRunRequest` → `RuntimeProcessSpec.WorkingDirectory` 傳給 runtime，以 `exec --workdir` 進入。
   - Runtime 只接受 `/workspace` 或由 Guid 產生的 `projects/{32 hex}`、`chats/{32 hex}`，其他路徑一律拒絕（SA §12）。
5. **目錄在 container 內建立**（`exec <container> mkdir -p <dir>`），擁有者自然是 agent 使用者。Podman 與 Docker 都不需要再 chown；Local runtime 直接建立 host 目錄。
6. **同一使用者的 execution 依序執行**（`UserExecutionLocks`），因為共用同一個 container 的 CPU / 記憶體 / pids 限制。同對話單一執行中的 filtered unique index 不變。
7. **Pi session** 放在使用者的 `/agent-state/sessions`，以 session id 區分對話，續接方式不變（ADR-0003）。
8. **API**：
   - `/api/workspaces*` 改為 `/api/projects`（列表 / 建立 / 取得）。
   - Runtime 狀態改為 `GET /api/runtime`，只查自己的，不接受任何 id。
   - `/api/conversations` 的 `projectId` 可省略：列表不帶就回傳全部，建立不帶就是未分組。
9. **Migration `OneRuntimePerUser`**：
   - `workspaces` 改名為 `projects`，原本的對話留在對應專案中，資料不遺失。
   - 舊的 per-workspace runtime 紀錄清空，下次執行時以使用者重建。
   - 舊的 `ymir-ws-*` container 需手動移除。

## 影響

- 容器數量從「Workspace 數」降為「使用者數」，資源較好估算。
- 同一使用者的專案之間沒有 container 層級的隔離，但都屬於同一個使用者，安全邊界仍是「使用者之間互相隔離」，這點不變。
- 同一使用者同時只跑一個 execution；之後若要並行，需要評估 container 資源限制與同目錄的檔案鎖定。
- 前端改為 ChatGPT 式版面：側邊欄分「專案」與「聊天」，可以直接在輸入框開始新對話。
- 舊資料：既有的 workspace 變成專案；host 上舊的 `{WorkspaceRoot}/{workspaceId}` 目錄不會自動搬移（目前只有開發資料）。
