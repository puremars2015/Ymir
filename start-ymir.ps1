# 啟動目前 Windows 部署：Docker 提供資料庫／網頁／Tunnel，API 在主機背景執行。
# 不建置、不拉程式碼、不建立或重建容器，也不啟動使用者的 Agent runtime。
[CmdletBinding()]
param(
    [string]$DeployDirectory = (Join-Path $env:LOCALAPPDATA 'Ymir\deploy'),
    [ValidateRange(1, 65535)][int]$ApiPort = 5081
)

$ErrorActionPreference = 'Stop'
$startupLock = [System.Threading.Mutex]::new($false, 'Local\Ymir.WindowsStartup')
$lockHeld = $false
$apiProcess = $null
$logHint = $null

function Test-DockerReady {
    try {
        & $docker info --format '{{.ServerVersion}}' *> $null
        return $LASTEXITCODE -eq 0
    }
    catch {
        # Windows PowerShell 5.1 會把原生命令的 stderr 視為 PowerShell 錯誤。
        return $false
    }
}

function Test-ApiHealth {
    try {
        $response = Invoke-WebRequest -Uri "http://127.0.0.1:$ApiPort/health" -UseBasicParsing -TimeoutSec 5
        return $response.StatusCode -eq 200 -and $response.Content.Trim() -eq 'Healthy'
    }
    catch {
        return $false
    }
}

try {
    try { $lockHeld = $startupLock.WaitOne(0) }
    catch [System.Threading.AbandonedMutexException] { $lockHeld = $true }
    if (-not $lockHeld) { throw '另一個 Ymir 啟動程序正在執行，請等它完成。' }

    $docker = (Get-Command docker.exe -ErrorAction Stop).Source
    $launcher = Join-Path $DeployDirectory 'run-api.ps1'
    if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) {
        throw "找不到已部署的 API 啟動檔：$launcher。請先完成部署，或指定 -DeployDirectory。"
    }

    Write-Host '檢查 Docker Desktop…'
    if (-not (Test-DockerReady)) {
        $desktop = Join-Path $env:ProgramFiles 'Docker\Docker\Docker Desktop.exe'
        if (-not (Test-Path -LiteralPath $desktop -PathType Leaf)) {
            throw 'Docker 尚未就緒，且找不到 Docker Desktop，請手動啟動後重試。'
        }
        Start-Process -FilePath $desktop -WindowStyle Hidden | Out-Null
        Write-Host '已啟動 Docker Desktop，等待引擎就緒（最多 120 秒）…'
        $deadline = [DateTime]::UtcNow.AddSeconds(120)
        do {
            Start-Sleep -Seconds 3
            $dockerReady = Test-DockerReady
        } while (-not $dockerReady -and [DateTime]::UtcNow -lt $deadline)
        if (-not $dockerReady) { throw 'Docker 引擎尚未就緒，請確認 Docker Desktop 狀態後重試。' }
    }

    # 先確認所有部署容器存在，避免缺少部署時只啟動一半。
    $containers = @('ymir-sql', 'ymir-litellm-litellm-db-1', 'ymir-litellm-litellm-1', 'ymir-web', 'ymir-tunnel-cloudflared-1')
    foreach ($name in $containers) {
        & $docker container inspect --format '{{.Name}}' $name *> $null
        if ($LASTEXITCODE -ne 0) { throw "找不到容器 $name，請先完成 Ymir 部署。" }
    }
    foreach ($name in $containers) {
        $state = & $docker container inspect --format '{{.State.Status}}' $name
        if ($LASTEXITCODE -ne 0) { throw "無法檢查容器 $name。" }
        if ($state.Trim() -eq 'running') {
            Write-Host "$name 已啟動。"
        }
        elseif ($state.Trim() -in @('created', 'exited')) {
            Write-Host "啟動 $name…"
            & $docker start $name | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "啟動 $name 失敗，請查看 Docker Desktop 的容器紀錄。" }
        }
        else {
            throw "$name 狀態為 $state，請先在 Docker Desktop 處理後重試。"
        }
    }

    $listeners = @(Get-NetTCPConnection -State Listen -LocalPort $ApiPort -ErrorAction SilentlyContinue)
    if ($listeners.Count -gt 0) {
        foreach ($listener in $listeners) {
            $process = Get-CimInstance Win32_Process -Filter "ProcessId=$($listener.OwningProcess)"
            if ($null -eq $process -or $process.CommandLine -notmatch '(?i)\bYmir\.Api(?:\.dll|\.exe)?\b') {
                throw "連接埠 $ApiPort 已被其他程式占用，未啟動第二份 API。"
            }
        }
        Write-Host 'Ymir API 已啟動，確認服務健康狀態…'
    }
    else {
        $logDirectory = Join-Path $DeployDirectory 'logs'
        New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
        $stdout = Join-Path $logDirectory "startup-$stamp.out.log"
        $stderr = Join-Path $logDirectory "startup-$stamp.err.log"
        $logHint = "API 紀錄：$stdout；$stderr"
        $shell = (Get-Process -Id $PID).Path
        $apiProcess = Start-Process -FilePath $shell -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"{0}"' -f $launcher)) -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
        Write-Host '已在背景啟動 API，等待服務就緒（最多 120 秒）…'
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    do {
        $apiReady = Test-ApiHealth
        if ($apiReady) { break }
        if ($null -ne $apiProcess -and $apiProcess.HasExited) { throw 'API 啟動程序已結束，請查看紀錄。' }
        Start-Sleep -Seconds 3
    } while ([DateTime]::UtcNow -lt $deadline)
    if (-not $apiReady) { throw 'API 未通過健康檢查，請確認資料庫、LiteLLM 與部署設定；不會重複啟動 API。' }

    $web = Invoke-WebRequest -Uri 'http://127.0.0.1:5080/login' -UseBasicParsing -TimeoutSec 10
    if ($web.StatusCode -ne 200) { throw '本機網頁未就緒，請查看 ymir-web 的紀錄。' }
    Write-Host ''
    Write-Host 'Ymir 已啟動，可以關閉這個視窗。' -ForegroundColor Green
    Write-Host '網站：https://ymir.thetainformation.com'
    Write-Host '本機網頁：http://localhost:5080（正式登入請使用 HTTPS 網址）'
    Write-Host '外網連線仍取決於既有 Cloudflare Tunnel、DNS 與網路狀態。'
    if ($logHint) { Write-Host $logHint }
}
catch {
    if ($logHint) { Write-Host $logHint }
    Write-Error $_
}
finally {
    if ($lockHeld) { $startupLock.ReleaseMutex() }
    $startupLock.Dispose()
}
