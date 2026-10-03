# Sprint 0 · 技術驗證 + 骨架

[← 回看板列表](README.md) ・ 規則與格式見 [README](README.md#留言規則)

---

## 📌 置頂：狀態總覽

> 最後更新：2026-10-03 14:52 ・ 狀態：**✅ 已完成**

**目標**：先驗證風險最高的「Pi + Rootless Podman + LiteLLM」能不能串起來，並建立可以 build / test 的專案骨架（[開發規劃](../planning/development-plan.md#修訂後的路線圖)）。

| 工作項目 | 狀態 | 連結 |
|---|---|---|
| SA 轉 Markdown、開發規劃、ADR 0001–0004、CLAUDE.md | ✅ | [docs/](../) ・ [CLAUDE.md](../../CLAUDE.md) |
| .NET 10 模組化單體骨架（Platform / VibeMaker） | ✅ | [src/](../../src/) |
| Pi RPC harness（串流、tool call、續接、abort、錯誤） | ✅ | [ADR-0003](../adr/0003-pi-rpc-via-podman-exec.md) |
| Podman runtime 與 Agent image | ✅ | [runtime/agent](../../runtime/agent/README.md) |
| 經過 LiteLLM 的完整鏈路 | ✅ | [PoC README](../../spikes/pi-rpc-poc/README.md) |
| Angular 22 骨架 + 開發用聊天頁 | ✅ | [web/](../../web/) |
| CI workflow + 雲端 session 啟動腳本 | ✅（CI 尚未在 GitHub 上跑過） | [ci.yml](../../.github/workflows/ci.yml) |
| 正式主機用完整 Containerfile 重跑 Podman 驗證 | ⏳ 移到 Sprint 1 | [#003](#003--驗證中的發現與環境限制) |

**待決定**：見 [#005](#005--需要決定的四件事)

---

## 💬 留言區

### #006 · 每次修改後自動檢查 commit / push / 進度紀錄

> 👤 **Claude（AI）** · 🕒 2026-10-03 14:52 · `📢公告`

依需求設定：**每次修改完都要 commit、push，並更新這個進度留言版**。

- 新增 Stop hook `.claude/hooks/require-commit-push-progress.sh`，AI 每次要結束回合時會檢查三件事，沒做完就擋下要求補完：
  1. 有未 commit 的變更
  2. 最後一次更新 `docs/progress/` 之後還有其他 commit（進度板沒跟上）
  3. 有未 push 的 commit
- `CLAUDE.md` 新增「每次修改後的流程」：驗證 → commit → 更新進度板 → push。
- hook 只檢查不代做，commit 訊息和留言仍由 AI 撰寫；push 失敗時只擋一次，第二次改為提醒，避免網路問題造成無限循環。

驗證：在暫存 repo 模擬五種狀態（乾淨、未 commit、進度板未跟上、未 push、未 push 且已擋過一次），結果都符合預期；`jq` 確認 `.claude/settings.json` 格式正確。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #005 · 需要決定的四件事

> 👤 **Claude（AI）** · 🕒 2026-10-03 00:04 · `❓待決定`

以下四件事會影響 Sprint 1～4 的做法，請在回覆區回覆（或直接在對話中告訴我）：

1. **套件下載方式**：Agent 要 `npm install` / `pip install`，但 SA §7 規定 container 只能連必要服務。
   建議：使用公司內部套件鏡像（Nexus / Artifactory）。替代方案：allow-list egress proxy。
2. **LiteLLM 的 PostgreSQL**：virtual key 功能必須有 PostgreSQL（[ADR-0004](../adr/0004-litellm-virtual-keys.md)）。
   建議：新增一個 LiteLLM 專用的 PostgreSQL。不允許的話，改由 API 轉發模型請求。
3. **企業 IdP 類型**：Entra ID（OIDC）/ ADFS / 純 LDAP？決定 Sprint 2 的登入實作。
4. **Podman 主機**：OS 與版本、能否設定 cgroups v2 + systemd delegation（否則資源限制無效，見 [#003](#003--驗證中的發現與環境限制)）。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #004 · Sprint 0 完成，已推送

> 👤 **Claude（AI）** · 🕒 2026-10-03 00:04 · `✅完成`

已推送到 `claude/inspiring-einstein-2pwxx6`，分成 5 個 commit（docs / .NET 骨架 / runtime + PoC / Angular / CI + hook），尚未開 PR。

| 驗證 | 結果 |
|---|---|
| `dotnet build Ymir.slnx -c Release`（警告視為錯誤） | ✅ |
| `dotnet test --solution Ymir.slnx`（.NET 41 個：單元 31、整合 10，其中 5 個啟動真實 Pi） | ✅ 全過；補上路徑測試前的 35 個連跑 3 次皆穩定 |
| `dotnet format --verify-no-changes` | ✅ |
| `npm run lint` / `npm test` / `npm run build`（Angular，4 個測試） | ✅ |
| Aspire 一鍵啟動（`dotnet run --project src/Ymir.AppHost`），經 Angular proxy 串流 | ✅ |
| 瀏覽器端對端：真實 Pi → Fake LLM → SSE → Angular | ✅ 截圖：[sprint0-dev-chat.png](../planning/sprint0-dev-chat.png) |
| GitHub Actions CI | ⏳ 尚未執行（只在 PR 或推到 main 時觸發） |

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #003 · 驗證中的發現與環境限制

> 👤 **Claude（AI）** · 🕒 2026-10-02 23:55 · `⚠️發現`

**會影響部署的發現**（已寫進 [runtime/agent/README](../../runtime/agent/README.md) 與 ADR）：

- **Rootless Podman 在 cgroups v1 會忽略 `--memory` / `--cpus` / `--pids-limit`**，只印一行警告。正式主機必須 cgroups v2 + systemd delegation。
- **`sleep infinity` 當 PID 1 不處理 SIGTERM**，`podman stop` 要等 10 秒 → 已加 `--init`，實測 0.2 秒結束。
- **LiteLLM virtual key 需要 PostgreSQL**，沒有 `DATABASE_URL` 時 `/key/generate` 直接失敗。
- Pi 的 agent 目錄必須可寫（會寫 `auth.json`、`models-store.json`），所以放在持久化的 `/agent-state/pi-agent`。

**驗證環境的限制**（雲端沙箱）：網路政策擋掉 `deb.debian.org`、沒有 `/dev/net/tun`。
所以 Podman 驗證時 image 拿掉了 `apt-get` 步驟、container 使用 `--network host`。
👉 正式主機需要用完整的 Containerfile 與 `slirp4netns` / `pasta` 重跑一次，步驟見 [runtime/agent/README](../../runtime/agent/README.md#手動驗證)。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #002 · Pi RPC + Podman + LiteLLM 實測通過

> 👤 **Claude（AI）** · 🕒 2026-10-02 23:36 · `🔬驗證`

SA §23 列為「待確認」的 Pi 整合方式已實測（Pi 1.0.0，套件改名為 `@earendil-works/pi-coding-agent`）：

- ✅ 文字串流、bash tool call 在 `/workspace` 建檔
- ✅ `--session-id <AGENT_SESSION.id>` 續接對話；**container 刪除重建後仍記得上下文**
- ✅ abort → `execution.cancelled`；模型 5xx（含 Pi 自動重試 3 次）→ `execution.failed`
- ✅ Pi → LiteLLM → 模型（含 tool call）
- ✅ Container 以 uid 1000 執行、無 capabilities、唯讀 root fs；不同 workspace 互相看不到檔案

真實的 Pi 輸出已錄成測試 fixture（`tests/Ymir.UnitTests/Fixtures/pi-rpc/`）。完整結果表：[PoC README](../../spikes/pi-rpc-poc/README.md)。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #001 · 專案盤點與 Sprint 0 開工

> 👤 **Claude（AI）** · 🕒 2026-10-02 23:18 · `📢公告`

盤點結果：repo 只有 SA 文件（docx）與架構圖，還沒有程式碼。

已確認的決定：

- **命名**：Ymir 是母平台、Vibe Maker 是子產品 → `Ymir.Platform.*` + `Ymir.VibeMaker.*`
- **這次範圍**：Sprint 0 技術驗證 + 骨架
- **MVP 調整**：加入唯讀 Workspace 檔案瀏覽 / 下載
- **開發方式**：全 AI 開發 → 文件、測試、CI 要當護欄

對 SA 的 13 點修訂建議（風險優先的 Sprint 順序、BFF + Cookie 讓 SSE 可用、SSE 斷線續傳、LiteLLM virtual key 等）與新路線圖：[開發規劃](../planning/development-plan.md)。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---
