<#
  One-command Git init + first commit. Result is written to logs\git.log (the agent reads it).
  Robust against CRLF warnings and stderr noise from git.

  Usage:
    .\git.ps1                      # default commit message
    .\git.ps1 -Message "My msg"    # custom message
#>
param(
  [string]$Message = "Vacation Planner: full MVP (auth, MassTransit, SPA, metrics, emails)"
)

# НЕ останавливаться на ворнингах git в stderr (CRLF и т.п.)
$ErrorActionPreference = "Continue"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root
New-Item -ItemType Directory -Force -Path "$root\logs" | Out-Null
$log = "$root\logs\git.log"

function Note($m) { if ($m) { Add-Content -Path $log -Value ([string]$m) } }
"===== git $(Get-Date) =====" | Add-Content $log

# ---- найти git ----
$candidates = @(
  "C:\Program Files\Git\cmd\git.exe",
  "C:\Program Files (x86)\Git\cmd\git.exe",
  "$env:LOCALAPPDATA\Programs\Git\cmd\git.exe"
)
$gitExe = $null
foreach ($c in $candidates) { if (Test-Path $c) { $gitExe = $c; break } }
if (-not $gitExe) { $g = Get-Command git -ErrorAction SilentlyContinue; if ($g) { $gitExe = $g.Source } }

if (-not $gitExe) {
  Note "GIT NOT INSTALLED"
  Write-Host "Git не найден. Поставь: winget install --id Git.Git --source winget"
  exit 1
}
Note "git: $gitExe"
$gitDir = Split-Path -Parent $gitExe
if ($env:PATH -notlike "*$gitDir*") { $env:PATH = "$gitDir;$env:PATH" }

# ---- настройки ----
& $gitExe config core.autocrlf false  2>$null   # убрать CRLF-ворнинги
& $gitExe config core.safecrlf false  2>$null
if (-not (& $gitExe config user.name))  { & $gitExe config user.name  "Vacation Planner Dev" }
if (-not (& $gitExe config user.email)) { & $gitExe config user.email "dev@vac.local" }

# ---- репозиторий + первый коммит ----
if (-not (Test-Path "$root\.git")) {
  Note "init: $( (& $gitExe init -b main 2>&1 | Out-String).Trim() )"
}
& $gitExe add -A 2>&1 | ForEach-Object { Note "add:  $_" }
Note "commit: $( (& $gitExe commit -m "$Message" 2>&1 | Out-String).Trim() )"

$logLine = (& $gitExe log --oneline -1 2>&1 | Out-String).Trim()
Note "HEAD: $logLine"
Write-Host "HEAD: $logLine"
Write-Host "Done. Full output in logs\git.log"