# ADR-0003：透過 `podman exec` 以 Pi RPC 模式執行 Agent

- 狀態：已採納（Sprint 0 已實測驗證，見 [`spikes/pi-rpc-poc/README.md`](../../spikes/pi-rpc-poc/README.md)）
- 日期：2026-10-02

## 背景

SA §23 把「Pi 的實際啟動 / Session / streaming protocol」列為待確認。查證 Pi 1.0（`@earendil-works/pi-coding-agent`）文件：

- `pi --mode rpc`：stdin 收 JSONL 指令（`prompt`、`abort`、`get_state`…），stdout 輸出 JSONL 回應與事件。
- 事件：`agent_start`、`message_update`（`assistantMessageEvent.type = text_delta`）、`tool_execution_start/update/end`、`agent_end`、`agent_settled`。
- Framing 只能以 LF 切行（JSON 字串內可能出現 U+2028/U+2029）。
- Session 以 JSONL 檔儲存，`--session-dir` 指定目錄；`--session-id <id>` 以指定 id 建立或續接 session；`--no-session` 為一次性。
- `models.json` 可設定 OpenAI 相容端點（`api: "openai-completions"`、`apiKey: "${ENV}"`），可直接指向 LiteLLM。

## 決策

1. **每個 Workspace 一個長駐 container**（SA §6.1；**已由 [ADR-0007](0007-one-runtime-per-user.md) 改為每個使用者一個 container**，專案是 container 內的目錄），container 主程序只負責保持存活（`sleep infinity`，搭配 `--init` 讓 `podman stop` 能立即結束），不常駐 Pi。
2. **每次 execution 以 `podman exec -i` 在該 container 內啟動一個 Pi RPC 程序**：

   ```
   podman exec -i -w /workspace <container> \
     pi --mode rpc --provider ymir --model <model> \
        --session-dir /agent-state/sessions --session-id <agent_session_id>
   ```

   API 經由 stdin/stdout 對接；執行結束（`agent_settled`）後關閉 stdin 讓 Pi 正常結束。
3. **Cancel**：送 `{"type":"abort"}`；超過寬限時間仍未結束則終止 exec 程序。**Timeout** 走同一路徑。
4. **Session 持久化**：`/agent-state` 掛載每個 Workspace 專屬的持久化目錄（與 `/workspace` 分開），container 重建後 Agent 不失憶（已實測）。
   Pi 的 session id 直接使用 `AGENT_SESSION.id`（`--session-id`），不需要另存 Pi 的檔案路徑。
   `PI_CODING_AGENT_DIR=/agent-state/pi-agent`：Pi 會寫入 `auth.json`、`models-store.json`，此目錄必須可寫。
   `models.json` 由 harness 在每個 runtime 第一次執行時，透過 runtime 內的 `sh -c 'cat > …'` 寫入（與 runtime provider 無關）。
5. **DB 的 MESSAGE 是 UI 的真實來源**；Pi session 檔遺失時，以 MESSAGE 歷史重建上下文（降級行為）。
6. **事件映射**（`Ymir.VibeMaker.Infrastructure/PiAgent/PiRpcEventMapper`）：

   | Pi 事件 | SA SSE 事件 |
   |---|---|
   | `agent_start` | `execution.started` |
   | `message_update` + `text_delta` | `assistant.delta` |
   | `tool_execution_start` | `tool.started`（只送工具名與摘要） |
   | `tool_execution_end` | `tool.completed` |
   | `auto_retry_start` / `compaction_start` | `status` |
   | `agent_settled` | `execution.completed` / `execution.cancelled`（依 stop reason） |
   | 程序異常結束、`stopReason = error` | `execution.failed` |

7. Container 內設定 `PI_OFFLINE=1`、`PI_SKIP_VERSION_CHECK=1`、`PI_TELEMETRY=0`，避免對外連線；Pi 版本在 image 內鎖定。
8. .NET 端以 `System.Diagnostics.Process` 呼叫 podman CLI（互動式 stdin/stdout 直接用 Process 最單純），封裝在 `ContainerRuntimeManager`（開發 / 驗證時也可用 Docker，見 [ADR-0005](0005-docker-for-development.md)）；Application 層只看到 `IAgentRuntimeManager` / `IAgentHarness`。
9. **與 SA §6.3 的介面差異**：`IAgentRuntimeManager.ExecuteAsync` 改為 `StartProcessAsync(runtimeId, RuntimeProcessSpec)`。Runtime Manager 只負責「在隔離環境啟動程序並提供 stdio」，Pi 協定完全由 `IAgentHarness`（`PiAgentHarness`）處理，兩者可以分別替換（例如 Kubernetes runtime + Pi，或 Podman + 其他 harness）。
10. 開發用 `LocalRuntimeManager` 直接在 host 執行（無隔離），只允許 Development 環境，用於整合測試與沒有 Podman 的開發機。

## 影響

- 每次 execution 有一次 Node 啟動成本（數百毫秒），相對於 LLM 延遲可接受。
- 不需在 container 開任何網路 port 給 API，也不需 mount podman socket。
- 同一 Workspace 多個 Conversation 共用一個 container、各自一個 Pi session 檔；SA §14 已限制同 Workspace 同時只有一個 BUSY execution。
- Agent 可讀寫自己的 session 檔（Pi 會把路徑放在 `PI_SESSION_FILE`），這在單一使用者的沙箱內可接受。
