<#
  End-to-end smoke test: login -> submit -> approve -> draft draft->submit->approve
  -> draft->cancel -> balance & metrics.
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

  # --- Draft lifecycle: draft -> submit -> approve ---
  $dfull = '{"userId":2,"type":0,"startDate":"2026-11-01","endDate":"2026-11-03","reason":"draft-cycle"}'
  $draftId = Invoke-RestMethod -Method Post -Uri "$b/api/leave/draft" -Headers @{Authorization="Bearer $emp"} -ContentType "application/json" -Body $dfull
  $dlist = Invoke-RestMethod -Uri "$b/api/leave" -Headers @{Authorization="Bearer $emp"}
  $d = $dlist | Where-Object { $_.id -eq $draftId }
  if (-not $d -or $d.status -ne 0) { throw "draft #$draftId status expected 0, got $($d.status)" }
  Note "draft #$draftId -> status $($d.status) (0=Draft): OK"

  Invoke-RestMethod -Method Post -Uri "$b/api/leave/$draftId/submit" -Headers @{Authorization="Bearer $emp"} | Out-Null
  $d = (Invoke-RestMethod -Uri "$b/api/leave" -Headers @{Authorization="Bearer $emp"}) | Where-Object { $_.id -eq $draftId }
  if ($d.status -ne 1) { throw "draft #$draftId submit status expected 1, got $($d.status)" }
  Note "draft #$draftId submit -> status $($d.status) (1=Submitted): OK"

  Invoke-RestMethod -Method Post -Uri "$b/api/leave/$draftId/approve" -Headers @{Authorization="Bearer $mgr"} | Out-Null
  $d = (Invoke-RestMethod -Uri "$b/api/leave" -Headers @{Authorization="Bearer $emp"}) | Where-Object { $_.id -eq $draftId }
  if ($d.status -ne 2) { throw "draft #$draftId approve status expected 2, got $($d.status)" }
  Note "draft #$draftId approve -> status $($d.status) (2=Approved): OK"

  # --- Draft cancel: draft -> cancelled ---
  $cb = '{"userId":2,"type":0,"startDate":"2026-12-01","endDate":"2026-12-02","reason":"draft-cancel"}'
  $cdraft = Invoke-RestMethod -Method Post -Uri "$b/api/leave/draft" -Headers @{Authorization="Bearer $emp"} -ContentType "application/json" -Body $cb
  Invoke-RestMethod -Method Post -Uri "$b/api/leave/$cdraft/cancel" -Headers @{Authorization="Bearer $emp"} | Out-Null
  $dc = (Invoke-RestMethod -Uri "$b/api/leave" -Headers @{Authorization="Bearer $emp"}) | Where-Object { $_.id -eq $cdraft }
  if ($dc.status -ne 4) { throw "draft #$cdraft cancel status expected 4, got $($dc.status)" }
  Note "draft #$cdraft cancel -> status $($dc.status) (4=Cancelled): OK"

  $bal = Invoke-RestMethod -Uri "$b/api/balance/2?year=2026" -Headers @{Authorization="Bearer $emp"}
  Note "balance 2026 remaining: $($bal.remaining)"

  $metrics = (Invoke-WebRequest "$b/metrics" -UseBasicParsing).Content
  ($metrics -split "`n" | Where-Object { $_ -match '^vacation_leaves' }) | ForEach-Object { Note $_ }

  Note "SMOKE DONE OK"
} catch {
  Note "SMOKE FAILED: $($_.Exception.Message)"
  exit 1
}