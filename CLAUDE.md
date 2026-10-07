# CLAUDE.md

Ymir 是企業內部 AI 平台；第一個子產品 **Vibe Maker**：企業帳號（Entra ID OIDC）或本機帳號密碼登入（ADR-0009）→ Chat → Agent（Pi）在每個使用者專屬的 Rootless Podman container（一人一個，ADR-0007）執行（正式部署時 API 在容器內，container 由主機上的 runtime host 管理，ADR-0008）；專案是 container 內的檔案群組 → 經 LiteLLM 呼叫模型 → SSE 即時串流回 Angular。
本專案**全由 AI 開發**，請嚴格遵守下列規則；做任何架構相關的改動前，先讀相關 ADR。

## 必讀文件

| 文件 | 內容 |
|---|---|
| `docs/sa/vibe-maker-core-mvp-sa.md` | SA（需求、資料模型、API、SSE 契約、驗收條件），以章節編號引用，例如「SA §10」 |
| `docs/planning/development-plan.md` | 對 SA 的修訂建議、路線圖、待確認事項 |
| `docs/adr/` | 已定案的架構決策；**不得在沒有新 ADR 的情況下推翻**（目前到 ADR-0011） |
| `spikes/pi-rpc-poc/README.md` | Pi / Podman / LiteLLM 的實測結果與發現 |
| `docs/progress/` | **開發進度留言版**：開工前先讀目前 Sprint 的看板，收工前依規則留言回報 |

## 專案結構

```
src/
  Ymir.Api/                          ASP.NET Core Host：Endpoints/（含 Admin）、Auth/（Cookie + XSRF、Entra OIDC、本機帳號、每請求狀態檢查）、openapi/v1.json（API 契約快照）
  Ymir.AppHost/                      .NET Aspire 本機開發編排
  Ymir.ServiceDefaults/              OpenTelemetry、health check
  Ymir.Edge/                         對外入口（Cloudflare Tunnel，ADR-0006）：可信任 proxy 的 X-Forwarded-*、Host 限制、HSTS、提供 Angular build
  Ymir.Api/Containerfile             API image（含 Angular build；Provider=Remote，ADR-0008）
  Ymir.RuntimeHost/                  主機服務（ADR-0008）：Unix socket + token，以 user id 管理 Agent container、WebSocket 轉送 stdio
  Platform/Ymir.Platform[.Infrastructure]/         共用核心：使用者、本機帳號密碼（LocalCredential）、身份（ICurrentUser）、稽核；schema platform
  Modules/VibeMaker/Ymir.VibeMaker/                Domain/（實體、狀態機）+ Application/（Projects、Conversations、Executions 用例；IAgentHarness、IAgentRuntimeManager、IModelGateway）
  Modules/VibeMaker/Ymir.VibeMaker.Infrastructure/ Persistence/（EF Core，schema vibemaker）、Executions/（背景 worker、事件 bus）、PiAgent/、Containers/（Podman / Docker）、Runtime/（Local，開發用；Remote/：呼叫 runtime host）、Dev/（Scripted harness）
  Modules/VibeMaker/Ymir.VibeMaker.Contracts/      API DTO、SSE 事件契約
tests/
  Ymir.UnitTests/                    含 Fixtures/pi-rpc/：Pi 1.0.0 的真實 RPC 錄製
  Ymir.IntegrationTests/             WebApplicationFactory + SQL Server（每個 fixture 獨立資料庫）+ 真實 Pi；含授權矩陣、OpenAPI 快照
  Ymir.Testing.FakeLlm/              OpenAI 相容假模型（[create-file] / [slow] / [fail] 腳本）
  Ymir.Testing.FakeOidc/             模擬 Entra ID 的 OIDC 伺服器（tid / oid / pairwise sub / roles、PKCE），測試與本機開發用
web/                                 Angular 22（standalone、signals、zoneless、Vitest、ESLint）；src/app/core/api/schema.ts 由 OpenAPI 產生；e2e/ Playwright 腳本
runtime/agent/                       Agent runtime Containerfile
deploy/cloudflared/                  cloudflared ingress 設定範本（指南 docs/guides/cloudflare-tunnel.md）
deploy/litellm/                      LiteLLM proxy sample（MiniMax 國際站 + Fake LLM）；金鑰只放在 .env（已被 gitignore）
deploy/api/                          API container：Linux 用 rootful Podman + Quadlet（ymir-api.container），Windows 用 Docker Desktop（compose.windows.yml）；唯讀、只掛 socket 目錄與 Data Protection 金鑰
deploy/runtime-host/                 runtime host 的 systemd unit、設定範本與安裝指南
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
cd web && CHROMIUM_PATH=/opt/pw-browsers/chromium npm run e2e -- <截圖目錄>
# 登入流程（另需 Fake OIDC，API 設定 Ymir__Auth__Oidc__* 指向它，見 docs/guides/entra-id.md）
dotnet run --project tests/Ymir.Testing.FakeOidc      # http://127.0.0.1:5299，client ymir-dev / ymir-dev-secret
cd web && CHROMIUM_PATH=/opt/pw-browsers/chromium npm run e2e:auth -- <截圖目錄>
# /make 指令與 Make 主題管理（同 e2e 的前置；以 Dev 登入 Admin）
cd web && CHROMIUM_PATH=/opt/pw-browsers/chromium npm run e2e:make -- <截圖目錄>
# 管理介面：總覽、停止執行環境、稽核紀錄、系統設定（同 e2e 的前置）
# 另需 Fake OIDC，API 加上 Ymir__Auth__Oidc__AuthorityHost=http://127.0.0.1:5299（不設定 Authority）
cd web && CHROMIUM_PATH=/opt/pw-browsers/chromium npm run e2e:admin -- <截圖目錄>

# 本機一鍵啟動（SQL Server container + Fake LLM + API + Angular；podman 請設定 ASPIRE_CONTAINER_RUNTIME=podman）
dotnet run --project src/Ymir.AppHost

# 個別啟動
dotnet run --project tests/Ymir.Testing.FakeLlm      # http://127.0.0.1:5199/v1
dotnet run --project src/Ymir.Api                    # http://localhost:5080（Development：自動 migrate、Scripted harness、Local runtime）
cd web && npm start                                  # http://localhost:4200，/api 轉給 5080
```

登入（ADR-0009）：企業帳號 `Ymir__Auth__Oidc__Authority` / `ClientId` / `ClientSecret`（Entra ID，設定值見 `docs/guides/entra-id.md`），或由 Admin 在「管理 → 系統設定」設定（ADR-0010：資料庫優先、不重啟生效；authority 由 `Ymir__Auth__Oidc__AuthorityHost` + Tenant ID 組成）；本機帳號 `Ymir__Auth__LocalAccounts__Enabled`（預設開啟，由 Admin 建立，第一個 Admin 可用 `dotnet Ymir.Api.dll create-local-admin <帳號>`）；Development 另有 `/api/dev/login`。

Development 環境預設 `VibeMaker:Harness=Scripted`（假 Agent）。要接真正的 Pi：設定 `VibeMaker__Harness=Pi` 並啟動 Fake LLM。

管理介面（ADR-0010）：`/admin` 總覽、使用者、用量、Make 主題、系統設定（Entra ID、Cloudflare Tunnel token 與對外網域、執行政策）、稽核紀錄；端點在 `src/Ymir.Api/Endpoints/Admin*.cs`，跨模組資料（使用者名稱 + Vibe Maker 統計）在 Api 層組合。稽核只讀查詢用 `IAuditLogQuery`，寫入仍只經由 `IAuditLog`。

對話檔案（Agent 產生的成果）：`GET /api/conversations/{id}/files`、`/files/download?path=`、`/files/archive`（zip）。經 `IWorkspaceFileReader` 在使用者 runtime 內執行 `find` / `bash` 讀取，所以 Remote（runtime host）也適用；路徑經 stdin 傳入、runtime 內以 realpath 確認不逃出工作目錄；下載一律附件（octet-stream、nosniff、CSP sandbox），不得在 Ymir 網域上直接開啟 Agent 產生的 HTML。

對話與專案的「刪除」一律是封存（`Status = Archived`，資料與 runtime 內的檔案保留；封存後所有端點回 404，執行中的對話不能封存）。`ConversationResponse.ActiveExecutionId` 讓前端重新整理後接回執行中的 SSE（`resumeTurnFrom`）；檔案面板的預覽只用文字綁定或 blob URL 的 `<img>`，不得以 innerHTML 或 iframe 顯示 Agent 產生的內容。

Runtime 生命週期（ADR-0011）：`RuntimeLifecycleWorker` 啟動時對帳、定期停止閒置的 runtime（跳過執行中的使用者）；執行政策（閒置時間、單次執行上限、每人排隊上限、每日次數）由 `RuntimePolicyService` 提供，管理介面的值（`vibemaker.runtime_policy`）優先於 `VibeMaker__Runtime__*`；超過配額回 429 `QUOTA_EXCEEDED`。生命週期操作（停止、查詢、對帳）一律以 user id 呼叫 `StopForUserAsync` / `GetStatusForUserAsync`，不要用 runtime id（服務重啟後會變）。

維運（Sprint 5 強化）：
- runtime 的 create / start / stop / reconcile 都寫稽核：`RuntimeInfo.Transition` 由 EnsureRuntime 回報。
- 監控指標在 `VibeMakerTelemetry`（meter / ActivitySource `Ymir.VibeMaker`）；每個 execution 有自己的 activity 與 log scope（ExecutionId）。
- `/health` 包含 database / runtime / litellm 檢查（`VibeMakerHealthChecks`），細節在 `/api/admin/health`；描述只寫摘要，不含路徑或例外訊息。
- `SecurityHeaders` 為所有回應加 CSP 等標頭：Angular build 不可產生 inline script（`angular.json` 的 `inlineCritical: false`），新的第三方腳本或 iframe 會被 CSP 擋下，需要時更新 CSP 並說明原因。
- `DataRetentionWorker` 依 `Ymir:Retention:*` 清理過期的稽核與 execution 事件；對話、訊息、檔案永久保留。
- 備份指南：`docs/guides/backup-restore.md`。

`/make`：對話輸入 `/make` 顯示管理員設定的主題按鈕（`vibemaker.make_topics`，Admin 在「管理 → Make 主題」維護）；給 Agent 的完整指示由後端 `MakePromptBuilder` 組合並存在 `AgentExecution.AgentPrompt`，對話紀錄只保留使用者輸入的文字。

可選模型：`VibeMaker__Models__N__Id` / `DisplayName`（預設為 `VibeMaker__Pi__ModelId`）；個人與專案 system prompt 以檔案附加在 Pi 預設 prompt 之後（`--append-system-prompt`，不經程序參數）。

模型金鑰（ADR-0004）：設定 `VibeMaker__LiteLlm__BaseUrl` 與 `VibeMaker__LiteLlm__MasterKey` 後，API 為每位使用者向 LiteLLM 發 virtual key（帶 `user_id`，發 key 前 upsert LiteLLM 使用者並套用每月預算）；用量頁的費用 / token 與預算檢查都經 `IModelGateway` 讀 LiteLLM，Ymir 不自己保存；非 Development 未設定會拒絕啟動。本機可用 `FAKE_LLM_MASTER_KEY=<任意值>` 讓 Fake LLM 模擬 LiteLLM 的 key 管理（說明見 `deploy/litellm/README.md`）。

Runtime：`VibeMaker:Runtime:Provider` = `Podman`（正式）| `Docker`（只用於開發 / 驗證，例如 Windows，ADR-0005；指南 `docs/guides/windows-docker.md`）| `Local`（Linux / macOS 開發用，無隔離）| `Remote`（API 在容器內，呼叫 runtime host：`VibeMaker:Runtime:Remote:Endpoint` / `Token`，ADR-0008）。Runtime host 自己用 Podman / Docker / Local。

API image：`podman build -f src/Ymir.Api/Containerfile -t localhost/ymir/api:dev .`。API container 的 engine：Linux 正式主機用 **rootful Podman**（Quadlet），Windows 開發機用 **Docker Desktop**（runtime host 聽 `127.0.0.1:5090`，API 經 `host.docker.internal` 連線，只限開發）；部署見 `deploy/api/README.md`、`deploy/runtime-host/README.md`。

## 架構規則

- **分層**：`Api → *.Infrastructure → 模組核心（Domain + Application）→ Contracts`。Application 層不得直接呼叫 podman、不得解析 Pi 協定（SA §19）。模組之間不得互相參考。
- **Runtime 與 Harness 分離**：`IAgentRuntimeManager` 只負責「在隔離環境啟動程序並提供 stdio」；Pi 協定全部在 `PiAgentHarness` / `PiRpcEventMapper`（ADR-0003）。
- **Harness 的事件保證**：每次 run 最後一個事件必定是 `AgentCompleted` / `AgentFailed` / `AgentCancelled`；取消時不拋例外。
- **SSE 契約**以 `Ymir.VibeMaker.Contracts.Executions` 與 `web/src/app/core/executions/execution-events.ts` 為準，兩邊必須同步修改。其他 API 型別一律由 OpenAPI 產生，不要手寫 DTO。
- **Execution**：與 HTTP request 解耦，由 `ExecutionWorker` 背景執行；事件**先寫 `execution_events` 再發佈**；任何結束路徑（完成、失敗、取消、逾時、例外）都必須寫終止事件並推進狀態（`ExecutionRunner`）。
- **授權預設拒絕**：fallback policy 要求登入；匿名端點必須明確 `AllowAnonymous`。狀態變更端點加 `RequireAntiforgeryHeader()`。新 `/api` 端點必須加入 `AuthorizationMatrixTests`，否則測試失敗。
- **認證**採 BFF + HttpOnly Cookie（ADR-0002）：前端不得保存 token，SSE 使用原生 `EventSource`。企業帳號以 `iss` + `oid` 識別、角色以 Entra app role 為準；本機帳號角色由 Ymir 管理；每個請求檢查帳號狀態，停用立即生效（ADR-0009）。Admin 端點用 `AuthSetup.AdminPolicy`，並加入授權矩陣的 `AdminOnlyRequests`。
- **資料存取**：Application 層透過 `IVibeMakerDbContext`（EF Core DbSet）存取；跨模組只存 id、不建 FK（ADR-0001）。並行規則（同 Conversation 單一執行中、冪等鍵）由 filtered unique index 保證，不要改成先查再寫。
- **Pi 版本鎖定**在 `runtime/agent/Containerfile` 與 CI；升級時必須重新錄製 `tests/Ymir.UnitTests/Fixtures/pi-rpc/` 並重跑整合測試。

## 安全紅線（違反即退回）

- 所有 Project / Conversation / Message / Execution API 必須在 server 端驗證擁有者；不得信任前端傳入的 user id（SA §12）。新 API 必須加入授權矩陣測試。
- Host 路徑只能由 user id 推導（`UserDirectories`）；Agent 工作目錄只能是 `RuntimePaths` 由 Guid 產生的 `/workspace/projects/{id}`、`/workspace/chats/{id}`（ADR-0007）。API 不接受任何外部傳入的路徑。讀取檔案時必須解析 realpath 並拒絕 `..` 與 symlink 逃逸。
- Container 不得 `--privileged`、不得掛載 container runtime socket 或 host 敏感路徑；改動 `ContainerCommandBuilder` 時同步更新 `ContainerCommandBuilderTests`（Podman 與 Docker 兩種 engine 都要符合）。
- Container 內不得出現 LiteLLM master key、DB 連線字串或 AD 憑證（ADR-0004）。環境變數以 `podman exec --env NAME` 傳遞，值不得出現在程序參數。
- 回給瀏覽器的錯誤與 tool 事件只能是摘要：不得含 stack trace、host path、token、完整 command output（SA §10、§12）。原始細節只寫 server log。
- `LocalRuntimeManager` 沒有隔離，只允許 Development 環境（DI 會在其他環境拒絕啟動）。
- API container 不得掛載 container runtime socket（podman.sock / docker.sock）或使用者 workspace，只能經由 runtime host（ADR-0008）。Runtime host 的端點只接受 user id 與 runtime 內的程序規格，不得新增接受 host 路徑、image、掛載或資源設定的端點；改動協定時同步更新 `RuntimeHostProtocolTests` 與 `RuntimeHostTests`。Runtime host token 只放在部署 secret，不得進版控；不得用 rootless Podman 帳號 `ymir` 跑 API container。
- 不得保存 IdP token（`SaveTokens=false`）；登入後導回位址只接受站內相對路徑（`SafeRedirect`）。Entra client secret 只放在 `deploy/api/.env`、部署 secret，或經管理介面以 Data Protection 加密存進 `platform.system_settings`（ADR-0010）；任何 API 回應、稽核、log 都不得包含 secret 值，`ISystemSettingsStore.SetAsync`（明文）不得用來存機密。本機帳號密碼只存 `PasswordHasher` 雜湊，不得記錄、回傳或寫入 log；登入端點的錯誤訊息不得區分「帳號不存在」與「密碼錯誤」。
- 模型供應商金鑰（例如 `MINIMAX_API_KEY`）與 LiteLLM master key 只放在 `deploy/*/.env` 或部署環境的 secret，不得進版控、不得進 Agent container。
- 對外公開（`Ymir:PublicEdge`，ADR-0006）不得在 Development 環境開啟；API 只綁 127.0.0.1、只信任 cloudflared 的 `X-Forwarded-*`。Tunnel 憑證不得進版控、不得進 API 或 Agent container；由管理介面設定時只經 runtime host 寫入 `ymir` 帳號的 600 檔案（ADR-0010），API 與資料庫不得保存、回應與稽核不得包含 token。Runtime host 的 tunnel 端點只接受 token 字串，檔案位置與服務名稱只來自 runtime host 設定；新增端點時同步更新 `TunnelEndpointTests.RuntimeHost_ExposesOnlyTheReviewedEndpoints`。

## 程式風格

- C#：file-scoped namespace、`sealed` 預設、record 表示不可變資料、`ConfigureAwait(false)`（library 專案）、註解用繁體中文並說明「為什麼」，引用 SA 章節或 ADR 編號。
- 分析器等級 `latest-recommended` 且警告視為錯誤；確實需要壓制時以 `#pragma warning disable` 加上理由。
- 測試：xUnit v3（`TestContext.Current.CancellationToken`）；外部依賴用 Fake LLM / Scripted harness，不要在測試中呼叫真實模型。
- Angular：standalone component、signals、`ChangeDetectionStrategy.OnPush`、新的控制流程語法（`@if` / `@for`）；狀態轉換寫成純函式並加上 Vitest 測試。

## 每次修改後的流程（必做）

任何檔案修改完成後，結束回合前依序完成：

1. **驗證**：跑相關的 build、test、lint，確認通過。開發在 Claude Code 雲端沙箱進行；需要沙箱網路不允許的外部資源（例如模型供應商 API）的驗證可以**跳過**，改用 Fake LLM 等本機替代驗證，並在看板註明「未驗證、待使用者環境確認」，不要為此繞過網路限制。
2. **Commit**：一次一個垂直切片，訊息說明做了什麼與為什麼，結尾附 attribution trailer。
3. **更新進度紀錄**：在 `docs/progress/` 目前 Sprint 的看板留言（做了什麼、實際跑過的驗證、卡關與待決定事項），更新置頂區的工作項目狀態，然後 commit。
4. **Push**：`git push -u origin <目前分支>`；網路失敗依 2 / 4 / 8 / 16 秒重試。不要推到其他分支。
5. **合併回 main**（使用者要求：每次做完都直接 merge）：開 PR → `main`，等 CI 全部通過後以 merge commit 合併（指定 head SHA）。CI 失敗就修正、驗證、commit、看板留言、push，直到通過；不得跳過或停用測試。合併後把工作分支重設到最新的 `main`（`git fetch origin main && git checkout -B <分支> origin/main`）再開始下一件工作。

`.claude/hooks/require-commit-push-progress.sh`（Stop hook）會在回合結束時檢查：有未 commit 的變更、最後一次更新 `docs/progress/` 之後還有其他 commit、或有未 push 的 commit，都會擋下要求補完。
