# ADR-0004：Container 內使用 LiteLLM Virtual Key

- 狀態：已採納（2026-10-05 實作，見下方「實作細節」）
- 日期：2026-10-02

## 背景

SA §12 要求「LiteLLM key 不應落地到使用者可讀 Workspace，以 runtime injection 或 gateway identity 管理」。但 Agent 在 container 內有 shell 能力，**任何以環境變數或檔案注入 container 的憑證，Agent 都讀得到**（例如 `env`、`cat /proc/self/environ`）。Prompt injection 可能誘使 Agent 把憑證外洩。

## 決策

1. Container 內**永遠不放 LiteLLM master key**。
2. `Ymir.VibeMaker.Infrastructure/LiteLLM` 在建立 / 啟動 runtime 時，呼叫 LiteLLM `/key/generate` 發一把 **virtual key**：
   - 綁定 `user_id`、`workspace_id`（metadata），用於用量追蹤
   - 限定可用模型
   - 設定預算上限與有效期（例如 24 小時，runtime 每次啟動時換發）
3. Virtual key 以環境變數注入 container，Pi 的 `models.json` 以 `"apiKey": "${LITELLM_API_KEY}"` 引用。
4. Runtime 停止 / 刪除、使用者被停用時，撤銷該 key。
5. Sprint 0 ~ 3 在開發環境先用固定的開發用 key 或 Fake LLM。

## 實作細節（2026-10-05）

- `LiteLlmModelGateway`：以 master key 呼叫 `POST /key/generate`，參數如下：
  - `models`：沒設定 `AllowedModels` 時只允許 Pi 使用的模型，不會發出不限模型的 key；
  - `duration`：預設 24h；
  - `max_budget`：可選；
  - `key_alias`：`ymir-user-{userId}-{時間}`；
  - `metadata`：`ymir_user_id`、`ymir_runtime_id`。
  
  撤銷用 `POST /key/delete`（以 LiteLLM 回傳的 hashed token）。
- 因為一個使用者一個 runtime（ADR-0007），key 以**使用者**為單位，metadata 不再有 `workspace_id`。
- `RuntimeCredentialService`：
  - 每位使用者一把 key，**只快取在 API 記憶體**，不寫資料庫、不寫 log（record 覆寫 `ToString`）；
  - 到期前 1 小時換發，換發後撤銷舊 key；
  - API 重啟後重新發一把，舊 key 最晚在有效期後失效。
- `ExecutionRunner` 在 `EnsureRuntime` 之後取得 key：
  - 經 `AgentRunRequest` 交給 `PiAgentHarness`，以 `exec --env LITELLM_API_KEY`（只傳名稱）注入；
  - 拿不到 key 時，execution 以 `MODEL_PROVIDER_ERROR` 結束。
- 沒有設定 `VibeMaker:LiteLlm:MasterKey` 時：只有 Development 可以退回固定的 `DevelopmentApiKey`，其他環境拒絕啟動。
- **尚未完成**：
  - 使用者被停用時撤銷 key：已有 `RuntimeCredentialService.RevokeAsync`，等帳號停用流程（Sprint 2 Admin）呼叫；
  - runtime idle stop 時撤銷：等 idle stop 實作。

## 補充（2026-10-07，ADR-0011）

- key 帶 LiteLLM 的 `user_id`（= Ymir 使用者 id），發 key 前先建立或更新 LiteLLM 使用者。
- 預算改為**每人每月**（使用者層級的 `max_budget` + `budget_duration: 30d`），由管理介面的執行政策設定。原本每把 key 的 `max_budget` 會隨 key 每 24 小時換發而重置，保留為相容設定。
- 管理介面的用量讀 LiteLLM 的 `/user/daily/activity` 與 `/user/info`，Ymir 不自己保存 token 或費用。

## 影響

- 即使 key 外洩，影響範圍限於單一使用者、有限模型、有限預算與時間。
- 免費取得 per-user / per-workspace 用量資料，Phase 2 的 Usage & Quota（SA Phase 2+）有現成基礎。
- API 需要持有 LiteLLM master key（只存在 API 的 secret 設定，不進 container）。
- **LiteLLM 的 virtual key 功能需要 PostgreSQL**（Sprint 0 實測：沒有 `DATABASE_URL` 時 `/key/generate` 直接失敗）。
  部署拓樸（SA §16）需新增一個 LiteLLM 專用的 PostgreSQL；Ymir 自己的資料仍在 SQL Server。若公司不允許 PostgreSQL，
  替代方案是在 API 前面做一個「模型 proxy」：container 只拿到 Ymir 自己發的短效 token，由 API 驗證後換成 master key 轉發給 LiteLLM。
