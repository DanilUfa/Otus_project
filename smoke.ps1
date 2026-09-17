<#
  End-to-end smoke test: login -> submit -> approve -> balance & metrics.
  Result is written to logs\smoke.log (the agent reads it; nothing to copy).

  Requires API running (run-dev.ps1) and fresh DB (manager id=1, employee id=2).
#>
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
New-Item -ItemType Directory -Force -Path "$root\logs" | Out-Null
$log = "$root\logs\smoke.log"
$b   = "http://localhost:5000"

function Note($m) { Add-Content -Path $log -Value $m; Write-Host $m }

"===== smoke $(Get-Date) =====" | Add-Content $log

try {
  $emp  = (Invoke-RestMethod -Method Post -Uri "$b/api/auth/login" -ContentType "application/json" -Body '{"email":"petr@vac.local","password":"User123!"}').token
  Note "login employee: OK"

  Invoke-RestMethod -Method Post -Uri "$b/api/leave" -ContentType "application/json" `
      -Headers @{Authorization="Bearer $emp"} `
      -Body '{"userId":2,"type":0,"startDate":"2026-09-01","endDate":"2026-09-07","reason":"smoke"}' | Out-Null
  Note "submit leave: OK"

  $list = Invoke-RestMethod -Uri "$b/api/leave" -Headers @{Authorization="Bearer $emp"}
  $newId = ($list | Sort-Object id -Descending | Select-Object -First 1).id
  Note "latest request id: $newId"

  $mgr = (Invoke-RestMethod -Method Post -Uri "$b/api/auth/login" -ContentType "application/json" -Body '{"email":"manager@vac.local","password":"Manager123!"}').token
  Invoke-RestMethod -Method Post -Uri "$b/api/leave/$newId/approve" -Headers @{Authorization="Bearer $mgr"} | Out-Null
  Note "approve #${newId}: OK"

  $bal = Invoke-RestMethod -Uri "$b/api/balance/2?year=2026" -Headers @{Authorization="Bearer $emp"}
  Note "balance 2026 remaining: $($bal.remaining)"

  $metrics = (Invoke-WebRequest "$b/metrics" -UseBasicParsing).Content
  ($metrics -split "`n" | Where-Object { $_ -match '^vacation_leaves' }) | ForEach-Object { Note $_ }

  Note "SMOKE DONE OK"
} catch {
  Note "SMOKE FAILED: $($_.Exception.Message)"
  exit 1
}