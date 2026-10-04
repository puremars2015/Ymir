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
  Ymir.Api/                          ASP.NET Core Host：Endpoints/、Auth/（Cookie + XSRF）、openapi/v1.json（API 契約快照）
  Ymir.AppHost/                      .NET Aspire 本機開發編排
  Ymir.ServiceDefaults/              OpenTelemetry、health check
  Platform/Ymir.Platform[.Infrastructure]/         共用核心：使用者、身份（ICurrentUser）、稽核；schema platform
  Modules/VibeMaker/Ymir.VibeMaker/                Domain/（實體、狀態機）+ Application/（Workspaces、Conversations、Executions 用例；IAgentHarness、IAgentRuntimeManager、IModelGateway）
  Modules/VibeMaker/Ymir.VibeMaker.Infrastructure/ Persistence/（EF Core，schema vibemaker）、Executions/（背景 worker、事件 bus）、PiAgent/、Containers/（Podman / Docker）、Runtime/（Local，開發用）、Dev/（Scripted harness）
  Modules/VibeMaker/Ymir.VibeMaker.Contracts/      API DTO、SSE 事件契約
tests/
  Ymir.UnitTests/                    含 Fixtures/pi-rpc/：Pi 1.0.0 的真實 RPC 錄製
  Ymir.IntegrationTests/             WebApplicationFactory + SQL Server（每個 fixture 獨立資料庫）+ 真實 Pi；含授權矩陣、OpenAPI 快照
  Ymir.Testing.FakeLlm/              OpenAI 相容假模型（[create-file] / [slow] / [fail] 腳本）
web/                                 Angular 22（standalone、signals、zoneless、Vitest、ESLint）；src/app/core/api/schema.ts 由 OpenAPI 產生；e2e/ Playwright 腳本
runtime/agent/                       Agent runtime Containerfile
spikes/pi-rpc-poc/                   技術驗證主控台程式
```

## 指令

```bash
# 後端（.NET 10 SDK）
dotnet build Ymir.slnx                    # 警告視為錯誤
dotnet test --solution Ymir.slnx          # 單元 + 整合測試（Microsoft Testing Platform）
dotnet test --project tests/Ymir.UnitTests

# 整合測試與本機 API 需要 SQL Server（雲端 session 的啟動 hook 會自動啟動）
podman run -d --name ymir-sql --network host -e ACCEPT_EULA=Y \
  -e 'MSSQL_SA_PASSWORD=Ymir_Dev_Passw0rd!' mcr.microsoft.com/mssql/server:2022-latest
# 測試預設連 localhost,1433；其他位址設定 YMIR_TEST_SQLSERVER（不含 Database，每個 fixture 自建自刪資料庫）

# 新增 migration（每個模組各自一組，schema：platform / vibemaker）
dotnet tool restore
dotnet ef migrations add <Name> --project src/Modules/VibeMaker/Ymir.VibeMaker.Infrastructure --context VibeMakerDbContext --output-dir Persistence/Migrations

# Pi 整合測試需要（CI 會安裝）
npm install -g @earendil-works/pi-coding-agent@1.0.0

# 前端（Node 24 LTS；Angular 22 需要 Node >= 22.22.3 或 >= 24.15）
cd web && npm ci && npm run lint && npm test -- --watch=false && npm run build

# API 契約有變更時（整合測試 OpenApiSnapshotTests 會失敗提醒）
YMIR_UPDATE_OPENAPI=1 dotnet test --project tests/Ymir.IntegrationTests -- --filter-method "*OpenApiDocument_MatchesCommittedSnapshot*"
cd web && npm run api:generate

# 端對端驗證（需先啟動 SQL Server、Fake LLM、API（VibeMaker__Harness=Pi）、npm start）
cd web && CHROMIUM_PATH=/opt/pw-browsers/chromium npm run e2e:sprint1 -- <截圖目錄>

# 本機一鍵啟動（SQL Server container + Fake LLM + API + Angular；podman 請設定 ASPIRE_CONTAINER_RUNTIME=podman）
dotnet run --project src/Ymir.AppHost

# 個別啟動
dotnet run --project tests/Ymir.Testing.FakeLlm      # http://127.0.0.1:5199/v1
dotnet run --project src/Ymir.Api                    # http://localhost:5080（Development：自動 migrate、Scripted harness、Local runtime）
cd web && npm start                                  # http://localhost:4200，/api 轉給 5080
```

Development 環境預設 `VibeMaker:Harness=Scripted`（假 Agent）。要接真正的 Pi：設定 `VibeMaker__Harness=Pi` 並啟動 Fake LLM。

Runtime：`VibeMaker:Runtime:Provider` = `Podman`（正式）| `Docker`（只用於開發 / 驗證，例如 Windows，ADR-0005；指南 `docs/guides/windows-docker.md`）| `Local`（Linux / macOS 開發用，無隔離）。

## 架構規則

- **分層**：`Api → *.Infrastructure → 模組核心（Domain + Application）→ Contracts`。Application 層不得直接呼叫 podman、不得解析 Pi 協定（SA §19）。模組之間不得互相參考。
- **Runtime 與 Harness 分離**：`IAgentRuntimeManager` 只負責「在隔離環境啟動程序並提供 stdio」；Pi 協定全部在 `PiAgentHarness` / `PiRpcEventMapper`（ADR-0003）。
- **Harness 的事件保證**：每次 run 最後一個事件必定是 `AgentCompleted` / `AgentFailed` / `AgentCancelled`；取消時不拋例外。
- **SSE 契約**以 `Ymir.VibeMaker.Contracts.Executions` 與 `web/src/app/core/executions/execution-events.ts` 為準，兩邊必須同步修改。其他 API 型別一律由 OpenAPI 產生，不要手寫 DTO。
- **Execution**：與 HTTP request 解耦，由 `ExecutionWorker` 背景執行；事件**先寫 `execution_events` 再發佈**；任何結束路徑（完成、失敗、取消、逾時、例外）都必須寫終止事件並推進狀態（`ExecutionRunner`）。
- **授權預設拒絕**：fallback policy 要求登入；匿名端點必須明確 `AllowAnonymous`。狀態變更端點加 `RequireAntiforgeryHeader()`。新 `/api` 端點必須加入 `AuthorizationMatrixTests`，否則測試失敗。
- **認證**採 BFF + HttpOnly Cookie（ADR-0002）：前端不得保存 token，SSE 使用原生 `EventSource`。
- **資料存取**：Application 層透過 `IVibeMakerDbContext`（EF Core DbSet）存取；跨模組只存 id、不建 FK（ADR-0001）。並行規則（同 Conversation 單一執行中、冪等鍵）由 filtered unique index 保證，不要改成先查再寫。
- **Pi 版本鎖定**在 `runtime/agent/Containerfile` 與 CI；升級時必須重新錄製 `tests/Ymir.UnitTests/Fixtures/pi-rpc/` 並重跑整合測試。

## 安全紅線（違反即退回）

- 所有 Workspace / Conversation / Message / Execution API 必須在 server 端驗證擁有者；不得信任前端傳入的 user id（SA §12）。新 API 必須加入授權矩陣測試。
- Host 路徑只能由 workspace id 推導（`WorkspaceDirectories`）；API 不接受任何外部傳入的路徑。讀取 workspace 檔案時必須解析 realpath 並拒絕 `..` 與 symlink 逃逸。
- Container 不得 `--privileged`、不得掛載 container runtime socket 或 host 敏感路徑；改動 `ContainerCommandBuilder` 時同步更新 `ContainerCommandBuilderTests`（Podman 與 Docker 兩種 engine 都要符合）。
- Container 內不得出現 LiteLLM master key、DB 連線字串或 AD 憑證（ADR-0004）。環境變數以 `podman exec --env NAME` 傳遞，值不得出現在程序參數。
- 回給瀏覽器的錯誤與 tool 事件只能是摘要：不得含 stack trace、host path、token、完整 command output（SA §10、§12）。原始細節只寫 server log。
- `LocalRuntimeManager` 沒有隔離，只允許 Development 環境（DI 會在其他環境拒絕啟動）。

## 程式風格

- C#：file-scoped namespace、`sealed` 預設、record 表示不可變資料、`ConfigureAwait(false)`（library 專案）、註解用繁體中文並說明「為什麼」，引用 SA 章節或 ADR 編號。
- 分析器等級 `latest-recommended` 且警告視為錯誤；確實需要壓制時以 `#pragma warning disable` 加上理由。
- 測試：xUnit v3（`TestContext.Current.CancellationToken`）；外部依賴用 Fake LLM / Scripted harness，不要在測試中呼叫真實模型。
- Angular：standalone component、signals、`ChangeDetectionStrategy.OnPush`、新的控制流程語法（`@if` / `@for`）；狀態轉換寫成純函式並加上 Vitest 測試。

## 每次修改後的流程（必做）

任何檔案修改完成後，結束回合前依序完成：

1. **驗證**：跑相關的 build、test、lint，確認通過。
2. **Commit**：一次一個垂直切片，訊息說明做了什麼與為什麼，結尾附 attribution trailer。
3. **更新進度紀錄**：在 `docs/progress/` 目前 Sprint 的看板留言（做了什麼、實際跑過的驗證、卡關與待決定事項），更新置頂區的工作項目狀態，然後 commit。
4. **Push**：`git push -u origin <目前分支>`；網路失敗依 2 / 4 / 8 / 16 秒重試。不要推到其他分支。

`.claude/hooks/require-commit-push-progress.sh`（Stop hook）會在回合結束時檢查：有未 commit 的變更、最後一次更新 `docs/progress/` 之後還有其他 commit、或有未 push 的 commit，都會擋下要求補完。
