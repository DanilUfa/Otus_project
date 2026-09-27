<#
    seed-leaves.ps1 - creates overlapping APPROVED leaves for employees 2,3,4
    so the day calendar shows spans and (red) overlap days.
    ASCII-only (PowerShell 5.1 safe). Output -> logs\seed-leaves.log
    Requires API running WITH the new Seed (run-dev.ps1 once) to have users 3,4.
#>
$ErrorActionPreference = 'Stop'
$log = Join-Path $PSScriptRoot 'logs\seed-leaves.log'
$b   = "http://localhost:5000"
function Note($m) { Add-Content -Path $log -Value $m; Write-Host $m }

"===== seed-leaves $(Get-Date -Format o) =====" | Add-Content $log

try {
  $mgr = (Invoke-RestMethod -Method Post -Uri "$b/api/auth/login" -ContentType "application/json" `
           -Body '{"email":"manager@vac.local","password":"Manager123!"}').token
  $mh  = @{ Authorization = "Bearer $($mgr)" }

  # uid, start, end - summer overlaps: Pet(2) x Maria(3) x Alex(4), plus single bar for Alex in May
  $plan = @(
    @{ uid = 2; start = '2026-06-01'; end = '2026-06-21' },
    @{ uid = 3; start = '2026-06-10'; end = '2026-06-24' },  # overlaps Petr 10..21
    @{ uid = 4; start = '2026-06-20'; end = '2026-07-03' },  # overlaps Maria 20..24
    @{ uid = 4; start = '2026-05-04'; end = '2026-05-12' }   # single bar in May
  )

  foreach ($p in $plan) {
    $body = @{ userId = $p.uid; type = 0; startDate = $p.start; endDate = $p.end; reason = "seed" } | ConvertTo-Json -Compress
    Invoke-RestMethod -Method Post -Uri "$b/api/leave" -Headers $mh -ContentType "application/json" -Body $body | Out-Null
    $id = (Invoke-RestMethod -Uri "$b/api/leave" -Headers $mh |`
             Where-Object { $_.userId -eq $p.uid -and $_.startDate -like "$($p.start)*" } |`
             Sort-Object id -Descending | Select-Object -First 1).id
    Invoke-RestMethod -Method Post -Uri "$b/api/leave/$id/approve" -Headers $mh | Out-Null
    Note "approved uid=$($p.uid) $($p.start)..$($p.end)  id=$id"
  }

  Note "seed-leaves DONE"
} catch {
  Note "seed-leaves FAILED: $($_.Exception.Message)"
  exit 1
}