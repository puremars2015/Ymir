# CLAUDE.md

Ymir 是企業內部 AI 平台；第一個子產品 **Vibe Maker**：企業帳號登入 → Chat → Agent（Pi）在每個 Workspace 專屬的 Rootless Podman container 執行 → 經 LiteLLM 呼叫模型 → SSE 即時串流回 Angular。
本專案**全由 AI 開發**，請嚴格遵守下列規則；做任何架構相關的改動前，先讀相關 ADR。

## 必讀文件

| 文件 | 內容 |
|---|---|
| `docs/sa/vibe-maker-core-mvp-sa.md` | SA（需求、資料模型、API、SSE 契約、驗收條件），以章節編號引用，例如「SA §10」 |
| `docs/planning/development-plan.md` | 對 SA 的修訂建議、路線圖、待確認事項 |
| `docs/adr/` | 已定案的架構決策；**不得在沒有新 ADR 的情況下推翻** |
| `spikes/pi-rpc-poc/README.md` | Pi / Podman / LiteLLM 的實測結果與發現 |
| `docs/progress/` | **開發進度留言版**：開工前先讀目前 Sprint 的看板，收工前依規則留言回報 |

## 專案結構

```
src/
  Ymir.Api/                          ASP.NET Core Host（composition root）
  Ymir.AppHost/                      .NET Aspire 本機開發編排
  Ymir.ServiceDefaults/              OpenTelemetry、health check
  Platform/Ymir.Platform[.Infrastructure]/         共用核心：身份、使用者、稽核
  Modules/VibeMaker/Ymir.VibeMaker/                Domain/ + Application/（介面：IAgentHarness、IAgentRuntimeManager、IModelGateway）
  Modules/VibeMaker/Ymir.VibeMaker.Infrastructure/ PiAgent/、Podman/、Runtime/（Local，開發用）、Dev/（Scripted harness）
  Modules/VibeMaker/Ymir.VibeMaker.Contracts/      API DTO、SSE 事件契約
tests/
  Ymir.UnitTests/                    含 Fixtures/pi-rpc/：Pi 1.0.0 的真實 RPC 錄製
  Ymir.IntegrationTests/             WebApplicationFactory + 真實 Pi 程序（PATH 上沒有 pi 會自動略過）
  Ymir.Testing.FakeLlm/              OpenAI 相容假模型（[create-file] / [slow] / [fail] 腳本）
web/                                 Angular 22（standalone、signals、zoneless、Vitest、ESLint）
runtime/agent/                       Agent runtime Containerfile
spikes/pi-rpc-poc/                   技術驗證主控台程式
```

## 指令

```bash
# 後端（.NET 10 SDK）
dotnet build Ymir.slnx                    # 警告視為錯誤
dotnet test --solution Ymir.slnx          # 單元 + 整合測試（Microsoft Testing Platform）
dotnet test --project tests/Ymir.UnitTests

# Pi 整合測試需要（CI 會安裝）
npm install -g @earendil-works/pi-coding-agent@1.0.0

# 前端（Node 24 LTS；Angular 22 需要 Node >= 22.22.3 或 >= 24.15）
cd web && npm ci && npm run lint && npm test -- --watch=false && npm run build

# 本機一鍵啟動（Fake LLM + API + Angular，不需要 container runtime）
dotnet run --project src/Ymir.AppHost

# 個別啟動
dotnet run --project tests/Ymir.Testing.FakeLlm      # http://127.0.0.1:5199/v1
dotnet run --project src/Ymir.Api                    # http://localhost:5080（Development：Scripted harness + Local runtime）
cd web && npm start                                  # http://localhost:4200，/api 轉給 5080
```

Development 環境預設 `VibeMaker:Harness=Scripted`（假 Agent）。要接真正的 Pi：設定 `VibeMaker__Harness=Pi` 並啟動 Fake LLM。

## 架構規則

- **分層**：`Api → *.Infrastructure → 模組核心（Domain + Application）→ Contracts`。Application 層不得直接呼叫 podman、不得解析 Pi 協定（SA §19）。模組之間不得互相參考。
- **Runtime 與 Harness 分離**：`IAgentRuntimeManager` 只負責「在隔離環境啟動程序並提供 stdio」；Pi 協定全部在 `PiAgentHarness` / `PiRpcEventMapper`（ADR-0003）。
- **Harness 的事件保證**：每次 run 最後一個事件必定是 `AgentCompleted` / `AgentFailed` / `AgentCancelled`；取消時不拋例外。
- **SSE 契約**以 `Ymir.VibeMaker.Contracts.Executions` 與 `web/src/app/core/executions/execution-events.ts` 為準，兩邊必須同步修改。
- **認證**採 BFF + HttpOnly Cookie（ADR-0002）：前端不得保存 token，SSE 使用原生 `EventSource`。
- **Pi 版本鎖定**在 `runtime/agent/Containerfile` 與 CI；升級時必須重新錄製 `tests/Ymir.UnitTests/Fixtures/pi-rpc/` 並重跑整合測試。

## 安全紅線（違反即退回）

- 所有 Workspace / Conversation / Message / Execution API 必須在 server 端驗證擁有者；不得信任前端傳入的 user id（SA §12）。新 API 必須加入授權矩陣測試。
- Host 路徑只能由 workspace id 推導（`WorkspaceDirectories`）；API 不接受任何外部傳入的路徑。讀取 workspace 檔案時必須解析 realpath 並拒絕 `..` 與 symlink 逃逸。
- Container 不得 `--privileged`、不得掛載 podman socket 或 host 敏感路徑；改動 `PodmanCommandBuilder` 時同步更新 `PodmanCommandBuilderTests`。
- Container 內不得出現 LiteLLM master key、DB 連線字串或 AD 憑證（ADR-0004）。環境變數以 `podman exec --env NAME` 傳遞，值不得出現在程序參數。
- 回給瀏覽器的錯誤與 tool 事件只能是摘要：不得含 stack trace、host path、token、完整 command output（SA §10、§12）。原始細節只寫 server log。
- `LocalRuntimeManager` 沒有隔離，只允許 Development 環境（DI 會在其他環境拒絕啟動）。

## 程式風格

- C#：file-scoped namespace、`sealed` 預設、record 表示不可變資料、`ConfigureAwait(false)`（library 專案）、註解用繁體中文並說明「為什麼」，引用 SA 章節或 ADR 編號。
- 分析器等級 `latest-recommended` 且警告視為錯誤；確實需要壓制時以 `#pragma warning disable` 加上理由。
- 測試：xUnit v3（`TestContext.Current.CancellationToken`）；外部依賴用 Fake LLM / Scripted harness，不要在測試中呼叫真實模型。
- Angular：standalone component、signals、`ChangeDetectionStrategy.OnPush`、新的控制流程語法（`@if` / `@for`）；狀態轉換寫成純函式並加上 Vitest 測試。
- Commit 一次一個垂直切片；改動前後都要跑 build、test、lint。
- 每次工作結束前，在 `docs/progress/` 目前 Sprint 的看板留言（做了什麼、實際跑過的驗證、卡關與待決定事項），並更新置頂區的工作項目狀態。
