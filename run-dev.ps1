<#
  One-command dev env startup. All logs go to .\logs\ (the agent reads these files).

  Usage:
    .\run-dev.ps1            # infra + build + start Web & Worker
    .\run-dev.ps1 -BuildOnly # infra + build only (no run)
    .\run-dev.ps1 -SkipInfra # skip docker, only build + run
#>
param(
  [switch]$SkipInfra,
  [switch]$BuildOnly
)

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root
New-Item -ItemType Directory -Force -Path "$root\logs" | Out-Null

function Wait-Port([int]$port, [int]$timeoutSec = 90) {
  $deadline = (Get-Date).AddSeconds($timeoutSec)
  while ((Get-Date) -lt $deadline) {
    $c = New-Object System.Net.Sockets.TcpClient
    try {
      $iar = $c.BeginConnect('127.0.0.1', $port, $null, $null)
      if ($iar.AsyncWaitHandle.WaitOne(1000)) { $c.EndConnect($iar); $c.Close(); return $true }
    } catch {}
    $c.Close()
    Start-Sleep 1
  }
  return $false
}

function Wait-Log([string]$file, [string]$text, [int]$timeoutSec = 150) {
  $deadline = (Get-Date).AddSeconds($timeoutSec)
  while ((Get-Date) -lt $deadline) {
    if (Test-Path $file) {
      $m = Select-String -Path $file -Pattern $text -Quiet -ErrorAction SilentlyContinue
      if ($m) { return $true }
    }
    Start-Sleep 2
  }
  return $false
}

if (-not $SkipInfra) {
  Write-Host "[1/4] infra (postgres/redis/rabbitmq/prometheus/grafana)..."
  docker compose up -d postgres redis rabbitmq prometheus grafana mailpit *>&1 | Tee-Object "$root\logs\infra.log"
  Write-Host "  waiting infra ports (postgres 5432, rabbit 5672, redis 6379)..."
  Wait-Port 5432 | Out-Null
  Wait-Port 5672 | Out-Null
  Wait-Port 6379 | Out-Null
}

Write-Host "[2/4] build..."
dotnet build VacationPlanner.sln *>&1 | Tee-Object "$root\logs\build.log"
if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED -> see logs\build.log"; exit 1 }

if ($BuildOnly) { Write-Host "Build ok -> logs\build.log"; exit 0 }

if (-not (Test-Path "$root\logs\migrations.ok")) {
  Write-Host "[3/5] migrations (first time)..."
  dotnet ef migrations add Init --project src/VacationPlanner.Infrastructure --startup-project src/VacationPlanner.Web *>&1 | Tee-Object "$root\logs\migrations.log"
  New-Item -ItemType File -Force -Path "$root\logs\migrations.ok" | Out-Null
}

Write-Host "[4/5] start Web -> logs\web.log, logs\web.err.log"
$web = Start-Process dotnet -ArgumentList @("run","--project","src/VacationPlanner.Web") `
       -RedirectStandardOutput "$root\logs\web.log" -RedirectStandardError "$root\logs\web.err.log" -PassThru -WindowStyle Hidden
Write-Host "[5/5] start Worker -> logs\worker.log, logs\worker.err.log"
$wrk = Start-Process dotnet -ArgumentList @("run","--project","src/VacationPlanner.Worker") `
       -RedirectStandardOutput "$root\logs\worker.log" -RedirectStandardError "$root\logs\worker.err.log" -PassThru -WindowStyle Hidden

"$($web.Id)" | Out-File "$root\logs\web.pid"
"$($wrk.Id)" | Out-File "$root\logs\worker.pid"

Write-Host "  waiting Rabbit bus (worker 'Bus started', up to 150s)..."
$busOk = Wait-Log "$root\logs\worker.log" "Bus started"
if (-not $busOk) { Write-Host "  WARN: worker bus not started yet - see logs\worker.log" }

Write-Host ""
Write-Host "Done:"
Write-Host "  API/Swagger : http://localhost:5000/swagger"
Write-Host "  Web  pid    : $($web.Id)   logs\web.log"
Write-Host "  Worker pid  : $($wrk.Id)   logs\worker.log"
Write-Host "Stop everything:  .\stop-dev.ps1"