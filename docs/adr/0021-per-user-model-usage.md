# ADR-0021：每人每模型用量（讀 LiteLLM model_groups，Ymir 不保存）

- 狀態：已採納
- 日期：2026-10-13
- 依據：使用者要求記錄每個人每個模型的用量；延伸 [ADR-0004](0004-litellm-virtual-keys.md) 補充（用量讀 LiteLLM，Ymir 不保存 token 與費用）與 [ADR-0011](0011-runtime-lifecycle-policy.md) §6（管理介面的用量）。

## 背景

- 管理介面「用量」（`GET /api/admin/usage?days=N`，ADR-0011 §6）原本只有每位使用者的合計：執行數來自 Ymir，費用、token 與請求數來自 LiteLLM。管理員看不出每個人用了哪些模型、各花了多少。
- ADR-0004 補充已經決定：Ymir 不自己保存 token 或費用，用量一律讀 LiteLLM。
- LiteLLM（版本鎖定在 `deploy/litellm/compose.yml` 的 1.103.2）已經在 `LiteLLM_DailyUserSpend` 依使用者、UTC 日期、key、供應商模型（`litellm_params.model`）、provider 與 endpoint 累計請求、token 與費用。
  - 每筆紀錄的 `model_group`（公開的 `model_name`）不在唯一鍵內，是當天第一個寫入這筆紀錄的請求所屬的 group（LiteLLM 原始碼 `proxy/schema.prisma` 的 `@@unique`、`db_transaction_queue/daily_spend_bulk_upsert.py`）。
  - spend log 的保存期限設定（`maximum_spend_logs_retention_period`，Ymir 的 `config.yaml` 沒有設定）只清理逐筆的 spend log，不清理這張每日彙總表（LiteLLM 原始碼 `db_transaction_queue/spend_log_cleanup.py:463-501`），所以歷史一直保留在 LiteLLM。
- Ymir 每次執行都保存使用者選的模型（`AgentExecution.ModelId`）。它與 LiteLLM 的公開 `model_name` 相同（`deploy/litellm/README.md`「新增模型」要求兩邊同名）；`ModelId` 欄位加入之前的舊執行為 null。

## 決策

1. **資料來源不變，多讀一個欄位**：仍以 master key 呼叫 `GET /user/daily/activity?user_id=&start_date=&end_date=&page=&page_size=1000`。`page_size` 由 100 改為 LiteLLM 的上限 1000，最多 20 頁。
   - 每筆 `results[]` 除了當天總計 `metrics`，另讀 `breakdown.model_groups.<model group>.metrics` 的 `spend`、`prompt_tokens`、`completion_tokens`、`api_requests`，跨頁、跨日依 model group 相加；空白的 key 略過。
   - LiteLLM 以 `model_group` 為 key，沒有 model group 時退回 `model`（供應商模型名稱，例如 `unknown-model`）；只有兩者都空白的紀錄不會出現在 `model_groups`，由決策 3 的差額列計入。
   - LiteLLM 依日期、id 分頁，每筆每日紀錄只出現在一頁，所以跨頁相加不會漏算或重複（依模型歸屬的限制見「影響」）。
   - **不讀 `breakdown.models`**：它的 key 是供應商的模型名稱（含 provider 前綴），與 Ymir 的模型 id 對不上。`api_key_breakdown`（key hash）、`providers`、`endpoints` 等其他欄位也不讀，這些資料不會離開 `LiteLlmModelGateway`（SA §12）。唯一的例外是上一點的退回：沒有 model group 的紀錄會以供應商模型名稱出現在 `model_groups`，顯示為 `Unlisted`；模型名稱不是機密，且只有 Admin 看得到。
   - 第 20 頁仍有下一頁時寫 warning log；總計與依模型拆分以相同方式截斷，兩者仍一致。
2. **以模型 id 合併**：Application 層的純函式 `ModelUsageBreakdown.Merge` 把 Ymir 依 `AgentExecution.ModelId` 分組的執行數（含失敗數）與 LiteLLM 的 model group 合併成每人每模型的列，模型 id 以 ordinal 比較。
3. **「未標示模型」列（`ModelId = null`）**：
   - 執行數：舊執行（沒有保存模型）的次數；
   - 請求、token 與費用：LiteLLM 總計減去各 model group 的加總，每個欄位不小於 0，費用差額低於 0.000001 視為 0。
   - 各列加總因此等於該使用者的總計：執行數一定相等；有 LiteLLM 資料時，請求、token 與費用也相等。
   - 沒有執行、也沒有任何用量的列（包括這一列）不顯示。
4. **四種分類**（`ModelUsageKind`）：

   | 分類 | 條件 | 顯示名稱 |
   |---|---|---|
   | `Catalog` | 在模型清單 `VibeMaker:Models` 內 | 清單的 `DisplayName` |
   | `Embedding` | 等於知識庫的 `VibeMaker:Rag:EmbeddingModel`（ADR-0014） | 模型 id |
   | `Unlisted` | 其他，例如已移除、改名的模型、LiteLLM 的 wildcard group，或沒有 model group 時以供應商模型名稱為 key 的紀錄 | 模型 id |
   | `Unattributed` | 「未標示模型」那一列 | 「（未標示模型）」 |

   - embedding 模型若也列在模型清單內，以 `Catalog` 為準。
   - 排序：費用、請求數、執行數由多到少，再依模型 id；「未標示模型」一律最後。
5. **查詢 LiteLLM 的對象**：期間內有執行的使用者，加上用過 runtime 的使用者與上傳過知識庫文件的使用者。知識庫的 key 掛在 `Guid.Empty` runtime 上，只用知識庫的使用者沒有 runtime 紀錄（ADR-0014）。
6. **期間的差異照實呈現**：LiteLLM 以 UTC 日期計算（含今天共 N 天，台灣時間 08:00 換日），Ymir 的執行數以近 N×24 小時計算（同 ADR-0011 §6）。用量頁的註腳說明這一點；`days=1` 在 UTC 午夜前後兩者可能對不上。
7. **API 只新增欄位**：`GET /api/admin/usage` 的每位使用者新增 `models[]`（`modelId`、`displayName`、`kind`、`executions`、`failed`、`spendUsd`、`promptTokens`、`completionTokens`、`modelRequests`）。
   - 既有欄位的意義與順序不變；不新增端點、資料表或 migration；仍只限 Admin（`AuthSetup.AdminPolicy`）。
   - 沒有連接 LiteLLM、或這位使用者查詢失敗時，各模型的 LiteLLM 欄位為 null（0 表示沒有用量）。沒有連接 LiteLLM 時用量頁隱藏這些欄位、只顯示執行數；單一使用者查詢失敗時該使用者顯示「—」，不影響其他人。
8. **前端**：用量頁新增「依模型」卡片，包含所有使用者依模型的合計（使用人數、費用佔比），以及每人每模型的明細。只用文字綁定，CSP 不變。
9. **範圍邊界**：下列做法都要另立新的 ADR 推翻 ADR-0004 補充，這次不做：
   - 以本地時區計算日期；
   - 在 Ymir 保存用量歷史（例如快照表，或解析 Pi 回報的 token）；
   - 依對話、專案或單次執行拆分用量。

   另外也不做成員查看自己的用量、每模型預算或配額、單價表。

## 影響

- **升級 LiteLLM 必須重新確認回應格式**：`LiteLlmModelGatewayTests` 的 fixture 鎖定 1.103.2 的 `breakdown.model_groups` 格式。新版若拿掉或改名這個欄位，依模型拆分會變成空的、全部落到「未標示模型」，總計仍然正確。
- **同一個上游模型的多個 alias 會合併**：每日彙總不以 model group 區分。兩個 Ymir 模型 id（`model_name`）若指向相同的 `litellm_params.model` 與 provider，同一天、同一個 key 的請求、token 與費用都記在當天先寫入的那個 id，另一個只顯示執行數、用量為 0；總計仍然正確。
  - 因此 `config.yaml` 中每個 `model_name` 的 `litellm_params.model`（含 provider）不可重複（`deploy/litellm/README.md`「新增模型」）。
  - 若真的需要分開同一上游模型的 alias，要改讀逐筆的 spend log，並另立 ADR。
- **Agent 與知識庫無法分開**：同一個聊天模型同時用於 Agent 與知識庫問答時，LiteLLM 只有一個 model group。Agent 在 container 內以 virtual key 自行呼叫模型（例如 `curl`）也算在同一個模型，但沒有對應的執行。
- **舊資料的執行數與費用落在不同列**：舊執行的次數在「未標示模型」，LiteLLM 仍把它們的 token 與費用記在實際的模型下。
- **歷史只在 LiteLLM 的 PostgreSQL**：遺失這個資料庫時，每人每模型用量與總計一起歸零（`docs/guides/backup-restore.md`）。
- **上限**：每位使用者最多 20 頁 × 1000 筆每日紀錄，超過時截斷並寫 warning，一般使用遠低於一頁。分頁期間 LiteLLM 寫入新紀錄可能讓分頁邊界的一筆重複計算，影響可以忽略。
  - 每頁仍包含 `api_key_breakdown`，重度使用者一次可能有數百 KB；管理頁最多同時查 4 位使用者，可以接受。
- **列不出來的用量**：沒有 `user_id` 的舊 key（ADR-0004 補充），以及沒有執行、沒有 runtime 也沒有知識庫文件的使用者。
- LiteLLM 以批次（約 10 秒）寫入每日彙總，剛結束的呼叫可能稍後才出現。
- 沒有設定單價的模型費用顯示 US$0.00，token 數仍然正確（`deploy/litellm/README.md`「單價」）。
- **測試替身**：Fake LLM 依 request 的 model 記錄（embeddings 費用 0）並回傳 `breakdown.model_groups`；`breakdown.models` 的 key 故意加上 `fake-provider/` 前綴，整合測試因此能證明 Ymir 讀的是 model group。
