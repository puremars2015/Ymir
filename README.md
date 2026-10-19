# Ymir

Ymir 是企業 AI 平台，目前的子產品 **Vibe Maker** 讓使用者透過對話，在個人隔離的 Agent 執行環境中建立工具、網站與文件。

## 主要功能

- **對話與專案**：串流回應、附件與圖片輸入、專案工作目錄及成果下載。
- **Agent 執行**：Pi Agent 執行工具，每位使用者擁有獨立容器與持久化檔案。
- **模型管理**：透過 LiteLLM 統一代理模型，支援模型選擇、依模型配置的思考深度、使用者權限與用量管理（含每人每模型用量，見 [ADR-0021](docs/adr/0021-per-user-model-usage.md)）。
- **企業管理**：Entra ID／本機帳號登入、管理員設定、執行政策與稽核紀錄。
- **文件建置**：`/help` 提供使用說明，`/make` 選擇管理員設定的工具、網站或文件建置主題。
- **可選整合**：平台 MCP、OneDrive、專案知識庫與獨立網域網站託管；需另外配置與啟用。

## 技術組成

| 層級 | 技術 |
|---|---|
| 前端 | Angular 22、TypeScript、REST／SSE |
| 後端 | .NET 10、ASP.NET Core、模組化單體、EF Core |
| Agent | Pi、Linux rootless Podman；Docker 用於開發／驗證 |
| 模型閘道 | LiteLLM、每位使用者的 virtual key |
| 資料儲存 | SQL Server；LiteLLM 專用 PostgreSQL |
| 正式入口 | Cloudflare Tunnel → nginx → Production API |

完整部署拓樸、執行流程與安全邊界見 **[系統架構](architecture.md)**。

## 專案目錄

```text
src/             API、平台核心、Vibe Maker 與獨立服務
web/             Angular 前端與端對端測試
runtime/agent/   Agent 容器映像、工具與文件模板
deploy/          各服務部署範本與操作指南
tests/           單元／整合測試與測試替身
docs/            需求、架構決策、使用指南與進度
spikes/          技術驗證
```

## 本機開發

準備 .NET 10 SDK、Node.js 24 LTS（至少 24.15）與可用的容器引擎，再於專案根目錄執行：

```bash
dotnet tool restore
npm --prefix web ci
dotnet run --project src/Ymir.AppHost
```

使用 Podman 時，啟動 AppHost 前設定 `ASPIRE_CONTAINER_RUNTIME=podman`。AppHost 編排 SQL Server、Fake LLM、API 與 Angular；依啟動輸出取得本機網址。開發預設使用 Scripted harness，不呼叫真實模型。

Pi 整合、測試指令與環境設定詳見 [開發規則](CLAUDE.md)。本機開發流程不等同正式部署；正式環境請依部署指南配置認證、隔離、機密與資料持久化。

## 文件導覽

**從乾淨 Ubuntu 主機安裝正式環境，請先讀 [Ubuntu 安裝總指南](docs/guides/ubuntu-install.md)。** 內含安裝順序、持久化資料、帳號／網路隔離、登入／Tunnel、驗收、更新與回復；不是把既有主機修復腳本當成全新安裝器。

| 主題 | 文件 |
|---|---|
| 系統全貌與專案分層 | [architecture.md](architecture.md) |
| Ubuntu 從零安裝／驗收／更新與回復 | [安裝總指南](docs/guides/ubuntu-install.md) |
| Windows 11 Professional 測試機從零安裝 | [測試機安裝總指南](docs/guides/windows-11-test-install.md) |
| 每個 container 所需 script／部署順序 | [部署操作索引](docs/guides/container-deployment.md) · [部署流水帳](docs/deployments/README.md) |
| 需求與 API／事件契約 | [系統分析](docs/sa/vibe-maker-core-mvp-sa.md) |
| 架構決策 | [ADR 索引](docs/adr/README.md) |
| 開發與驗證規則 | [CLAUDE.md](CLAUDE.md) |
| API 與 Agent 主機部署 | [API](deploy/api/README.md) · [RuntimeHost](deploy/runtime-host/README.md) |
| 模型代理與訂閱登入 | [LiteLLM](deploy/litellm/README.md) · [ChatGPT OAuth](deploy/litellm/CHATGPT-OAUTH.md) |
| 可選服務 | [MCP](deploy/mcp/README.md) · [SiteHost](deploy/sites/README.md) |
| 文件建置模板 | [公告 Word](runtime/agent/templates/announcement/README.md) · [簡報](runtime/agent/templates/presentation/README.md) |
| 備份與還原 | [維運指南](docs/guides/backup-restore.md) |
| 開發進度與驗證紀錄 | [Sprint 看板](docs/progress/README.md) |

部署快照、故障處理、實機驗證與待完成事項記錄於各服務指南及進度看板，不在 README 維護。
