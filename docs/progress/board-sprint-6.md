# Sprint 6 · Agent 擴充能力、RAG 知識庫、前端網站託管

[← 回看板列表](README.md) ・ 規則與格式見 [README](README.md#留言規則)

---

## 📌 置頂：狀態總覽

> 最後更新：2026-10-07 23:00 ・ 狀態：**🚧 進行中（A1 擴充政策）**

**目標**：
- 讓 Agent 的能力可以在管理員的管制下擴充：使用者可自建 skill / MCP，開發人員則維護平台 MCP（[ADR-0012](../adr/0012-agent-extensions-and-platform-mcp.md)）；
- 把 [RAG 知識庫](../planning/rag-knowledge-base-plan.md) 與 [前端網站託管](../planning/frontend-site-hosting-plan.md) 從計畫推進到 ADR，再進入第一階段實作。

**Done Definition**：
1. 管理員可設定全域與每人的擴充能力（`skills`、`mcp`），伺服器在啟動 Agent 時強制執行，並寫稽核。
2. Agent 只能經獨立的 MCP Gateway、以每人專屬的短期 token 呼叫平台服務；以 echo 服務驗證授權、稽核與 rate limit。
3. RAG 與網站託管的 ADR 經使用者確認；確認後完成各自的第一個實作階段（RAG 最小索引、公開網站發布）。

**使用者已決定（2026-10-07）**：
- 範圍：ADR-0012、RAG、網站託管。剩餘強化項目（rate limit、CI 掃描、壓測）不在本 Sprint。
- ADR-0012 的四項決定：

  | # | 項目 | 決定 |
  |---|---|---|
  | 1 | 權限粒度 | 全域預設 + 每人覆寫 |
  | 2 | 第一階段能力 | `skills` 與 `mcp` 都做 |
  | 3 | Gateway | 獨立服務 |
  | 4 | Egress | 先做 spike 再決定 |

| # | 工作項目 | 狀態 | 前置 / 待決定 |
|---|---|---|---|
| A0 | ADR-0012 spike：實測 Pi 1.0.0 在 RPC 模式的 `--no-skills` / `--skill`、能否不讀使用者層 `mcp.json`、平台 MCP 設定能否放在 Agent 不可寫的位置，以及 rootless Podman 能否限制 egress；結果寫回 ADR-0012 | ✅ | — |
| A1 | 擴充政策（ADR-0012 第一階段）：<br>• `vibemaker.extension_policy` + 每人覆寫資料表<br>• `IExtensionPolicy`<br>• `PiAgentHarness` 依政策組合參數<br>• `ymir-extension-builder` skill<br>• 管理介面、`GET /api/extensions`、稽核、授權矩陣<br>• 一律 `-ne`、每次執行重寫 `settings.json` / `trust.json` / `mcp.json`（A0 結果） | 🚧 | — |
| A2 | MCP Gateway（ADR-0012 第二階段）：<br>• 獨立專案 `Ymir.McpGateway`<br>• 每人短期 token、`deploy/mcp/servers.json` 服務目錄、存取清單<br>• echo 服務、稽核與 rate limit、部署文件 | ⏳ | A1；**❓待確認**：正式主機是否採用 `--internal` network（ADR-0012「Egress 建議」） |
| R0 | RAG ADR（ADR-0013）：服務與 volume 邊界、Embedding 抽象、向量儲存介面、SQLite（sqlite-vec）部署、權限 | ⏳ | **❓待決定**：Embedding 模型與硬體、文件格式與容量、外部回答模型的資料政策 |
| R1 | RAG 最小索引：知識庫、文件儲存、背景索引、Fake Embedding、每專案一份 SQLite | ⏳ | R0 經使用者確認 |
| R2 | RAG 問答：檢索、回答、引用、資料不足提示、UI | ⏳ | R1 |
| H0 | 網站託管 ADR（ADR-0014）：獨立網站網域、每站來源隔離、私人網站登入、經主機複製產物、容量限制 | ⏳ | **❓待決定**：網域與 DNS / Tunnel、公司模式的帳號範圍、配額 |
| H1 | 公開網站發布：網站與版本、產物檢查、Nginx 託管、原子切換、取消發布 | ⏳ | H0 經使用者確認 |
| H2 | 身分與分享：公司模式、指定使用者、分享給我的網站 | ⏳ | H1 |

**從 Sprint 2 移交、待使用者環境確認**（見 [Sprint 2 看板 #037](board-sprint-2.md)）：
- 真 Entra 登入；
- 真 LiteLLM + MiniMax 與單價；
- Linux rootless Podman + runtime host + Quadlet；
- Cloudflare Tunnel；
- Windows Docker Desktop；
- OTLP 指標匯出；
- 驗收計畫的手動項目（Codex 執行，見 [sprint5-acceptance-plan.md](../planning/sprint5-acceptance-plan.md)）。

**沙箱限制**（無法在沙箱驗證，會標「未驗證、待使用者環境確認」）：
- 真實 Embedding 模型；
- 網域、DNS、Tunnel；
- EIP / MES 等內網服務；
- 正式主機的 rootless Podman egress。

---

## 💬 留言區

### #001 · Sprint 6 看板開張

> 👤 **Claude（AI）** · 🕒 2026-10-07 21:30 · `📢公告`

- **背景**：使用者更新了規劃文件與 ADR（ADR-0012 草案、Codex 的 RAG 與網站託管計畫），要求建立新的 Sprint。
  - Sprint 2 看板實際上涵蓋了路線圖的 Sprint 2～5，已經結束（見 [Sprint 2 #037](board-sprint-2.md)）。
  - 新看板接著路線圖編號為 Sprint 6。
- **範圍與決定**：見置頂區。ADR-0012 已依使用者決定改為「已採納（egress 待 spike 結果）」。
- **順序**：
  1. A0 → A1 → A2 依序實作；
  2. R0 與 H0 只寫 ADR，可以在 A 系列進行時先寫好，交給使用者決定；
  3. R1 / R2、H1 / H2 等使用者回覆後才開始。
- **流程**：每一項都依 CLAUDE.md 流程獨立走完：驗證 → commit → 看板留言 → PR → CI → merge。
- **下一步**：A0 spike，用沙箱的 Fake LLM + 真實 Pi 1.0.0 實測 skill / MCP 的載入與關閉，結果寫回 ADR-0012。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---

### #002 · A0 spike 完成：Pi 的 skill / MCP / extension 載入行為與 egress

> 👤 **Claude（AI）** · 🕒 2026-10-07 23:00 · `🔬實測` `📢公告`

- **做了什麼**：用沙箱的真實 Pi 1.0.0（RPC 模式，參數與環境變數同 `PiAgentHarness`）搭配記錄請求內容的假模型實測。依模型收到的 system prompt 與 `tools` 判斷，結果寫進 [ADR-0012「Spike 結果」](../adr/0012-agent-extensions-and-platform-mcp.md)。
- **結論**：
  - `-ns` 關掉使用者層與專案層 skill；`--skill <路徑>` 不受影響，可用來載入平台 skill。
  - `-ne` 關掉 MCP 與所有 extension；`-ne -e builtin:mcp` 只恢復 MCP。
  - MCP 設定只讀 `agentDir/mcp.json` 與受信任專案的 `.pi/mcp.json`，**沒有唯讀層**；只在 session 啟動時讀取。
    - 因此改為 Ymir 每次執行前重新產生 `mcp.json`（平台項目優先），強制點在 gateway 的 token 與存取清單。
- **發現的風險（現況）**：
  - 目前的參數下，Agent 寫進 `/agent-state/pi-agent/` 的 skill、`mcp.json`、**extension（TypeScript 程式碼）**都會在下一次執行被載入。
  - Agent 可以改寫 `trust.json` 或 `settings.json` 讓 workspace 的 `.pi/` 生效。
  - 影響範圍仍限於該使用者自己的 container，沒有跨使用者或拿到平台憑證的問題。A1 會一律加 `-ne`，並在每次執行前重寫這些檔。
- **Egress**（沙箱是 root Podman）：
  - `--network none` 全部不通；
  - 現行的 `slirp4netns` 可連 host 的對外 IP 與網際網路；
  - `--internal` network 只通同網路的 container 與閘道 IP。
  - `VibeMaker__Runtime__Network` 已可設定，不必改 `ContainerCommandBuilder`。
- **驗證**：只改文件；`git diff --check`；spike 腳本留在 scratchpad，不進版控。
- **未驗證、待使用者環境確認**：rootless Podman 下 `--internal` network 與 LiteLLM / gateway container 共用網路的行為。
- **❓待確認**：
  - Egress 建議：第一階段不強制；
  - 正式主機提供 `--internal` network 範本，由管理員選擇啟用；
  - 這會讓使用者自建、需要連外的 MCP 失效。
  - 請使用者確認是否同意。A2 開工前需要答案，A1 不受影響。
- **下一步**：A1 擴充政策實作。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---
