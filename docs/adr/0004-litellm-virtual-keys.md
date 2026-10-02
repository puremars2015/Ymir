# ADR-0004：Container 內使用 LiteLLM Virtual Key

- 狀態：已採納（Sprint 4 實作）
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

## 影響

- 即使 key 外洩，影響範圍限於單一使用者、有限模型、有限預算與時間。
- 免費取得 per-user / per-workspace 用量資料，Phase 2 的 Usage & Quota（SA Phase 2+）有現成基礎。
- API 需要持有 LiteLLM master key（只存在 API 的 secret 設定，不進 container）。
- **LiteLLM 的 virtual key 功能需要 PostgreSQL**（Sprint 0 實測：沒有 `DATABASE_URL` 時 `/key/generate` 直接失敗）。
  部署拓樸（SA §16）需新增一個 LiteLLM 專用的 PostgreSQL；Ymir 自己的資料仍在 SQL Server。若公司不允許 PostgreSQL，
  替代方案是在 API 前面做一個「模型 proxy」：container 只拿到 Ymir 自己發的短效 token，由 API 驗證後換成 master key 轉發給 LiteLLM。
