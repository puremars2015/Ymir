# Vibe Maker Core MVP 系統分析文件（SA）

> 本文件由 `documents/Vibe_Maker_Core_MVP_SA_v1.0.docx` 轉為 Markdown，內容未修改，方便版本控管與 AI 開發引用。
> 原始 docx 仍為正式版本；對 SA 的修訂建議請見 [`docs/planning/development-plan.md`](../planning/development-plan.md) 與 [`docs/adr/`](../adr/)。

帳號登入・對話介面・Agent Harness・Podman Runtime

| 文件項目 | 內容 |
|---|---|
| 文件版本 | v1.0 |
| 文件日期 | 2026-10-02 |
| 系統名稱 | Vibe Maker |
| 階段 | Phase 1 / Core MVP |
| 主要技術 | Angular / ASP.NET Core .NET 10 / SQL Server / Rootless Podman / Pi Agent / LiteLLM |
| 文件目的 | 提供系統分析、介面契約、資料模型與驗收基準，供工程團隊進行系統設計與開發。 |

## 1. 文件目的與範圍

本文件定義 Vibe Maker 第一階段 Core MVP。第一階段目標不是完成所有 AI 平台能力，而是先建立可持續擴充的核心鏈路：企業使用者登入後，可建立對話，後端依使用者/Workspace 啟動隔離的 Agent Runtime，調用 Pi Agent Harness 與 LiteLLM，並將 Agent 執行過程即時回傳至前端。

### 1.1 MVP In Scope

- 企業帳號登入與 Vibe Maker 使用者資料建立/同步。
- 基本角色權限：Admin、User。
- Conversation 建立、列表、讀取與訊息保存。
- Chat UI 與 Agent 回覆串流顯示。
- Agent Service：將 Conversation 與底層 Agent Session 解耦。
- Runtime Manager：建立、啟動、停止、恢復 Rootless Podman Container。
- Workspace 持久化；Container 可被停止/重建而不遺失 Workspace。
- Pi Agent Harness 調用與 LiteLLM 模型入口。
- Runtime、Agent、API 基本 Audit Log 與錯誤追蹤。

### 1.2 MVP Out of Scope

- RAG 建立與管理。
- MES MCP、EIP MCP、OneDrive MCP、SQLite MCP 的正式功能實作。
- Vibe Maker 小工具動態上架與公開 URL 管理。
- 多 Agent Team / Agent Marketplace。
- 計費、部門成本分攤、複雜 Token Quota。
- Kubernetes；MVP 先採 Podman，保留 Runtime Provider 抽象層。

## 2. 核心使用流程

```text
[User]
  │ 1. AD/SSO Login
  ▼
[Vibe Maker Web / Angular]
  │ 2. Access Token
  ▼
[ASP.NET Core .NET 10 API]
  │
  ├─ User / Role
  ├─ Conversation
  ├─ Message
  └─ Agent Service
         │
         ▼
   [Runtime Manager]
         │
         ▼
 [Rootless Podman]
         │
         ▼
 [Workspace Agent Container]
         │
         ├─ Pi Agent Harness
         │       │
         │       ▼
         │    LiteLLM ──> Cloud / Local LLM
         │
         └─ /workspace (persistent volume)

Agent Events ──SSE──> Angular Chat UI
```

核心原則：共用 Vibe Maker Control Plane、Agent Harness Image、LiteLLM 與未來 MCP Infrastructure；不共用使用者的 Agent Runtime 與 Workspace。

## 3. 系統元件與責任

| 元件 | 建議技術 | 主要責任 |
|---|---|---|
| Vibe Maker Web | Angular | 登入導向、Conversation UI、Chat UI、SSE 事件呈現、停止 Agent 操作。 |
| Vibe Maker API | ASP.NET Core .NET 10 | Authentication、Authorization、User、Conversation、Message、Agent API。 |
| Agent Service | .NET 10 Application Service | Conversation → Agent Session；送出 Prompt；處理 Agent execution lifecycle。 |
| Runtime Manager | .NET 10 Service | 封裝 Podman runtime 操作，不讓其他模組直接依賴 Podman CLI/API。 |
| Podman | Rootless Podman | 提供隔離的 Agent 執行環境、資源限制與 Workspace mount。 |
| Pi Agent Harness | Agent Container | LLM loop、tool execution、filesystem/shell 等 Agent 能力。 |
| LiteLLM | Shared Service | 統一模型 API、模型 routing，後續擴充 quota / usage。 |
| SQL Server | Database | User、Workspace、Conversation、Message、Session、Runtime metadata、Audit。 |
| Workspace Storage | Linux filesystem/NAS | 保存 Agent 產生的專案、檔案與必要持久資料。 |

## 4. 身份驗證與帳號管理

### 4.1 登入流程

```text
User -> Vibe Maker Web -> Enterprise Identity Provider
     <- Authentication Result / Token
Web  -> Vibe Maker API
API  -> Validate Identity
API  -> Upsert USER
API  -> Load Role / Status
API  -> Return Vibe Maker Session/Profile
```

企業正式環境優先採 AD/Entra ID/OIDC 類型的 SSO。Vibe Maker 不保存企業密碼。若現場為傳統 AD/LDAP，應由獨立 Authentication Adapter 封裝，避免業務層直接依賴 LDAP。

| 角色 | 權限 |
|---|---|
| Admin | 帳號狀態管理、角色設定、Runtime 查詢/強制停止、系統設定；後續可管理模型與 MCP 權限。 |
| User | 建立與管理自己的 Workspace/Conversation、調用 Agent、停止自己的執行。 |

## 5. Chat / Conversation 設計

Conversation 是 UI/業務物件；Agent Session 是底層 Agent 狀態。兩者不可視為同一物件，以避免未來更換 Agent Harness 時影響 Chat 資料。

```text
USER
 └─ WORKSPACE
     ├─ CONVERSATION
     │   ├─ MESSAGE
     │   └─ AGENT_SESSION
     └─ AGENT_RUNTIME
```

| 功能 | MVP 行為 |
|---|---|
| New Chat | 在指定 Workspace 建立 Conversation。 |
| Conversation List | 僅回傳登入使用者有權限的 Conversation。 |
| Send Message | 先保存 User Message，再觸發 Agent execution。 |
| Streaming | 透過 SSE 回傳 Agent 狀態、文字增量、Tool execution 與完成事件。 |
| Stop | 使用者可中止目前 execution；Conversation/既有訊息保留。 |
| Resume | 再次發送訊息時重用 Agent Session；Runtime 若已停止則先恢復。 |

## 6. Agent Runtime 設計

### 6.1 Runtime 邊界

MVP 建議採「一個 Active Workspace 對應一個 Agent Container」。同一使用者可擁有多個 Workspace，因此 Runtime 不應只以 user_id 作唯一鍵。

```text
Workspace A -> Runtime A -> Podman Container A -> /workspace A
Workspace B -> Runtime B -> Podman Container B -> /workspace B

Shared:
  Agent Image
  LiteLLM
  Vibe Maker API

Isolated:
  Filesystem / workspace
  process
  environment
  runtime identity
  resource quota
  agent session data (as designed)
```

### 6.2 Runtime Lifecycle

```text
NOT_CREATED
    │ create
    ▼
CREATED
    │ start
    ▼
RUNNING ── idle timeout ──> STOPPED
   │                         │
   │ execution              │ resume
   ▼                         └──────> RUNNING
BUSY
   │ complete/stop
   ▼
RUNNING

Any state -> ERROR
STOPPED/CREATED -> DELETE -> DELETED
```

### 6.3 Runtime Manager Interface

```csharp
public interface IAgentRuntimeManager
{
    Task<RuntimeInfo> EnsureRuntimeAsync(Guid workspaceId, CancellationToken ct);
    Task StartAsync(Guid runtimeId, CancellationToken ct);
    Task StopAsync(Guid runtimeId, CancellationToken ct);
    Task DeleteAsync(Guid runtimeId, CancellationToken ct);
    Task<RuntimeInfo> GetStatusAsync(Guid runtimeId, CancellationToken ct);
    Task<AgentExecution> ExecuteAsync(
        Guid runtimeId,
        AgentExecutionRequest request,
        CancellationToken ct);
}
```

PodmanRuntimeManager 為 MVP 實作。未來可新增 KubernetesRuntimeManager，而不修改 Conversation/Agent Service 的核心流程。

## 7. Podman 執行規格

| 項目 | MVP 規則 |
|---|---|
| 模式 | Rootless Podman。 |
| Container Image | 統一版本化，例如 vibemaker/agent-runtime:{version}。 |
| Volume | 每個 Workspace 掛載獨立 host path/volume 至 /workspace；禁止跨 Workspace 共用可寫 volume。 |
| CPU/Memory | Runtime Manager 建立時套用系統預設 quota；數值由設定檔控制。 |
| Capabilities | 預設 drop 不必要 capabilities；禁止 privileged。 |
| Host Access | 不得 mount Podman socket、host root、敏感系統路徑。 |
| Network | 預設僅允許必要服務；至少須可連 LiteLLM。正式版再細化 egress policy。 |
| Secrets | 不直接寫入 Workspace；使用短效 token 或 secrets reference/injection。 |
| Idle | 可設定 idle timeout，自動停止 Container，但保留 Workspace。 |

## 8. 資料模型

### USER

| 欄位 | 型別 | 說明 |
|---|---|---|
| id | uniqueidentifier | PK |
| external_account | nvarchar(200) | AD/SSO account |
| display_name | nvarchar(200) |   |
| email | nvarchar(320) | nullable |
| department | nvarchar(200) | nullable |
| role | varchar(30) | ADMIN/USER |
| status | varchar(30) | ACTIVE/DISABLED |
| last_login_at | datetime2 |   |
| created_at | datetime2 |   |
| updated_at | datetime2 |   |

### WORKSPACE

| 欄位 | 型別 | 說明 |
|---|---|---|
| id | uniqueidentifier | PK |
| user_id | uniqueidentifier | FK USER |
| name | nvarchar(200) |   |
| storage_key | nvarchar(500) | logical storage reference |
| status | varchar(30) | ACTIVE/ARCHIVED |
| created_at | datetime2 |   |
| updated_at | datetime2 |   |

### CONVERSATION

| 欄位 | 型別 | 說明 |
|---|---|---|
| id | uniqueidentifier | PK |
| workspace_id | uniqueidentifier | FK |
| user_id | uniqueidentifier | FK |
| title | nvarchar(300) |   |
| status | varchar(30) | ACTIVE/ARCHIVED |
| created_at | datetime2 |   |
| updated_at | datetime2 |   |

### MESSAGE

| 欄位 | 型別 | 說明 |
|---|---|---|
| id | uniqueidentifier | PK |
| conversation_id | uniqueidentifier | FK |
| role | varchar(30) | USER/ASSISTANT/SYSTEM/TOOL |
| content | nvarchar(max) |   |
| message_type | varchar(50) | TEXT/STATUS/TOOL_EVENT/ERROR |
| sequence_no | bigint | conversation order |
| created_at | datetime2 |   |

### AGENT_SESSION

| 欄位 | 型別 | 說明 |
|---|---|---|
| id | uniqueidentifier | PK |
| conversation_id | uniqueidentifier | FK |
| workspace_id | uniqueidentifier | FK |
| runtime_id | uniqueidentifier | FK |
| provider | varchar(50) | PI |
| provider_session_id | nvarchar(300) | nullable |
| status | varchar(30) | ACTIVE/ERROR/CLOSED |
| created_at | datetime2 |   |
| updated_at | datetime2 |   |

### AGENT_RUNTIME

| 欄位 | 型別 | 說明 |
|---|---|---|
| id | uniqueidentifier | PK |
| workspace_id | uniqueidentifier | FK, MVP unique active runtime |
| provider | varchar(50) | PODMAN |
| provider_runtime_id | nvarchar(300) | container id/name |
| image_version | nvarchar(100) |   |
| status | varchar(30) | CREATED/RUNNING/BUSY/STOPPED/ERROR |
| last_active_at | datetime2 |   |
| created_at | datetime2 |   |
| updated_at | datetime2 |   |

### AGENT_EXECUTION

| 欄位 | 型別 | 說明 |
|---|---|---|
| id | uniqueidentifier | PK |
| conversation_id | uniqueidentifier | FK |
| agent_session_id | uniqueidentifier | FK |
| user_message_id | uniqueidentifier | FK |
| status | varchar(30) | QUEUED/RUNNING/COMPLETED/FAILED/CANCELLED |
| started_at | datetime2 |   |
| ended_at | datetime2 | nullable |
| error_code | varchar(100) | nullable |

## 9. API 契約（MVP）

| Method | Path | 用途 |
|---|---|---|
| GET | /api/me | 取得目前使用者資訊。 |
| GET | /api/workspaces | Workspace 列表。 |
| POST | /api/workspaces | 建立 Workspace。 |
| GET | /api/conversations | Conversation 列表，可依 workspaceId 篩選。 |
| POST | /api/conversations | 建立 Conversation。 |
| GET | /api/conversations/{id} | 取得 Conversation 與必要 metadata。 |
| GET | /api/conversations/{id}/messages | 取得歷史訊息。 |
| POST | /api/conversations/{id}/messages | 保存 User Message 並建立 Agent Execution。 |
| GET | /api/executions/{id}/events | SSE：取得 execution 即時事件。 |
| POST | /api/executions/{id}/cancel | 中止執行。 |
| GET | /api/workspaces/{id}/runtime | Runtime 狀態。 |
| POST | /api/workspaces/{id}/runtime/stop | 使用者或 Admin 停止 Runtime。 |

### 9.1 Send Message

```http
POST /api/conversations/{conversationId}/messages

{
  "content": "幫我建立一個 Todo List 網站",
  "clientRequestId": "uuid"
}

202 Accepted
{
  "messageId": "uuid",
  "executionId": "uuid",
  "eventStreamUrl": "/api/executions/{executionId}/events"
}
```

clientRequestId 用於防止前端重送造成重複 execution。後端應對 user + clientRequestId 建立 idempotency 約束或檢查。

## 10. SSE Event Contract

MVP 建議 SSE 用於單向 Agent streaming；控制命令（send/cancel）走 REST。若未來需要大量雙向互動，再評估 WebSocket/SignalR。

| event | 用途 | data 範例 |
|---|---|---|
| execution.started | Agent 開始執行 | {"executionId":"..."} |
| assistant.delta | LLM/Agent 文字增量 | {"text":"正在建立"} |
| tool.started | 工具開始 | {"tool":"shell","callId":"...","summary":"npm install"} |
| tool.completed | 工具完成 | {"callId":"...","success":true} |
| status | 非文字狀態 | {"text":"正在準備 Runtime"} |
| execution.completed | 執行完成 | {"messageId":"..."} |
| execution.failed | 執行失敗 | {"code":"AGENT_RUNTIME_ERROR","message":"..."} |
| execution.cancelled | 使用者中止 | {"executionId":"..."} |

安全規則：Tool event 預設只回傳摘要，不直接將 secret、完整環境變數、Authorization header 或敏感 command output 傳至瀏覽器。

## 11. Send Message 後端時序

```text
Angular
  │ POST message
  ▼
Conversation API
  │ authorize conversation ownership
  │ persist USER message
  │ create AGENT_EXECUTION(QUEUED)
  ▼
Agent Service
  │ load/create AGENT_SESSION
  ▼
Runtime Manager
  │ EnsureRuntime(workspace)
  │ start if stopped
  ▼
Podman Runtime
  │ invoke Pi Agent
  ▼
Pi Agent ──> LiteLLM ──> LLM
  │
  ├─ text delta
  ├─ tool events
  └─ final
  ▼
Agent Service
  │ persist ASSISTANT message
  │ execution=COMPLETED
  ▼
SSE -> Angular
```

## 12. 權限與安全要求

- 所有 Workspace、Conversation、Message、Execution API 必須在 Server Side 驗證 owner/role，不可信任前端傳入的 user_id。
- Container 必須 Rootless、non-privileged，禁止直接取得 host container runtime socket。
- Workspace host path 由 Runtime Manager 根據 workspace_id 解析；API 不接受任意 host path。
- Agent 不得取得 Vibe Maker DB 連線字串或 AD 管理憑證。
- LiteLLM key/credential 不應落地到使用者可讀 Workspace；以 runtime injection 或 gateway identity 管理。
- 所有 runtime create/start/stop/delete、login、agent execution、admin action 寫入 Audit Log。
- 錯誤回應不得暴露 host path、stack trace、token、connection string。
- 使用者被 Disabled 後，不得再建立 execution；其 active runtime 應由管理流程停止。

## 13. 錯誤處理

| 錯誤碼 | HTTP/事件 | 處理 |
|---|---|---|
| AUTH_REQUIRED | 401 | 未登入或 token 無效。 |
| FORBIDDEN | 403 | 沒有 Workspace/Conversation 權限。 |
| CONVERSATION_NOT_FOUND | 404 | Conversation 不存在或不可見。 |
| EXECUTION_CONFLICT | 409 | 同一 Conversation/Session 不允許的並行 execution。 |
| RUNTIME_START_FAILED | SSE failed / 500 | Runtime Manager 啟動 Podman 失敗。 |
| AGENT_TIMEOUT | SSE failed | Agent 超過 execution timeout。 |
| AGENT_RUNTIME_ERROR | SSE failed | Agent process/container 異常。 |
| MODEL_PROVIDER_ERROR | SSE failed | LiteLLM/模型端失敗。 |
| EXECUTION_CANCELLED | SSE cancelled | 使用者中止，非系統錯誤。 |

## 14. Concurrency 與狀態規則

- MVP 建議同一 Conversation 同時間只允許一個 RUNNING execution，避免 Agent Session history 競態。
- 同一 Workspace 可先限制為單一 BUSY Agent execution；若後續需要並行，需將 Workspace file locking 納入設計。
- Runtime Manager 的 EnsureRuntime 必須具備 concurrency protection，避免兩個 request 同時建立兩個 Container。
- 所有 execution 必須具 CancellationToken / cancellation mechanism。
- 服務重啟後，RUNNING/QUEUED execution 必須執行 reconciliation，不可永久卡在 RUNNING。

## 15. Runtime Recovery / Idle Strategy

Container 是運算資源，不是唯一資料來源。Runtime 被停止或重建後，Workspace 與 Conversation 必須仍存在。

```text
last_active_at > idle_timeout
        │
        ▼
Runtime Manager Stop
        │
        ├─ Container stopped
        ├─ Workspace retained
        └─ Runtime metadata retained

Next Message
        │
        ▼
EnsureRuntime
        │
        ├─ stopped -> start
        └─ missing -> recreate + mount same workspace
```

## 16. 部署拓樸（MVP）

```text
Corporate Network
│
├─ Reverse Proxy / HTTPS
│       │
│       ▼
│   Vibe Maker Web
│       │
│       ▼
│   .NET 10 API
│       ├─ SQL Server
│       ├─ Runtime Manager
│       └─ Audit
│
├─ Rootless Podman Host
│       ├─ workspace-runtime-001
│       ├─ workspace-runtime-002
│       └─ workspace-runtime-N
│
├─ LiteLLM
│       ├─ Cloud Models
│       └─ Local Models
│
└─ Workspace Storage
        └─ {workspace_id}/
```

MVP 可先單機部署 API + Podman，但程式設計不得假設兩者永遠在同一 process。Runtime Manager 介面需保留未來拆成獨立 Runtime Service 的可能性。

## 17. Configuration

| 設定 | 說明 |
|---|---|
| Runtime:Provider | Podman |
| Runtime:Image | Agent runtime image/version |
| Runtime:IdleTimeoutMinutes | 閒置多久自動停止。 |
| Runtime:MemoryLimit | 每 Workspace Runtime memory quota。 |
| Runtime:CpuLimit | 每 Workspace Runtime CPU quota。 |
| Runtime:ExecutionTimeoutMinutes | 單次 Agent execution timeout。 |
| Workspace:Root | Host workspace root；僅 Runtime Service 可解析。 |
| LiteLLM:BaseUrl | LiteLLM endpoint。 |
| Auth:* | OIDC/AD Authentication Adapter 設定。 |

## 18. Logging / Audit / Observability

- 每個 HTTP request 使用 correlation_id。
- 每個 Agent 執行使用 execution_id，並貫穿 API、Runtime Manager、Agent Adapter、LiteLLM request metadata。
- Application log 與 Audit log 分離。
- Audit 至少記錄 actor、action、target_type、target_id、result、timestamp、correlation_id。
- 不可在一般 log 記錄完整 Prompt/Response secrets；若需保存對話，使用 MESSAGE 資料模型並套用資料權限。
- MVP 監控指標至少包含 active runtimes、busy runtimes、execution duration、execution failures、runtime start failures。

## 19. 工程模組建議

```text
src/
├─ VibeMaker.Api
├─ VibeMaker.Application
│   ├─ Auth
│   ├─ Users
│   ├─ Workspaces
│   ├─ Conversations
│   └─ Agents
├─ VibeMaker.Domain
├─ VibeMaker.Infrastructure
│   ├─ SqlServer
│   ├─ Identity
│   ├─ Podman
│   ├─ PiAgent
│   └─ LiteLLM
└─ VibeMaker.Contracts
```

關鍵 Adapter/Interface 建議：IIdentityProvider、IAgentRuntimeManager、IAgentHarness、IModelGateway。避免 Application Layer 直接執行 podman command 或直接依賴 Pi-specific protocol。

## 20. 開發階段拆分

| Sprint | 範圍 | Done Definition |
|---|---|---|
| Sprint 1 | Auth + User | 企業登入成功；User upsert；Admin/User authorization 可運作。 |
| Sprint 2 | Workspace + Conversation + Chat UI | 可建立 Workspace/Conversation；歷史訊息可讀寫；Chat UI 完成。 |
| Sprint 3 | Agent Adapter + LiteLLM + SSE | Prompt 可送至 Pi Agent/LiteLLM；文字與狀態可串流回 UI。 |
| Sprint 4 | Podman Runtime | 每 Workspace 隔離 Runtime；start/stop/resume；workspace persistence。 |
| Sprint 5 | Hardening | Cancel、timeout、recovery、audit、quota、錯誤處理與整合測試。 |

## 21. MVP 驗收條件

1. 使用者可透過企業帳號登入，Disabled User 無法使用 Agent。
2. User A 無法透過修改 URL/API payload 存取 User B 的 Workspace、Conversation、Message 或 Runtime。
3. 使用者可建立 Workspace 與多個 Conversation，重新登入後資料仍存在。
4. 使用者發送訊息後，API 回傳 executionId，UI 可透過 SSE 即時顯示 Agent 回應。
5. Agent 可透過 Pi Agent Harness 調用 LiteLLM 並完成至少一個文字任務。
6. Agent 可在自己的 /workspace 建立檔案，其他 Workspace Runtime 無法直接看到該檔案。
7. Runtime 停止後 Workspace 資料仍存在；再次發送訊息可恢復或重建 Runtime。
8. Container 以 Rootless、non-privileged 方式執行，未掛載 host runtime socket。
9. 使用者可取消 RUNNING execution，狀態正確變更為 CANCELLED。
10. Podman/LiteLLM/Agent 發生錯誤時，前端收到可理解的錯誤事件，Server log 可透過 correlation_id / execution_id 追蹤。
11. 同一 Conversation 的並行送出不會造成兩個互相覆蓋的 Agent execution。
12. 所有 runtime lifecycle 與 agent execution 皆有 Audit 紀錄。

## 22. 後續擴充介面

Core MVP 完成後，以下能力應以外掛/服務方式接入，而不是改寫 Chat/Conversation 核心：

- MCP Gateway：EIP MCP、MES MCP、RAG MCP、SQLite MCP、OneDrive MCP。
- Model ACL / department quota / usage dashboard。
- 小工具 Preview、Port/URL Proxy、動態上架與版本管理。
- Kubernetes Runtime Provider / Agent Sandbox。
- 多 Agent / sub-agent orchestration。
- RAG workspace、檔案附件、圖片與語音輸入。

## 23. 開發前待確認項目

| 項目 | 建議預設 | 需要確認 |
|---|---|---|
| 企業身份來源 | OIDC/Entra ID 優先；傳統 AD 則做 Adapter | 現場 AD 類型與可用協定。 |
| Podman Host OS | Linux | 正式部署 OS/版本。 |
| Workspace Storage | Local/NAS filesystem | 容量、備份、是否需 HA。 |
| Pi Agent Integration | Agent Adapter | Pi 的實際啟動/Session/streaming protocol。 |
| LiteLLM Auth | Server-to-server credential | 是否依 user/department 發 virtual key。 |
| Runtime Quota | 設定檔管理 | 預設 CPU/RAM/timeout 數值。 |
| Retention | Conversation/Workspace 保留 | 公司資料保存政策。 |

## 24. SA 結論

Vibe Maker Core MVP 應將「企業身份與對話 Control Plane」和「Agent 執行 Runtime」清楚分離。Angular/.NET 10 負責使用者、權限、Conversation、Agent orchestration；Rootless Podman 提供每個 Active Workspace 的隔離執行環境；Pi Agent Harness 負責 Agent loop；LiteLLM 作為統一模型入口。第一階段先確保 Login → Chat → Agent → Streaming → Persistence → Runtime Isolation 這條鏈路可靠，再向 MCP、RAG 與工具上架擴充。
