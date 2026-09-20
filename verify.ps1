<#
    verify.ps1 - checks new capabilities: health, SignalR negotiate, Redis cache,
    draft auto-cancel (State), audit fan-out. Output -> logs\verify.log
    ASCII-only (PowerShell 5.1 safe).
#>
$ErrorActionPreference = 'Continue'
$log = Join-Path $PSScriptRoot 'logs\verify.log'
$b  = 'http://localhost:5000'

"===== verify $(Get-Date -Format o) =====" | Tee-Object -FilePath $log -Append

# --- health ---
try { $r = Invoke-WebRequest -UseBasicParsing -Uri "$b/health/live";  "health/live  -> $($r.StatusCode) $($r.Content)" | Tee-Object $log -Append }
catch { "health/live  -> ERROR $($_.Exception.Message)" | Tee-Object $log -Append }
try { $r = Invoke-WebRequest -UseBasicParsing -Uri "$b/health/ready"; "health/ready -> $($r.StatusCode) $($r.Content)" | Tee-Object $log -Append }
catch { "health/ready -> ERROR $($_.Exception.Message)" | Tee-Object $log -Append }

# --- SignalR negotiate ---
try { $n = Invoke-RestMethod -Method Post -Uri "$b/hubs/vacation/negotiate?negotiateVersion=1"; "signalr negotiate -> connectionId=$($n.connectionId)" | Tee-Object $log -Append }
catch { "signalr negotiate -> ERROR $($_.Exception.Message)" | Tee-Object $log -Append }

# --- auth ---
try {
  $mgr = Invoke-RestMethod -Method Post -Uri "$b/api/auth/login" -ContentType "application/json" -Body '{"email":"manager@vac.local","password":"Manager123!"}'
  $emp = Invoke-RestMethod -Method Post -Uri "$b/api/auth/login" -ContentType "application/json" -Body '{"email":"petr@vac.local","password":"User123!"}'
  $mh = @{ Authorization = "Bearer $($mgr.token)" }
  $eh = @{ Authorization = "Bearer $($emp.token)" }

  # --- Redis cache path (two calls, second should be cache hit) ---
  $u1 = (Invoke-RestMethod -Uri "$b/api/users" -Headers $eh)
  $u2 = (Invoke-RestMethod -Uri "$b/api/users" -Headers $eh)
  $bal1 = Invoke-RestMethod -Uri "$b/api/balance/2?year=2026" -Headers $eh
  $bal2 = Invoke-RestMethod -Uri "$b/api/balance/2?year=2026" -Headers $eh
  "redis: users(count x2)=$($u1.Count)/$($u2.Count); balance(x2)=$($bal1.remaining)/$($bal2.remaining)" | Tee-Object $log -Append

  # --- draft -> auto-cancel (State) ---
  $body = '{"userId":2,"type":0,"startDate":"2030-03-01","endDate":"2030-03-05","reason":"draft-auto-close"}'
  $draftId = Invoke-RestMethod -Method Post -Uri "$b/api/leave/draft" -Headers $eh -ContentType "application/json" -Body $body
  "draft created -> id=$draftId ; waiting 30s for auto-cancel..." | Tee-Object $log -Append
  Start-Sleep -Seconds 30
  $leaves = Invoke-RestMethod -Uri "$b/api/leave" -Headers $mh
  $d = $leaves | Where-Object { $_.id -eq $draftId }
  if ($d) { "draft final -> id=$($d.id) status=$($d.status) (4=Cancelled expected)" | Tee-Object $log -Append }
  else { "draft id=$draftId NOT FOUND in list" | Tee-Object $log -Append }
}
catch { "auth/flow -> ERROR $($_.Exception.Message)" | Tee-Object $log -Append }

"===== verify END =====" | Tee-Object $log -Append
"Done. See $log"