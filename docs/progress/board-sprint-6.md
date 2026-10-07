# Sprint 6 · Agent 擴充能力、RAG 知識庫、前端網站託管

[← 回看板列表](README.md) ・ 規則與格式見 [README](README.md#留言規則)

---

## 📌 置頂：狀態總覽

> 最後更新：2026-10-07 21:30 ・ 狀態：**🚧 進行中（A0 spike）**

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
| A0 | ADR-0012 spike：實測 Pi 1.0.0 在 RPC 模式的 `--no-skills` / `--skill`、能否不讀使用者層 `mcp.json`、平台 MCP 設定能否放在 Agent 不可寫的位置，以及 rootless Podman 能否限制 egress；結果寫回 ADR-0012 | 🚧 | — |
| A1 | 擴充政策（ADR-0012 第一階段）：<br>• `vibemaker.extension_policy` + 每人覆寫資料表<br>• `IExtensionPolicy`<br>• `PiAgentHarness` 依政策組合參數<br>• `ymir-extension-builder` skill<br>• 管理介面、`GET /api/extensions`、稽核、授權矩陣 | ⏳ | A0 |
| A2 | MCP Gateway（ADR-0012 第二階段）：<br>• 獨立專案 `Ymir.McpGateway`<br>• 每人短期 token、`deploy/mcp/servers.json` 服務目錄、存取清單<br>• echo 服務、稽核與 rate limit、部署文件 | ⏳ | A1；egress 依 A0 結果 |
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
