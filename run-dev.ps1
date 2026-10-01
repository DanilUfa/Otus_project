<#
  One-command dev env startup (microservices). Logs to .\logs\.
  Usage:
    .\run-dev.ps1             # infra + build + start 6 microservices
    .\run-dev.ps1 -BuildOnly  # infra + build only
#>
param([switch]$SkipInfra,[switch]$BuildOnly)

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root
New-Item -ItemType Directory -Force -Path "$root\logs" | Out-Null

function Wait-Port([int]$port,[int]$timeoutSec=90){
  $deadline=(Get-Date).AddSeconds($timeoutSec)
  while((Get-Date)-lt $deadline){
    $c=New-Object System.Net.Sockets.TcpClient
    try{ $iar=$c.BeginConnect('127.0.0.1',$port,$null,$null); if($iar.AsyncWaitHandle.WaitOne(1000)){ $c.EndConnect($iar); $c.Close(); return $true } }catch{}
    $c.Close(); Start-Sleep 1
  }
  return $false
}
function Wait-Log([string]$file,[string]$text,[int]$timeoutSec=150){
  $deadline=(Get-Date).AddSeconds($timeoutSec)
  while((Get-Date)-lt $deadline){
    if(Test-Path $file){
      try{ $fs=[System.IO.File]::Open($file,'Open','Read','ReadWrite')
        try{ $sr=New-Object System.IO.StreamReader($fs,[System.Text.Encoding]::UTF8); $content=$sr.ReadToEnd(); $sr.Dispose(); if($content -like "*$text*"){ return $true } }finally{ $fs.Dispose() } }catch{}
    }
    Start-Sleep 2
  }
  return $false
}

if(-not $SkipInfra){
  Write-Host "[1/6] infra..."
  docker compose up -d --no-build postgres redis rabbitmq prometheus grafana mailpit *>&1 | Tee-Object "$root\logs\infra.log"
  Write-Host "  waiting infra ports..."
  Wait-Port 5432|Out-Null; Wait-Port 5672|Out-Null; Wait-Port 6379|Out-Null
}

Write-Host "[2/6] build..."
dotnet build VacationPlanner.sln *>&1 | Tee-Object "$root\logs\build.log"
if($LASTEXITCODE -ne 0){ Write-Host "BUILD FAILED -> logs\build.log"; exit 1 }
if($BuildOnly){ Write-Host "Build ok -> logs\build.log"; exit 0 }

function Start-Task([string]$proj,[string]$log,[int]$port){
  if($port -ge 1000){ $env:ASPNETCORE_URLS="http://127.0.0.1:$port" } else { Remove-Item Env:\ASPNETCORE_URLS -ErrorAction SilentlyContinue }
  $p=Start-Process dotnet -ArgumentList @("run","--project","$root\src\$proj","--no-build") `
      -WorkingDirectory "$root\src\$proj" `
      -RedirectStandardOutput "$root\logs\$log.log" -RedirectStandardError "$root\logs\$log.err.log" `
      -PassThru -WindowStyle Hidden
  "$($p.Id)" | Out-File "$root\logs\$log.pid"
  if($port -lt 1000){ Write-Host "  $log (worker, pid $($p.Id))" } else { Write-Host "  $log -> http://127.0.0.1:$port (pid $($p.Id))" }
  return $p
}

Write-Host "[3/6] start services..."
$api      = Start-Task "VacationPlanner.Web"       "web"        5100
$audit    = Start-Task "VacationPlanner.Audit"     "audit"      5300
$report   = Start-Task "VacationPlanner.Reporting" "reporting"  5200
$notifier = Start-Task "VacationPlanner.Notifier"  "notifier"   0
$sched    = Start-Task "VacationPlanner.Scheduler" "scheduler"  0
$gateway  = Start-Task "VacationPlanner.Gateway"   "gateway"    5000

Write-Host "[4/6] wait for services up..."
Wait-Port 5100|Out-Null; Wait-Port 5200|Out-Null; Wait-Port 5300|Out-Null
$gw=Wait-Port 5000
if(-not $gw){ Write-Host "  WARN: gateway not up - see logs\gateway.log" }

Write-Host "[5/6] wait Rabbit bus (notifier)..."
$busOk=Wait-Log "$root\logs\notifier.log" "Bus started"
if(-not $busOk){ Write-Host "  WARN: notifier bus not seen - see logs\notifier.log" }

Write-Host "[6/6] done:"
Write-Host "  Gateway / UI : http://localhost:5000"
Write-Host "  Audit API    : http://localhost:5300/api/audit"
Write-Host "  Reporting    : http://localhost:5200"
Write-Host "  Mailpit UI   : http://localhost:8025"
Write-Host "  Grafana      : http://localhost:3000 (admin/admin)"
Write-Host "Stop:  .\stop-dev.ps1"