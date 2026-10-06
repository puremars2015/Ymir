# ADR-0011：Runtime 生命週期與執行政策

- 狀態：已採納（補充 ADR-0007、ADR-0008、ADR-0010）
- 日期：2026-10-07

## 背景

Sprint 4 的範圍是 runtime 的完整生命週期：閒置停止、啟動時對帳、配額、執行逾時設定，以及在管理介面顯示用量。

開始實作前發現兩個問題：

- **runtime id 只存在記憶體**：`IAgentRuntimeManager` 的停止與查詢原本以 runtime id 為參數。這個 id 由 manager 在 `EnsureRuntime` 時產生、只放在記憶體；runtime host 的 user → runtime 對照表（`RuntimeRegistry`）也一樣。
  - API 或 runtime host 重新啟動後，資料庫紀錄的 id 與 manager 的 id 就不同。
  - 結果：Admin「停止執行環境」與停用帳號時的停止都會失敗（找不到 runtime）。
- **設定寫死在部署**：`IdleTimeoutMinutes` 雖然有設定欄位，但沒有任何程式使用；執行逾時只能改 `.env` 後重新啟動。

## 決策

1. **生命週期操作一律以 user id 進行。** 一人一個 runtime（ADR-0007），user id 就足以識別。
   - `IAgentRuntimeManager` 新增 `GetStatusForUserAsync` 與 `StopForUserAsync`。
   - Podman / Docker：以 user id 推導的 container 名稱直接 inspect / stop。
   - runtime host：`GET /v1/users/{id}/runtime` 回傳 `{ status }`，沒有 runtime 時為 `NotCreated`；`POST .../stop` 回 204，沒有 runtime 時回 404。兩者都不依賴 registry。
   - 端點清單不變，也沒有接受路徑、image 或資源設定的參數。
2. **啟動時對帳**：`RuntimeLifecycleWorker` 啟動時，對每筆未刪除的 runtime 紀錄查詢實際狀態，與資料庫不同時以實際狀態為準，例如主機重開機或手動停止。
3. **閒置停止**：每隔 `IdleCheckIntervalSeconds`（預設 60 秒）檢查一次。
   - 對象：`LastActiveAt` 超過閒置時間的 runtime。`LastActiveAt` 在 execution 開始與結束時都會更新。
   - 跳過正在執行或有排隊工作的使用者：先以非阻塞方式取得 `UserExecutionLocks`；取得後才停止，所以不會停到正在執行的 container。
   - 每次停止都寫稽核 `runtime.idle_stop`，actor 為 `system`。
   - 檔案保留，下一次送訊息時由 `EnsureRuntime` 自動啟動。
4. **執行政策可由管理介面修改**：

   | 項目 | 部署設定 | 範圍 |
   |---|---|---|
   | 閒置停止時間 | `VibeMaker__Runtime__IdleTimeoutMinutes`（預設 30） | 0 = 不停止，最多 1440 分鐘 |
   | 單次執行上限 | `ExecutionTimeoutMinutes`（預設 30） | 1～240 分鐘 |
   | 每人排隊 + 執行中上限 | `MaxPendingExecutionsPerUser`（預設 5） | 1～50 |
   | 每人過去 24 小時執行次數 | `DailyExecutionLimit`（預設 0） | 0 = 不限，最多 10000 |

   - 管理介面的值存在 `platform.system_settings` 的 `vibemaker.runtime_policy`，比照 ADR-0010：資料庫優先、不必重啟。
   - 讀取快取 30 秒。
   - 資料庫內容不合法時忽略，退回部署設定。
   - 不含機密，所以以明文 JSON 保存。
5. **配額是軟性上限**：送訊息時先查再寫，超過時回 **429 `QUOTA_EXCEEDED`**，訊息說明原因。
   - 同時送出時可能略為超過。目的是防止濫用，不是一致性保證。
   - 「同一對話只有一個執行中」仍由 filtered unique index 保證（CLAUDE.md 架構規則）。
6. **用量**：`GET /api/admin/usage?days=N`（1～90）提供每位使用者的資料：
   - 執行數、完成 / 失敗 / 取消數；
   - Agent 實際執行時間（開始到結束的總和）；
   - 過去 24 小時的次數，前端與每日上限比較並標示；
   - runtime 狀態。

   模型 token 用量在 LiteLLM（ADR-0004），之後再整合。

## 影響

- 實作 `IAgentRuntimeManager` 的類別都要實作兩個新方法。
- `ContainerRuntimeManager.StopForUserAsync` 與 `EnsureRuntime` 共用同一個 per-user lock。
- runtime host 的 `GET /runtime` 回應格式改為 `RuntimeStateMessage`。舊版 API 搭配新版 runtime host 時，`GetStatusAsync` 會解析失敗，兩者必須同時升級；目前只有 API 自己使用這個端點。
- 閒置停止在單一 API instance 內以記憶體 lock 避開執行中的使用者，與 `UserExecutionLocks` 的前提相同（ADR-0007）。之後要多 instance 時，兩者都需要改成分散式 lock。
- Local runtime（開發用）沒有 container：停止只是忘記記憶體中的紀錄，下一次執行重新建立。API 重新啟動後，對帳會把紀錄標成 `NotCreated`。
