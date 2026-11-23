param([switch]$Auto, [switch]$Check)

$ErrorActionPreference = 'Stop'
$OutputEncoding = New-Object System.Text.UTF8Encoding($false)
[Console]::OutputEncoding = $OutputEncoding
$root = $PSScriptRoot
$baseUrl = 'http://localhost:5000'
$composeFile = Join-Path $root 'demo-importer.compose.yml'
$stateDir = Join-Path $root '.local-demo\importer'
$docker = (Get-Command docker -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)
if (-not $docker) { $docker = Join-Path $env:LOCALAPPDATA 'Programs\DockerDesktop\resources\bin\docker.exe' }
$transcribing = $false
$previousEnvironment = @{}
foreach ($name in 'IMPORTER_DEMO_DB_PASSWORD','IMPORTER_DEMO_TOKEN','IMPORTER_DEMO_INPUT_DIR') {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

function Invoke-Docker([string[]]$Command, [int]$ExpectedCode = 0, [switch]$Quiet) {
    # Windows PowerShell 5.1 may treat Docker progress on stderr as a terminating error.
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $lines = @(& $docker @Command 2>&1 | ForEach-Object {
            $text = $_.ToString()
            if ($text -ne 'System.Management.Automation.RemoteException') { $text }
        })
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previousErrorAction }
    if (-not $Quiet -or $code -ne $ExpectedCode) {
        foreach ($line in $lines) { Write-Host $line }
    }
    if ($code -ne $ExpectedCode) {
        throw "Docker/импортёр: код $code, ожидался $ExpectedCode. См. вывод выше."
    }
    return ($lines -join "`n")
}

function Invoke-Importer([string[]]$Command, [int]$ExpectedCode = 0, [switch]$Quiet) {
    return Invoke-Docker -Command (@('compose','-p','vacation-importer-demo','-f',$composeFile,
        'run','--rm','--no-deps','-T','importer') + $Command) -ExpectedCode $ExpectedCode -Quiet:$Quiet
}

function Stage([string]$Title, [string]$Explanation) {
    Write-Host "`n============================================================" -ForegroundColor Cyan
    Write-Host $Title -ForegroundColor Cyan
    Write-Host $Explanation
    if (-not $Auto) { $null = Read-Host 'Нажмите Enter, чтобы выполнить этот шаг' }
}

function Login-Employee {
    $script:employee = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/auth/login" `
        -ContentType 'application/json' -Body '{"email":"petr@vac.local","password":"User123!"}'
    $env:IMPORTER_DEMO_TOKEN = $script:employee.token
    $script:headers = @{ Authorization = "Bearer $($script:employee.token)" }
}

function Import-Sample([string]$Name, [int]$ExpectedCode, [string]$ExpectedSummary) {
    Write-Host "`nCSV: $Name" -ForegroundColor Yellow
    Get-Content -LiteralPath (Join-Path $runDir $Name) | ForEach-Object { Write-Host $_ }
    Login-Employee
    $output = Invoke-Importer -Command @('import','--file',"/input/$Name") -ExpectedCode $ExpectedCode
    if ($ExpectedSummary) {
        $match = [regex]::Match($output, 'JobId=(\d+) Status=' + [regex]::Escape($ExpectedSummary) + '\b')
        if (-not $match.Success) { throw "Неожиданный результат $Name. Ожидалось: $ExpectedSummary" }
        return [int]$match.Groups[1].Value
    }
}

try {
    if (-not (Test-Path -LiteralPath $docker)) { throw 'Docker не найден. Запустите Docker Desktop.' }
    $null = Invoke-Docker -Command @('info','--format','Docker Engine: {{.ServerVersion}}')
    $null = Invoke-Docker -Command @('image','inspect','vacation-importer:latest','--format','Importer image: {{.Id}}')
    try { $ready = Invoke-RestMethod "$baseUrl/health/ready" -TimeoutSec 10 } catch {
        throw "Приложение не запущено. В $root выполните docker compose up -d --no-build."
    }
    if ($ready.status -ne 'Healthy') { throw 'Основное приложение пока не готово.' }
    if ($Check) {
        Write-Host 'Проверка пройдена: Docker, образ импортёра и приложение готовы.' -ForegroundColor Green
        exit 0
    }

    $null = New-Item -ItemType Directory -Force -Path $stateDir
    $passwordFile = Join-Path $stateDir 'db-password.xml'
    if (-not (Test-Path -LiteralPath $passwordFile)) {
        $generated = [guid]::NewGuid().ToString('N') + [guid]::NewGuid().ToString('N')
        ConvertTo-SecureString $generated -AsPlainText -Force | Export-Clixml -LiteralPath $passwordFile
        $generated = $null
    }
    $secret = Import-Clixml -LiteralPath $passwordFile
    $env:IMPORTER_DEMO_DB_PASSWORD = [System.Net.NetworkCredential]::new('', $secret).Password
    $label = 'import-demo-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,6)
    $runDir = Join-Path $stateDir "runs\$label"
    $null = New-Item -ItemType Directory -Force -Path $runDir
    $env:IMPORTER_DEMO_INPUT_DIR = $runDir
    Start-Transcript -Path (Join-Path $runDir 'demo.log') | Out-Null
    $transcribing = $true

    Write-Host "Демонстрация импортёра. Приложение: $baseUrl" -ForegroundColor Green
    Write-Host 'Будут созданы три демонстрационные заявки Петра. База импортёра отдельная.'
    Write-Host "Метка заявок: $label"
    $null = Invoke-Docker -Command @('compose','-p','vacation-importer-demo','-f',$composeFile,'up','-d','--wait','db')
    $null = Invoke-Importer -Command @('migrate')
    Login-Employee
    $existing = Invoke-RestMethod "$baseUrl/api/leave" -Headers $headers
    $start = [datetime]::Today.AddDays(1)
    foreach ($leave in @($existing | Where-Object { $_.userId -eq $employee.userId -and $_.status -in @(1,2) } |
        Sort-Object { [datetime]$_.startDate })) {
        if (([datetime]$leave.endDate).Date -ge $start -and ([datetime]$leave.startDate).Date -le $start.AddDays(2)) {
            $start = ([datetime]$leave.endDate).Date.AddDays(1)
        }
    }
    $dates = @(0..2 | ForEach-Object { $start.AddDays($_).ToString('yyyy-MM-dd') })
    $userId = $employee.userId
    $header = 'RequestId,UserId,Type,StartDate,EndDate,Reason'
    @($header,
        "$([guid]::NewGuid()),$userId,Sick,$($dates[0]),$($dates[0]),$label-A",
        "$([guid]::NewGuid()),$userId,Unpaid,$($dates[1]),$($dates[1]),$label-B") |
        Set-Content -LiteralPath (Join-Path $runDir 'valid.csv') -Encoding ASCII
    @($header,
        "$([guid]::NewGuid()),$userId,Unpaid,$($dates[2]),$($dates[2]),$label-C",
        "$([guid]::NewGuid()),$userId,Sick,2030-02-30,$($dates[2]),invalid-date",
        "$([guid]::NewGuid()),$userId,Sick,$($dates[2]),$($dates[2]),overlap") |
        Set-Content -LiteralPath (Join-Path $runDir 'mixed.csv') -Encoding ASCII
    'wrong,header' | Set-Content -LiteralPath (Join-Path $runDir 'invalid.csv') -Encoding ASCII

    if (-not $Auto) {
        Start-Process $baseUrl
        Write-Host 'Войдите на сайте: petr@vac.local / User123! Откройте «Мои заявки».'
    }
    Stage '1. Успешный импорт' 'Две корректные строки CSV должны создать две заявки через API. Ожидаем Imported=2, Errors=0.'
    $successJob = Import-Sample 'valid.csv' 0 'DoneSuccessfully Total=2 Imported=2 Errors=0'
    $null = Invoke-Importer -Command @('dispatches','--job',"$successJob")
    Write-Host 'В dispatches показаны UUID строк и ID созданных заявок. Обновите список заявок на сайте.'

    Stage '2. Частичный успех и ошибки строк' 'Первая строка корректная, во второй неверная дата, третья пересекается с первой. Ожидаем Imported=1, Errors=2.'
    $mixedJob = Import-Sample 'mixed.csv' 3 'DoneWithErrors Total=3 Imported=1 Errors=2'
    $null = Invoke-Importer -Command @('errors','--job',"$mixedJob")
    $null = Invoke-Importer -Command @('dispatches','--job',"$mixedJob")

    Stage '3. Повторная отправка' 'Отправляем тот же valid.csv с прежними RequestId. Импортёр должен обнаружить Duplicate и не создать новых заявок.'
    $repeatJob = Import-Sample 'valid.csv' 3 'DoneWithErrors Total=2 Imported=0 Errors=2'
    $null = Invoke-Importer -Command @('errors','--job',"$repeatJob")

    Stage '4. Проверка структуры CSV' 'В файле неверный заголовок. Preflight должен отклонить его до создания задачи импорта.'
    $beforeJobs = Invoke-Importer -Command @('list') -Quiet
    $null = Import-Sample 'invalid.csv' 2 ''
    $afterJobs = Invoke-Importer -Command @('list') -Quiet
    $beforeJobRows = ($beforeJobs -split '\r?\n' | Where-Object { $_ -match '^JobId=' }) -join "`n"
    $afterJobRows = ($afterJobs -split '\r?\n' | Where-Object { $_ -match '^JobId=' }) -join "`n"
    if ($beforeJobRows -ne $afterJobRows) { throw 'Некорректный файл неожиданно изменил список задач.' }

    Stage '5. Сверка с приложением' 'Проверяем, что четыре сценария создали ровно три заявки. История задач и ошибки хранятся в PostgreSQL импортёра.'
    Login-Employee
    $after = Invoke-RestMethod "$baseUrl/api/leave" -Headers $headers
    $created = @($after | Where-Object { $_.reason -like "$label-*" })
    if ($created.Count -ne 3) { throw "Получено заявок: $($created.Count), ожидалось 3." }
    $created | Select-Object id,startDate,endDate,status,reason | Format-Table -AutoSize | Out-Host
    foreach ($id in @($successJob, $mixedJob, $repeatJob)) {
        $null = Invoke-Importer -Command @('status','--job',"$id")
    }
    @{ result='passed'; label=$label; createdLeaveIds=@($created.id); successJob=$successJob;
        mixedJob=$mixedJob; repeatJob=$repeatJob; inputDirectory=$runDir } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runDir 'result.json') -Encoding UTF8
    Write-Host "`nДЕМОНСТРАЦИЯ ПРОЙДЕНА. Создано ровно 3 заявки." -ForegroundColor Green
    Write-Host "CSV, отчёт и журнал: $runDir"
    Write-Host 'Повторный запуск подберёт новые свободные даты и сохранит предыдущие результаты.'
    Write-Host 'В коде: Program.cs -> ImportRunner -> CsvSnapshot -> RowPipeline -> LeaveApi / JobStore.'
} catch {
    Write-Host "`nОШИБКА: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
} finally {
    if ($transcribing) { Stop-Transcript | Out-Null }
    foreach ($name in $previousEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
    }
}
