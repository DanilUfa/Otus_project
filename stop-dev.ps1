<#
  Stops all microservices started via run-dev.ps1 (by logs\*.pid).
#>
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
foreach($name in @('web','audit','reporting','notifier','scheduler','gateway')){
  $pidFile="$root\logs\$name.pid"
  if(Test-Path $pidFile){
    $p=(Get-Content $pidFile|Select-Object -First 1).Trim()
    if($p -match '^\d+$'){ Write-Host "Stopping $name (pid $p)..." ; taskkill /PID $p /T /F 2>$null|Out-Null }
  }
}
Write-Host "Done. Reset queue if needed: docker compose restart rabbitmq"