# Pi RPC PoC（Sprint 0 技術驗證）

驗證 SA §23 列為「待確認」的 Pi 整合方式：**Runtime（Local / Podman）→ `pi --mode rpc` → OpenAI 相容模型端點（LiteLLM / Fake LLM）**，
並把 Pi 事件轉成 SA §10 的 SSE 事件印出。正式程式碼在 `Ymir.VibeMaker.Infrastructure/PiAgent`，這裡只是組裝與手動操作用的主控台程式。

## 前置

```bash
npm install -g @earendil-works/pi-coding-agent@1.0.0   # Local runtime 需要；Podman runtime 用 image 內的 pi
```

## 用法

```bash
# Local runtime + 內建 Fake LLM（不需要任何外部服務）
dotnet run --project spikes/pi-rpc-poc -- --prompt "[create-file] 建立檔案" --prompt "第二句"

# 互動模式：不帶 --prompt，逐行輸入；執行中按 Ctrl+C 會送出 abort
dotnet run --project spikes/pi-rpc-poc

# 經過 LiteLLM
dotnet run --project spikes/pi-rpc-poc -- --llm-url http://127.0.0.1:4000/v1 --model <model> --api-key <key> --prompt "hello"

# Rootless Podman runtime（需先建置 runtime/agent image）
dotnet run --project spikes/pi-rpc-poc -- --runtime Podman --network host --prompt "hello"
```

| 參數 | 預設 | 說明 |
|---|---|---|
| `--runtime` | `Local` | `Local`（host 直接執行，無隔離）或 `Podman` |
| `--llm-url` | 內建 Fake LLM | OpenAI 相容 base URL |
| `--model` / `--api-key` | `fake-model` / `$LITELLM_API_KEY` | |
| `--workspace` / `--session` | 固定 GUID | 相同 session 會續接對話 |
| `--workspace-root` | `$TMPDIR/ymir-poc-workspaces` | Host 上的 workspace 根目錄 |
| `--network` / `--image` | `slirp4netns` / `localhost/ymir/agent-runtime:dev` | Podman 專用 |

Fake LLM 的腳本指令（寫在 prompt 裡）：`[create-file]` 讓 Agent 用 bash 建立 `hello.txt`、`[slow]` 慢速串流（測 abort）、`[fail]` 回傳 HTTP 500。

## 驗證結果（2026-10-02，Pi 1.0.0、Podman 4.9.3、LiteLLM proxy）

| # | 驗證項目 | 方式 | 結果 |
|---|---|---|---|
| 1 | 文字串流 → `assistant.delta` | Local + Fake LLM | ✅ |
| 2 | Tool call（bash）在 `/workspace` 建檔 → `tool.started` / `tool.completed` | Local、Podman | ✅ 檔案出現在 host 的 workspace 目錄 |
| 3 | 相同 `--session-id` 續接上下文 | Local、Podman | ✅ 第二次執行模型收到 2 則使用者訊息 |
| 4 | **Container 刪除重建後**續接上下文 | Podman：`podman rm -f` 後再送訊息 | ✅ session 檔在 `/agent-state`，Agent 未失憶 |
| 5 | Abort → `execution.cancelled` | 整合測試（`[slow]` 中途取消） | ✅ Pi 回 `stopReason=aborted` + `agent_settled` |
| 6 | 模型端錯誤 → `execution.failed (MODEL_PROVIDER_ERROR)` | `[fail]`，含 Pi 自動重試 3 次 | ✅ 錯誤細節只寫 server log |
| 7 | Pi → **LiteLLM** → 模型（含 tool call） | LiteLLM proxy 轉發到 Fake LLM | ✅ |
| 8 | Container 安全設定 | `podman inspect` / `/proc/self/status` | ✅ uid 1000、CapEff=0、root fs 唯讀、no-new-privileges、只有兩個 bind mount |
| 9 | Workspace 隔離 | 第二個 workspace 的 container 看不到第一個的檔案 | ✅ |
| 10 | 掛載檔案擁有者 | `--userns keep-id` | ✅ host 上檔案屬於執行 podman 的使用者 |

自動化的部分（1、2、3、5、6 與錯誤處理）在 `tests/Ymir.IntegrationTests/PiAgent/PiAgentHarnessTests.cs`，CI 會安裝 Pi 執行。

## 驗證中的發現（已反映到程式與文件）

1. **`--session-id <uuid>`** 可以用指定的 id 建立或續接 session，直接對應 `AGENT_SESSION.id`，不需要另外保存 Pi 的 session 路徑。
2. **Pi 需要可寫入的 agent 目錄**（會寫 `auth.json`、`models-store.json`），所以 `PI_CODING_AGENT_DIR` 放在 `/agent-state/pi-agent`，不能做成唯讀掛載。
3. **`sleep infinity` 當 PID 1 不處理 SIGTERM**，`podman stop` 要等 10 秒才 SIGKILL → 已加 `--init`（實測 0.2 秒結束）。
4. **Rootless Podman 在 cgroups v1 會忽略資源限制**（`--memory`/`--cpus`/`--pids-limit`）→ 正式主機必須 cgroups v2 + delegation，見 [`runtime/agent/README.md`](../../runtime/agent/README.md)。
5. **LiteLLM virtual key 需要 PostgreSQL**（`/key/generate` 沒有 `DATABASE_URL` 會失敗）→ 部署需多一個 PostgreSQL，見 [ADR-0004](../../docs/adr/0004-litellm-virtual-keys.md)。
6. Pi 遇到模型端 5xx 會自動重試 3 次（2s/4s/8s），期間發出 `auto_retry_start` → 已映射成 `status` 事件，UI 可以顯示「正在重試」。

## 驗證環境的限制

雲端開發沙箱的網路政策擋掉 `deb.debian.org`、沒有 `/dev/net/tun`，所以 Podman 驗證時：
- image 以拿掉 `apt-get` 步驟的暫時版本建置（其餘步驟與 `runtime/agent/Containerfile` 相同）；
- container 使用 `--network host` 連到 127.0.0.1 上的 Fake LLM。

正式主機請以完整的 `Containerfile` 與 `slirp4netns`/`pasta` 網路重新跑一次上表的 Podman 項目。
