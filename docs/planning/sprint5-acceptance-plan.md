# Sprint 5 驗收計畫（交給 Codex 執行）

> 2026-10-07 由 Claude 撰寫。範圍：SA §21 的 12 項 MVP 驗收條件，加上開發規劃新增的 13～16 項。
> 「強化」的部分另案處理，見文末，不在這份計畫內。

## 執行前必讀（給 Codex）

- 先讀 `CLAUDE.md`、`docs/sa/vibe-maker-core-mvp-sa.md` §12～§21、`docs/adr/`（目前到 ADR-0011）、`docs/progress/` 目前的看板。
- **只補測試與驗收文件，不改產品行為。** 發現產品缺口時：
  - 在看板留言，標明哪一項驗收條件、缺什麼；
  - 測試寫好但先標成 `Skip`，並附上原因與看板連結，等缺口修好再打開；
  - 監控指標、log 的 ExecutionId scope、健康檢查、安全標頭、保存期限已在 2026-10-07 的強化完成（看板 #034），AC-10 可以直接驗證 log scope；
  - **不得刪除或停用既有測試**。
- 測試規範：
  - xUnit v3，用 `TestContext.Current.CancellationToken`；
  - 外部依賴一律用 Fake LLM / Fake OIDC / Scripted harness，不得呼叫真實模型；
  - 端對端用 Playwright（`web/e2e/*.mjs`），沿用既有腳本的寫法：`step()` 印出 ✔、每個 step 截圖。
- 每完成一個切片，依 `CLAUDE.md` 的「每次修改後的流程」做完驗證、commit、看板留言、push、PR、CI 綠燈、merge。

## 產出

1. **`web/e2e/acceptance-flow.mjs`**（`npm run e2e:acceptance`）：
   - 以使用者角度依序走完下表標示「E2E」的條件；
   - 每一步的 `step()` 訊息開頭標上條件編號，例如 `✔ [AC-04] …`；
   - 截圖放在 `<截圖目錄>/AC-XX-*.png`。
2. **`tests/Ymir.IntegrationTests/Acceptance/`**：
   - 每項條件一個測試類別，命名 `Ac01_...Tests`、`Ac02_...Tests`……；
   - 只補既有測試沒涵蓋的部分，已涵蓋的用 `// 已由 XxxTests.Method 涵蓋` 註明，不要重寫。
3. **`docs/acceptance/mvp-acceptance-report.md`**：
   - 每項條件的結果（✅ 自動化通過 / 🧪 手動驗證步驟 / ⏳ 待使用者環境）與證據（測試名稱、截圖檔名、CI run 連結）；
   - 最後列出所有「待使用者環境確認」的手動步驟，整理成一份清單。
4. **CI**：
   - Podman 相關的驗收（AC-06、AC-08）加在既有的 `Agent runtime image` job 後面，或新增一個 job，只在 GitHub Actions 的 Ubuntu runner 上跑；
   - Playwright 的 `e2e:acceptance` **先不進 CI**，在沙箱手動跑，截圖附在報告。

## 驗收條件對照

「現有涵蓋」是 2026-10-07 時的狀態，Codex 動工前請再確認一次。

| # | 條件（SA §21） | 現有涵蓋 | 要補的 | 方式 |
|---|---|---|---|---|
| AC-01 | 企業帳號登入；Disabled User 無法使用 Agent | `OidcLoginTests`、`LocalAccountTests`（含停用）、`e2e:auth` | ① 停用後**既有 cookie** 立即無法送訊息、SSE 連線被拒；② 停用時進行中的 execution 被取消、runtime 被停止（`UserDeactivationService`） | 整合 + E2E |
| AC-02 | User A 改 URL / payload 無法存取 User B 的資源 | `AuthorizationMatrixTests`（所有 `/api` 端點 × 非擁有者） | ① 在 payload 夾帶別人的 `projectId` 建立對話；② 下載檔案時用 `path` 指向別人的目錄、`..`、絕對路徑；③ SSE 用別人的 executionId 加 `Last-Event-ID` | 整合 |
| AC-03 | 建立專案與多個對話，重新登入後資料仍在 | `ProjectConversationTests`、`e2e`（chat-flow 重新整理） | 登出 → 再登入（不是重新整理）後，側邊欄、歷史訊息、專案設定都還在 | E2E |
| AC-04 | 送訊息回傳 executionId，SSE 即時顯示 | `ExecutionFlowTests`、`e2e` | 斷言 202 回應含 `executionId` / `eventStreamUrl`；UI 在完成前就看到部分文字（串流，不是一次出現） | E2E |
| AC-05 | 經 Pi 調用 LiteLLM 完成文字任務 | `PiExecutionTests`、`LiteLlmExecutionTests` | 以 Fake LLM 的 LiteLLM 模式（`FAKE_LLM_MASTER_KEY`）跑完整 UI 流程，確認 Agent 用的是 virtual key | E2E + 手動（真 LiteLLM + MiniMax，⏳） |
| AC-06 | Agent 在自己的 /workspace 建檔，其他人的 runtime 看不到 | Local runtime 沒有隔離，**目前沒有自動化** | Podman 測試：兩個使用者各自建檔，在 A 的 container 內 `ls /workspace` 看不到 B 的檔案；B 的 host 目錄沒有掛進 A 的 container | 整合（CI 的 Podman job） |
| AC-07 | Runtime 停止後資料仍在，再送訊息會恢復或重建 | `AdminOverviewTests.StopRuntime_*`、`RuntimeLifecycleTests.IdleRuntime_*`（Local） | Podman：stop 與 `rm -f` container 後再送訊息，檔案與 Pi session 都在（同一對話仍記得上一輪，對應 AC-14） | 整合（Podman） |
| AC-08 | Rootless、non-privileged、沒有掛 runtime socket | `ContainerCommandBuilderTests`（指令層級） | 對實際啟動的 container 執行 `podman inspect`：沒有 `Privileged`、沒有 `*.sock` 掛載、`CapDrop` 含 ALL、`no-new-privileges`、user namespace 不是 root | 整合（Podman）+ 手動（正式主機，⏳） |
| AC-09 | 可取消 RUNNING execution，狀態變 CANCELLED | `ExecutionFlowTests`、`e2e`（停止） | 斷言資料庫的 `AgentExecution.Status == Cancelled`、SSE 最後一個事件是 `execution.cancelled`、有 `execution.cancel` 稽核 | 整合 |
| AC-10 | 錯誤時前端收到可理解的訊息，server log 可用 correlation id / execution id 追蹤 | `UnreachableLiteLlmTests`、`ExecutionTimeoutTests` | ① Fake LLM `[fail]`、runtime 啟動失敗、逾時：UI 顯示中文摘要，不含 host path、stack trace、token；② 用 `TestOutputLogger` 收 log，斷言 log 含該 execution id 與 trace id；③ 稽核的 `CorrelationId` 與 log 的 trace id 一致 | 整合 + E2E |
| AC-11 | 同一對話並行送出不會產生兩個互相覆蓋的 execution | `PersistenceConstraintTests`（filtered unique index） | 用 `Task.WhenAll` 同時送 10 次（不同 `clientRequestId`）：只有 1 個 202、其他 409；同一個 `clientRequestId` 重送回同一個 executionId | 整合 |
| AC-12 | 所有 runtime lifecycle 與 agent execution 都有稽核 | execution 的建立 / 開始 / 結束 / 取消；runtime 的 `runtime.create` / `runtime.start` / `runtime.stop` / `runtime.reconcile` / `runtime.idle_stop`、Admin 停止（2026-10-07 強化已補上） | 一個測試走完：第一次送訊息（create）→ Admin 停止 → 再送訊息（Local 為 create、Podman 為 start）→ 閒置停止 → 對帳，逐一斷言稽核動作與 `TargetId` | 整合 |
| AC-13 | SSE 斷線後以 `Last-Event-ID` 重連，不遺失、不重複 | `ExecutionFlowTests`（續傳） | 執行中斷線，再用 `Last-Event-ID` 重連：事件序號連續、沒有重複；頁面重新整理後接回（`resumeTurnFrom`） | 整合 + E2E |
| AC-14 | Container 重建後，同一對話仍保有上下文 | `PiExecutionTests`（session 續接） | 與 AC-07 合併：`rm -f` container 後第二輪回覆仍提到第一輪 | 整合（Podman） |
| AC-15 | 檔案 API 無法透過 `..` 或 symlink 讀到 workspace 以外 | `WorkspaceFileTests` | 補 symlink 指向 `/etc/passwd`、指向其他對話目錄、URL 編碼的 `..%2f` | 整合 |
| AC-16 | Container 內拿不到 LiteLLM master key | `LiteLlmExecutionTests`（Agent 只用 virtual key） | 在 container 內執行 `env`、`cat /proc/1/environ`、搜尋 `/workspace`，都找不到 master key、DB 連線字串；程序參數也沒有 key | 整合（Podman + Local） |

## 執行環境

- **沙箱 / 本機**：
  - SQL Server、Fake LLM（`FAKE_LLM_MASTER_KEY=sk-dev`）、Fake OIDC；
  - API 設定 `VibeMaker__Harness=Pi`、`VibeMaker__LiteLlm__BaseUrl=http://127.0.0.1:5199/`、`MasterKey=sk-dev`、`Ymir__Auth__Oidc__AuthorityHost=http://127.0.0.1:5299`；
  - 指令見 `CLAUDE.md` 的「端對端驗證」。
- **Podman 測試**：
  - 在 GitHub Actions Ubuntu runner（已有 rootless Podman）以 `VibeMaker:Runtime:Provider=Podman` 跑；
  - 測試以環境變數 `YMIR_ACCEPTANCE_PODMAN=1` 啟用，沒有設定時 `Skip`。
- **待使用者環境（⏳）**：
  - 真 Entra 登入；
  - 真 LiteLLM + MiniMax；
  - 正式 Linux 主機的 rootless Podman + runtime host + Quadlet；
  - Cloudflare Tunnel；
  - Windows Docker Desktop。
  - 報告中為每一項寫出逐步的手動驗證方法與預期結果。

## 完成定義

- 16 項都有對應的自動化測試或手動驗證步驟，報告中每項都有結果與證據。
- 自動化測試全部通過；`Skip` 的只限「已知產品缺口」與「需要 Podman / 真實服務」，每個都附原因。
- `e2e:acceptance` 在沙箱完整跑過，截圖附在報告。
- 看板有留言：完成項目、已知缺口、待使用者確認的清單。

---

## 強化（另案，由 Claude 規劃與實作）

和驗收平行進行；驗收發現的缺口（例如 AC-12）會併入這裡。內容見看板 #033 的提案，待使用者確認範圍後開工。
