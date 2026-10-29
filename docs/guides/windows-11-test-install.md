# Windows 11 Professional 測試機安裝總指南

> 適用：Windows 11 Pro x64、Docker Desktop（WSL 2／Linux containers）、Windows 原生 PowerShell 7。這是本機開發／功能測試環境，**不是正式部署，也不能取代 Ubuntu 的隔離與維運驗收**。
>
> 文件依程式與既有部署範本整理；本輪未在乾淨 Windows 11 主機實際安裝。Docker Desktop、Windows 防火牆、NTFS 掛載及重開機恢復仍須依本文實機驗收。首次安裝與既有資料更新不可混用。

## 1. 選擇測試架構

### A：先建立的基本測試環境

```text
瀏覽器 localhost:4200 → Angular dev server → Windows 上 API :5080
                                              ├→ Docker SQL Server
                                              ├→ Windows Fake LLM :5199
                                              └→ Docker CLI → 每人一個 Linux Agent／Pi
                                                                    └→ host.docker.internal:5199
```

先用 **真 Pi＋Fake LLM** 驗證對話、工具、檔案與取消，不需要供應商金鑰、不產生模型費用。API 設 `Development`＋`Runtime= Docker`，第一次啟動自動套兩模組 migration；資料必須是這台機器專用的測試資料。

### B：選用的 API 容器化測試

基本驗收後可改為「API＋Angular build 在 Docker container → Windows RuntimeHost → Docker Agent」，見 §10。A／B **二擇一啟動**，不能同時占用 5080、使用同一資料庫執行背景工作或管理同一批 Agent。

| Windows 可驗證 | 仍必須在 Ubuntu 驗證 |
|---|---|
| 真 Pi RPC、SSE、取消、每人容器、附件與成果保存、一般 Docker 安全參數 | rootless Podman、keep-id、正式 cgroup delegation／user scope、SELinux、正式 Unix socket 權限、Quadlet／systemd 開機程序、正式受限網路 |
| Fake LLM 功能；另行配置後的真 LiteLLM／SSO | 真模型費用與供應商可用性、正式 Tunnel／DNS、備份還原與高可用 |

決策依據：[ADR-0005](../adr/0005-docker-for-development.md)、[ADR-0008](../adr/0008-containerized-api-runtime-host.md)。本指南不安裝 k8s、Podman、nginx、Cloudflare Tunnel 或 Ubuntu 的 systemd 修復工具。

## 2. 硬體、系統與安全前置

| 項目 | 要求／建議 |
|---|---|
| OS／CPU | 受支援且完成更新的 Windows 11 Professional **x64／Intel 或 AMD**；本指南不涵蓋 ARM64，SQL Server Linux image 不能以原生 ARM64 路徑驗收 |
| 虛擬化 | BIOS／UEFI 開啟硬體虛擬化；Task Manager → Performance → CPU 顯示 Virtualization enabled；若 Windows 自己在 VM 裡，需主機允許 nested virtualization |
| 資源 | 建議至少 4 核心、16 GiB RAM（多 Agent 建議 32 GiB）、100 GiB 可用 SSD；不是容量或效能保證，單 Agent 預設上限 2 GiB |
| 工具 | Git、PowerShell 7、.NET **10 SDK**（依 `global.json`）、Node **24 LTS，至少 24.15**、Docker Desktop＋Compose v2 |
| 權限 | 啟用 WSL／安裝工具時用系統管理員；日常 git、dotnet、npm、Docker 使用自己的標準使用者身分，不長期用 Administrator 開發 |
| 路徑 | Repo 用 `C:\src\Ymir`；測試資料用 `%LOCALAPPDATA%\YmirTest`。不要用 OneDrive／網路磁碟／需額外同步的路徑當 workspace |

**Development 有免密碼登入，禁止經 Tunnel／port forwarding 公開，也不開放給 LAN 當共用測試站。** 不連正式 SQL／正式 OAuth／正式 workspace，不把正式備份直接還原到開發環境。公司環境須先確認 Docker Desktop 的授權與 IT 政策。

## 3. 啟用 WSL 2 與 Docker Desktop

在「系統管理員」PowerShell 執行以下 Windows 官方命令；若已安裝先檢查，不重置現有 distro：

```powershell
wsl --install --no-distribution
wsl --update
wsl --set-default-version 2
wsl --status
```

若提示重新啟動，先存檔並重新啟動 Windows；不要假定 shell 關掉就等於安裝完成。這條路徑**不需要另外安裝 Ubuntu distro**，也不在 WSL 裡同時裝第二個 Docker Engine。

依官方來源安裝：

- [Git for Windows](https://git-scm.com/downloads/win)。
- [PowerShell 7](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows)；後續使用 `pwsh`，不是 Windows PowerShell 5.1。
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) x64，不能只裝 runtime。
- [Node.js](https://nodejs.org/en/download) 選 **24.x**；不要因安裝器標 LTS 就裝到另一個主要版本。
- [Docker Desktop for Windows](https://docs.docker.com/desktop/setup/install/windows-install/)；開啟後選 **WSL 2 based engine／Linux containers**，完成使用者授權。若安裝器要求登出／重登則照做。

在新開的 PowerShell 7 檢查；Docker Desktop 必須已啟動且 engine ready：

```powershell
$PSVersionTable.PSVersion
git --version
dotnet --list-sdks
node --version
npm --version
docker version
docker compose version
docker info --format '{{.OSType}}'
```

最後一行預期 `linux`，`docker version` 必須有 Server 區段。依 Docker Desktop／WSL 設定為 Linux VM 留足 RAM／磁碟，不要把所有主機記憶體配置給 VM。Docker daemon 管理權限很高，不要將 TCP daemon 無驗證公開或分享給一般使用者。

參考：[Microsoft WSL 安裝](https://learn.microsoft.com/windows/wsl/install)、[Docker WSL 2 backend](https://docs.docker.com/desktop/features/wsl/)。具體受支援 Windows build／WSL 版本以官方當期要求為準。

## 4. 取得版本、建立私有測試設定

### 4.1 原始碼與套件

在一般使用者 PowerShell 7 執行；範例是**新安裝**，若目錄已存在就停止，改看 §12 更新流程：

```powershell
New-Item -ItemType Directory -Force C:\src | Out-Null
git clone https://github.com/puremars2015/Ymir.git C:\src\Ymir
if ($LASTEXITCODE -ne 0) { throw 'git clone 失敗' }
Set-Location C:\src\Ymir
git status --short --branch
git rev-parse HEAD
dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore 失敗' }
dotnet restore Ymir.slnx
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore 失敗' }
npm --prefix web ci
if ($LASTEXITCODE -ne 0) { throw 'npm ci 失敗' }
```

記錄本次 SHA 與 CI 結果；若要鎖定其他測試版本，先確認沒有未提交工作再切換。不要把 Linux 正式主機的含密鑰部署目錄複製過來。

### 4.2 私有設定（首次建立一次）

以下使用 Windows DPAPI 為**目前 Windows 使用者**保存 SQL 測試密碼，存放於 repo 外；仍需防止其他程式以你的身分執行。不要開 `Start-Transcript` 記錄秘密，不把密碼貼到對話或 Git。密碼至少 16 字元、含大小寫數字特殊符號；此範例限制字元以避免 connection string quoting 問題。

```powershell
$TestRoot = Join-Path $env:LOCALAPPDATA 'YmirTest'
if (Test-Path (Join-Path $TestRoot 'sql-password.xml')) { throw '已有設定；請勿覆蓋密碼，改用下方載入步驟' }
New-Item -ItemType Directory -Force $TestRoot | Out-Null
$SqlSecret = Read-Host '建立這台測試 SQL 的密碼（不使用正式密碼）' -AsSecureString
$SqlPassword = [System.Net.NetworkCredential]::new('', $SqlSecret).Password
if ($SqlPassword.Length -lt 16 -or $SqlPassword -notmatch '^[A-Za-z0-9!@#%^_+=.-]+$' -or
    $SqlPassword -cnotmatch '[A-Z]' -or $SqlPassword -cnotmatch '[a-z]' -or
    $SqlPassword -notmatch '[0-9]' -or $SqlPassword -notmatch '[!@#%^_+=.-]') {
    throw '密碼需至少 16 字元、含大小寫數字特殊符號，且限上述允許字元'
}
[pscredential]::new('sa', $SqlSecret) | Export-Clixml (Join-Path $TestRoot 'sql-password.xml')
New-Item -ItemType Directory -Force (Join-Path $TestRoot 'workspaces'), (Join-Path $TestRoot 'keys') | Out-Null
Remove-Variable SqlPassword, SqlSecret
```

再建立下列共用設定檔，供**每個新終端**載入。檔案本身不含明文密碼；`Load-TestEnv.ps1` 是你信任並自行建立的本機腳本，不得從不明來源下載後直接 dot-source：

```powershell
@'
$ErrorActionPreference = 'Stop'
$RepoRoot = 'C:\src\Ymir'
$TestRoot = Join-Path $env:LOCALAPPDATA 'YmirTest'
$Credential = Import-Clixml (Join-Path $TestRoot 'sql-password.xml')
$env:ConnectionStrings__ymir = "Server=localhost,1433;Database=ymir_win11_test;User Id=sa;Password=$($Credential.GetNetworkCredential().Password);TrustServerCertificate=True"
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ENVIRONMENT = 'Development'
$env:services__api__http__0 = 'http://127.0.0.1:5080'
$env:Ymir__PublicEdge__Enabled = 'false'
$env:Ymir__DataProtection__KeysPath = Join-Path $TestRoot 'keys'
$env:Ymir__Knowledge__Root = Join-Path $TestRoot 'knowledge'
$env:Ymir__Sites__Root = Join-Path $TestRoot 'sites'
$env:VibeMaker__Runtime__Provider = 'Docker'
$env:VibeMaker__Runtime__WorkspaceRoot = Join-Path $TestRoot 'workspaces'
$env:VibeMaker__Runtime__Image = 'localhost/ymir/agent-runtime:win11-test'
$env:VibeMaker__Harness = 'Pi'
$env:VibeMaker__Pi__ModelBaseUrl = 'http://host.docker.internal:5199/v1'
$env:VibeMaker__Pi__ModelId = 'fake-model'
$env:VibeMaker__Pi__DevelopmentApiKey = 'dev-key'
Remove-Variable Credential
Set-Location $RepoRoot
'@ | Set-Content -Encoding utf8 (Join-Path $TestRoot 'Load-TestEnv.ps1')

. (Join-Path $TestRoot 'Load-TestEnv.ps1')
```

**後續每開一個終端，先執行這一行**（會重新解密密碼至該程序的 env，勿輸出 env）：

```powershell
. (Join-Path $env:LOCALAPPDATA 'YmirTest\Load-TestEnv.ps1')
```

DPAPI 檔案只供原使用者／Windows 環境解密，不能當跨主機密碼備份；換機時須另從密碼管理器取得測試密碼。不要用 `setx` 把測試 env 永久塞進系統；避免污染正式或其他專案。

## 5. 啟動持久化 SQL Server

這裡使用 `ymir-win11-sql` 與 `ymir-win11-sql-data`，避免與現有 `ymir-sql` 混淆。若 1433 已被占用，**不要停掉不明服務**；改用另一個 loopback 埠，並同步 §4 載入腳本及 §10 容器 API 的連線字串。

首次建立，在已載入共用設定的終端執行：

```powershell
docker container inspect ymir-win11-sql *> $null
if ($LASTEXITCODE -eq 0) { throw '容器已存在；日常啟動使用 docker start，不重建' }
docker volume inspect ymir-win11-sql-data *> $null
if ($LASTEXITCODE -eq 0) { throw 'SQL volume 已存在；先確認資料來源與原密碼，不套首次建立流程' }
$Credential = Import-Clixml (Join-Path $TestRoot 'sql-password.xml')
$env:MSSQL_SA_PASSWORD = $Credential.GetNetworkCredential().Password
try {
    docker volume create ymir-win11-sql-data
    if ($LASTEXITCODE -ne 0) { throw '建立 SQL volume 失敗' }
    docker run -d --name ymir-win11-sql --restart unless-stopped --stop-timeout 60 `
      -p 127.0.0.1:1433:1433 --env ACCEPT_EULA=Y --env MSSQL_PID=Developer `
      --env MSSQL_SA_PASSWORD --mount source=ymir-win11-sql-data,target=/var/opt/mssql `
      mcr.microsoft.com/mssql/server:2022-latest
    if ($LASTEXITCODE -ne 0) { throw '建立 SQL 容器失敗' }
} finally {
    Remove-Item Env:MSSQL_SA_PASSWORD -ErrorAction SilentlyContinue
    Remove-Variable Credential
}
```

`--env MSSQL_SA_PASSWORD` 只把變數名稱放在 command line；Docker daemon 仍能讀到 env，不能把它說成 secret vault。Developer edition 僅測試／開發，不得用於正式工作負載。`2022-latest` 是拉取入口，記錄實際 image ID；日後不要在一般應用換版時順便拉新 DB image。

檢查 SQL readiness（最多約兩分鐘；不是只看 container running）：

```powershell
docker logs --tail 30 ymir-win11-sql
$Ready = $false
for ($i = 0; $i -lt 60; $i++) {
    docker exec ymir-win11-sql bash -lc 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -Q "SELECT 1"' *> $null
    if ($LASTEXITCODE -eq 0) { $Ready = $true; break }
    Start-Sleep -Seconds 2
}
if (-not $Ready) { throw 'SQL 尚未就緒；停止安裝並查看 SQL log，不刪 volume' }
docker inspect ymir-win11-sql --format '{{.Image}}'
```

Production 的最小權限／migration 帳號仍須另外測試；本指南 `sa` 只服務隔離的 Development 測試 DB。保留 volume 不能取代 SQL 原生備份。

## 6. 建置 Agent image

在 repo 根目錄、同一個 Docker Desktop Linux engine 建置：

```powershell
docker build -t localhost/ymir/agent-runtime:win11-test -f runtime/agent/Containerfile runtime/agent
if ($LASTEXITCODE -ne 0) { throw 'Agent image 建置失敗' }
docker image inspect localhost/ymir/agent-runtime:win11-test --format '{{.Id}}'
```

不要自行升級 Pi，版本以 Containerfile 為準；不需把 Pi 全域安裝到 Windows 才能跑 Docker Agent。整合測試另外有主機 Pi 相依，見 §9。第一次執行會建立 Windows workspace／agent-state 及容器；Docker 路徑分享／防毒若阻擋，先依政策調整專用測試目錄，不能用 privileged 或關閉全部防護繞過。

## 7. 依序啟動 Fake LLM、API、前端

以下三個程序各開一個一般使用者 PowerShell 7 終端，**每個都先載入 §4 的 `Load-TestEnv.ps1`**。程序在前景運作，視窗關閉即停止；不是 Windows service。

### 終端 A：Fake LLM

```powershell
. (Join-Path $env:LOCALAPPDATA 'YmirTest\Load-TestEnv.ps1')
dotnet run --no-launch-profile --project tests/Ymir.Testing.FakeLlm -- --urls http://0.0.0.0:5199
```

Fake LLM 因為要讓 Docker Agent 透過 `host.docker.internal` 回連，才監聽 `0.0.0.0`；API／前端不照做。**5199 不得對外開放**：使用隔離測試網段，依 IT 政策將防火牆存取限於本機／Docker Desktop 需要的來源，不接受「允許所有網路」或關閉防火牆。若公司政策無法安全允許這條回連，先停止、請 IT 配置或另規劃容器內 Fake LLM，不公開此端點。

另一個終端驗證主機與 Linux container 都能連線（不建立使用者 Agent）：

```powershell
Invoke-RestMethod http://127.0.0.1:5199/health/liveliness
docker run --rm --network bridge --entrypoint node localhost/ymir/agent-runtime:win11-test `
  -e "fetch('http://host.docker.internal:5199/health/liveliness',{signal:AbortSignal.timeout(10000)}).then(async r=>{if(!r.ok)throw new Error(String(r.status));console.log(await r.text())}).catch(e=>{console.error(e.message);process.exitCode=1})"
if ($LASTEXITCODE -ne 0) { throw 'Docker 到 Fake LLM 不通；先排查 DNS／防火牆' }
```

這是一次性連線測試容器，不呼叫真模型、不需 master key。

### 終端 B：Windows 主機 API（方案 A）

```powershell
. (Join-Path $env:LOCALAPPDATA 'YmirTest\Load-TestEnv.ps1')
dotnet run --no-launch-profile --project src/Ymir.Api -- --urls http://127.0.0.1:5080
```

第一次啟動會自動建立 `ymir_win11_test` 並套兩組 migration；失敗先查 DB 與程式 log，不能跳過 schema 失敗。明確使用 `Docker`，Windows 不支援 `Local` runtime。`--no-launch-profile` 避免 launchSettings 覆蓋本機選定的環境與埠。

```powershell
Invoke-RestMethod http://127.0.0.1:5080/health
Invoke-RestMethod http://127.0.0.1:5080/api/auth/providers
```

預期 API health 為 `Healthy`；若不健康先解決 SQL／Docker／模型路徑，不啟動另一套 API 占相同埠。

### 終端 C：Angular

```powershell
. (Join-Path $env:LOCALAPPDATA 'YmirTest\Load-TestEnv.ps1')
Set-Location (Join-Path $RepoRoot 'web')
npm start -- --host localhost --port 4200
```

瀏覽 `http://localhost:4200`，透過 Development 測試登入建立測試帳號；日後沿用相同身分，才能看到原對話。不要將 `localhost` 與 `127.0.0.1` 當成相同 cookie origin 交替使用。Angular 的 `/api` proxy 指向 5080，不另外配置跨網域 token 儲存。

## 8. 必做功能驗收

| 項目 | 操作與預期 |
|---|---|
| 真 Pi／工具建檔 | 送 `[create-file] 幫我建立檔案`，Agent 應呼叫工具建立檔案，前端檔案面板可讀／下載；這不是模型理解能力測試 |
| 同一對話延續 | 再送訊息；Fake LLM 的回覆則數應增加，Pi session 保存在 agent-state |
| 取消／錯誤 | 送 `[slow] 慢慢講` 後按停止；送 `[fail]` 驗證錯誤摘要與終止狀態；不要把預期 Fake 失敗當安裝故障 |
| 兩使用者隔離 | 登出後另一身分登入並送訊息，應有不同 `ymir-user-*` 容器／workspace，不可讀第一人的檔案 |
| 檔案／重啟 | 停止再啟動 API，沿用原測試身分，確認對話與附件；依 §11 啟停後再次核對 workspace／成果 |
| 安全／資源 | 下方 inspect 應顯示非 root user、read-only、drop ALL、no-new-privileges；記憶體預設 2 GiB，CPU／pids 按設定 |

只列這台測試環境的容器，先從清單選定正確 Agent 名稱，不用模糊比對後批次刪除：

```powershell
docker ps -a --filter 'name=ymir-user-' --format '{{.Names}}\t{{.Status}}'
$Agent = Read-Host '輸入剛剛建立的測試 Agent 完整名稱'
docker inspect $Agent --format '{{.Config.User}} {{.HostConfig.ReadonlyRootfs}} {{.HostConfig.CapDrop}} {{.HostConfig.SecurityOpt}}'
docker inspect $Agent --format '{{.HostConfig.Memory}} {{.HostConfig.NanoCpus}} {{.HostConfig.PidsLimit}}'
docker stats --no-stream $Agent
```

使用者資料通常在 `%LOCALAPPDATA%\YmirTest\workspaces\users\<userId>\workspace\chats\<conversationId>` 或 `projects\<projectId>`，另一個 `agent-state` 保存 session；以實際檔案／掛載為準，不手動重設 NTFS 權限或刪目錄來解決問題。

「容器重建後仍有檔案／記憶」測試只在備份、沒有執行中工作且確定是自己的測試 Agent 後進行；先正常 stop，再精確移除該**單一**容器（不加 `-f`、不刪主機目錄），下次訊息由 runtime 重建。不要使用全域 rm、volume prune、Docker Desktop Factory Reset 當排錯步驟。

## 9. 開發驗證與選用真實服務

一般檢查（repo 根目錄；PowerShell 對 native command 要檢查 exit code）：

```powershell
dotnet build Ymir.slnx
if ($LASTEXITCODE -ne 0) { throw '後端 build 失敗' }
dotnet test --project tests/Ymir.UnitTests
if ($LASTEXITCODE -ne 0) { throw '單元測試失敗' }
npm --prefix web run lint
if ($LASTEXITCODE -ne 0) { throw 'lint 失敗' }
npm --prefix web test -- --watch=false
if ($LASTEXITCODE -ne 0) { throw '前端測試失敗' }
npm --prefix web run build
if ($LASTEXITCODE -ne 0) { throw '前端 build 失敗' }
```

- **整合測試**：會建立／刪除各自的暫時 DB；先以 `YMIR_TEST_SQLSERVER` 指定專用測試 SQL 與足夠權限，不得指向正式 SQL。真 Pi 主機程序與 POSIX 測試相依不是 Windows 保證支援範圍；完整 suite 依 Linux CI／隔離 WSL 環境驗證，不把 Windows 無法執行的測試停用後宣稱全通過。
- **Playwright**：先於 `web` 安裝 Playwright Chromium，再按個別 `web/e2e/*.mjs` 前置條件設定執行；部分腳本有 Linux 專用 Chromium 路徑／測試伺服器假設，不直接照抄 Ubuntu 指令。
- **真模型**：可另依 [LiteLLM 指南](../../deploy/litellm/README.md)在此測試機建立獨立 PostgreSQL／LiteLLM，使用專用低額度金鑰；API 的 `LiteLlm:BaseUrl` 是主機可達 URL，Agent 的 `Pi:ModelBaseUrl` 是 container 可達 URL，不能只改一個。需同步模型清單／AllowedModels、master key，停止 API 再載入新設定。首次只驗證 Fake，再經授權做付費推論；不要執行 Ubuntu `apply-*-linux.sh`。
- **SSO／OneDrive／MCP／網站**：另依 [Entra](entra-id.md)、[Google](google-login.md)、[OneDrive](onedrive.md)、[MCP](../../deploy/mcp/README.md)、[SiteHost](../../deploy/sites/README.md)的測試前置條件。需要測登入 cookie／SSO 時用受信任的本機 HTTPS、專用 app registration 與 callback，不為方便公開 Development API。
- **Production 模式本機測試**：不是將 `Development` 字串換掉就完成；需先手動 migration、LiteLLM virtual key 設定、本機 HTTPS、管理員／認證與完整 keyring。另立測試計畫，不拿本文 HTTP＋dev login 路徑冒充正式驗收。

## 10. 選用：API 容器＋Windows RuntimeHost（方案 B）

基本方案 A 通過後再做。先停止 A 的 Angular／API（Fake LLM 與 SQL 保留），確認沒有執行中工作；兩種 API 不能同時存取同一測試 DB。

### 10.1 RuntimeHost（獨立終端，前景執行）

先建立只供這台機器的強 token，DPAPI 存於 repo 外；不要輸出 token：

```powershell
. (Join-Path $env:LOCALAPPDATA 'YmirTest\Load-TestEnv.ps1')
$TokenFile = Join-Path $TestRoot 'runtime-token.xml'
if (-not (Test-Path $TokenFile)) {
    $Token = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    [pscredential]::new('runtime', (ConvertTo-SecureString $Token -AsPlainText -Force)) | Export-Clixml $TokenFile
    Remove-Variable Token
}
$env:RuntimeHost__Token = (Import-Clixml $TokenFile).GetNetworkCredential().Password
$env:RuntimeHost__Listen = 'http://127.0.0.1:5090'
$env:RuntimeHost__Tunnel__Mode = 'Disabled'
dotnet run --no-launch-profile --project src/Ymir.RuntimeHost
```

RuntimeHost 沿用共用設定的 `Provider=Docker`、Agent image 與 Windows workspace；不能設 `Provider=Remote`。僅監聽 loopback，API 經 Docker Desktop 的 `host.docker.internal` 轉送；若健康檢查／loopback 驗證不通先停止排錯，不能改成公開 `0.0.0.0` 或停用 token。

### 10.2 建立容器 API 的 `.env.windows`（首次一次）

在另一終端執行。這裡**自行產生專用設定**，不照抄範本裡的示例 SQL 密碼／正式憑證。`.env.windows` 含明文秘密、已被 gitignore，仍須限制本機存取、不分享不提交：

```powershell
. (Join-Path $env:LOCALAPPDATA 'YmirTest\Load-TestEnv.ps1')
$EnvFile = Join-Path $RepoRoot 'deploy\api\.env.windows'
if (Test-Path $EnvFile) { throw '.env.windows 已存在；先備份並核對，不覆蓋' }
$Credential = Import-Clixml (Join-Path $TestRoot 'sql-password.xml')
$Token = (Import-Clixml (Join-Path $TestRoot 'runtime-token.xml')).GetNetworkCredential().Password
$Lines = @(
    'ASPNETCORE_ENVIRONMENT=Development'
    'DOTNET_ENVIRONMENT=Development'
    'Ymir__PublicEdge__Enabled=false'
    "ConnectionStrings__ymir=Server=host.docker.internal,1433;Database=ymir_win11_test;User Id=sa;Password=$($Credential.GetNetworkCredential().Password);TrustServerCertificate=True"
    'VibeMaker__Runtime__Provider=Remote'
    "VibeMaker__Runtime__Remote__Token=$Token"
    'VibeMaker__Harness=Pi'
    'VibeMaker__Pi__ModelBaseUrl=http://host.docker.internal:5199/v1'
    'VibeMaker__Pi__ModelId=fake-model'
    'VibeMaker__Pi__DevelopmentApiKey=dev-key'
)
# Compose 會展開 $；§4 密碼白名單不含 $，token 為十六進位。
$Lines | Set-Content -Encoding utf8 $EnvFile
Remove-Variable Credential, Token, Lines
New-Item -ItemType Directory -Force (Join-Path $RepoRoot 'deploy\api\data\keys') | Out-Null
Set-Location (Join-Path $RepoRoot 'deploy\api')
docker compose -p ymir-win11 -f compose.windows.yml up -d --build api
if ($LASTEXITCODE -ne 0) { throw 'API 容器啟動失敗' }
```

Compose 已明確將 Remote Endpoint 設為 `http://host.docker.internal:5090`，公開埠只綁 `127.0.0.1:5080`，API 不掛 Docker socket 或 Windows workspace；容器內用 `Remote`，不可改成 `Docker` 嘗試直接管理 engine。

**SQL loopback 轉送限制**：此模式沿用 Docker Desktop 的 host forwarding；若 container 無法經 `host.docker.internal:1433` 連到 loopback 發布的 SQL，不能把 SQL 改綁 LAN 當修復。先依 Docker Desktop 版本／IT 政策排查，或另案規劃 API 與 SQL 的專用 Docker network／service DNS；方案 A 不受此 container → host SQL 路徑影響。

```powershell
Invoke-RestMethod http://127.0.0.1:5090/health
Invoke-RestMethod http://127.0.0.1:5080/health
docker compose -p ymir-win11 -f compose.windows.yml ps
docker compose -p ymir-win11 -f compose.windows.yml logs --tail 50 api
```

容器 API 自帶 Angular build，改用 `http://localhost:5080`，**不需 4200 dev server**。更換 origin 後重新做測試登入。原 keyring 不同於 A：此模式使用 `deploy/api/data/keys`，不宣稱兩模式 cookie／加密 token 可互通；需要跨模式遷移時，先規劃成套 DB＋keyring 備份／還原。此 Compose 範本未提供 knowledge／sites 持久 volume，不在此模式驗收資料保存，啟用前須另外規劃。

## 11. 日常啟停與 Windows 重新啟動

| 操作 | 正確順序 |
|---|---|
| 方案 A 啟動 | Docker Desktop engine ready → `docker start ymir-win11-sql` → SQL readiness → Fake LLM → API → Angular |
| 方案 B 啟動 | Docker ready → SQL readiness → Fake LLM → Windows RuntimeHost → Compose API（若已 running 先查 readiness，勿重建） |
| 停止 A | 停送新訊息、取消／排空 → Angular Ctrl+C → API Ctrl+C → Fake LLM Ctrl+C → 視需要停止指定 Agent／SQL |
| 停止 B | 排空 → `docker compose -p ymir-win11 -f compose.windows.yml stop api`（在 `deploy/api`）→ RuntimeHost Ctrl+C → Fake LLM Ctrl+C → 視需要停 SQL |

停止 SQL 使用 `docker stop --time 60 ymir-win11-sql`，不要刪 volume。Agent 預設常駐（ADR-0033），可由 Admin 在管理介面停止，或在無工作時精確 stop 已確認的測試容器；不批次停止其他專案。

Docker Desktop 可選「登入時啟動」，`restart: unless-stopped` **不等於**未登入就有無人值守的 Windows service；手動 stopped 的容器也不保證下次登入自動啟動。Windows 上的 `dotnet run`／`npm start` 不會因 Docker ready 而自動啟動，重新啟動 Windows 後須按表依序重啟並驗收。

repo 的 [`start-ymir.ps1`](../../start-ymir.ps1) 是**特定既有主機部署 launcher**，預設 SQL 名稱／部署目錄不同；它不會替新機建立本指南的設定，也不是直接適用此命名的總安裝器。不照抄 Ubuntu 的 `fix-boot.sh`／systemctl，亦不為此新增排程工作或任意提高執行權限。

## 12. 更新、備份與回復

1. 記錄原 SHA、SQL／Agent／API image ID、使用的 A／B、測試設定、DB＋keyring／workspace 路徑；將紀錄依[部署流水帳範本](../deployments/README.md)保存在去機密文件中。
2. 更新前排空並停止本機 API／RuntimeHost 寫入，SQL 做原生備份及 verify，匯出到 Docker VM 外；保存相配的 keyring、workspace／agent-state、需要的 knowledge／sites。測試資料也可能含個資，備份需加密且限制存取；細節見[備份還原](backup-restore.md)，其中 Linux 指令不可原樣用於 Windows。
3. 乾淨 main 執行 `git fetch origin`、`git rebase origin/main`，保留其他人的未提交工作；`npm --prefix web ci`、`dotnet restore`，再檢查本次 migration。Development **啟動即自動 migrate**，不能先啟動再想備份。
4. 只有 Agent Containerfile／工具變動才建新 image，先記錄新舊 ID；**改 image 設定不會自動替換既有 Agent**。選定閒置測試使用者、保留兩個 host 資料目錄、再精確安排單一 container 替換，不 force rm。
5. A 重新啟動 host API／Angular；B 只重建 API service。一般應用更新不重建 SQL、不刪 volume、不清空 keyring，不做 Docker Desktop factory reset。
6. 回復時若 schema 相容，恢復舊程式／image／設定；不相容須依事前計畫還原測試 DB＋keyring／資料。不能把對話遺失稱為成功回復，也不盲目 EF down migration。備份之後新增的資料可能遺失，須先確認。
7. 重做 §8 驗收，登記實際結果。完整重新開機／備份還原成功必須實際測試，不能只看 container 綠燈。

## 13. 排錯順序

| 症狀 | 先查／處理 |
|---|---|
| Docker CLI 找不到 daemon／顯示 windows | Docker Desktop ready、Linux containers、WSL 狀態；不要再啟第二套 engine |
| SQL 一直退出 | 密碼政策、RAM、x64 image、1433 衝突、SQL logs；volume 已有 DB 時改環境密碼不會自動更改原 sa 密碼 |
| API 啟動要用 `sh`／Local 不支援 | 是否載入本機設定、Provider 必須 Docker（A）或 Remote（B）；不要把 Windows bash 路徑塞進 Local runtime |
| 找不到 Agent image | tag 是否與 `VibeMaker__Runtime__Image` 一致、是否在相同 Docker engine 建置 |
| `[create-file]` 只回一般文字／沒有容器 | 是否使用 `Harness=Pi`，不是預設 Scripted；檢查容器及執行紀錄 |
| 模型 Connection error | Fake LLM 程序存活、Docker 內 `host.docker.internal:5199`、Windows 防火牆；主機 localhost 正常不代表 container 可達 |
| Remote 401／連線失敗 | B 的 token 是否相同、RuntimeHost 5090 是否啟動、Docker Desktop loopback forwarding；不能取消 token 驗證 |
| 4200 的 `/api` 502 | A API 是否在 5080、Angular proxy 設定、不要同時執行 B |
| NTFS／agent-state permission denied | 正確本機 workspace、Docker 檔案分享、受控資料夾存取／防毒政策；先備份再排查，不刪未知目錄／不廣泛 chmod |
| 檔案操作慢 | Windows bind mount／掃毒成本；需要 Linux 效能可另開 WSL Linux 路徑部署，但不能與 Windows runtime 共用同一份 DB／workspace 同時跑 |
| Cookie／SSO 失敗 | localhost origin、Development HTTP 與 Production Secure cookie 差異、信任憑證與 callback；不要把所有環境的 Secure 關掉 |

## 14. 驗收／交接紀錄

- [ ] Windows edition／build、CPU 架構、WSL／Docker／SDK／Node 版本已記錄，Docker Server 為 Linux。
- [ ] SQL volume／image ID／測試 DB 已核對，資料不在 container writable layer。
- [ ] API／前端僅本機可用，Fake LLM 回連經防火牆政策限制；未啟動 Tunnel。
- [ ] 真 Pi＋Fake LLM：建檔、取消、錯誤、兩使用者隔離與資料重啟保存通過。
- [ ] 明確標示使用方案 A 或 B、已跑哪些測試及哪些未驗證。
- [ ] 成套備份／還原與 Windows 重開機恢復實測並記錄，不宣稱等同 Ubuntu 正式驗收。

其他入口：[Windows Docker 短指南](windows-docker.md)、[API Windows 範本](../../deploy/api/compose.windows.yml)、[Ubuntu 正式安裝](ubuntu-install.md)、[container script 對照](container-deployment.md)。
