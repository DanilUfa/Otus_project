param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$Test
)

$ErrorActionPreference = 'Stop'
$oldPath = $env:PATH
$oldDotnetRoot = $env:DOTNET_ROOT
Push-Location $PSScriptRoot
try {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe')
        (Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)
    ) | Select-Object -Unique
    $dotnet = $null
    foreach ($candidate in $candidates) {
        if (-not (Test-Path -LiteralPath $candidate)) { continue }
        $sdks = & $candidate --list-sdks
        if ($sdks -match '^10\.0\.') { $dotnet = $candidate; break }
    }
    if (-not $dotnet) { throw 'Install .NET 10 SDK before building.' }
    $env:DOTNET_ROOT = Split-Path -Parent $dotnet
    $env:PATH = "$env:DOTNET_ROOT;$oldPath"
    Write-Host "Using $dotnet"
    & $dotnet --version
    if ($LASTEXITCODE -ne 0) { throw 'Cannot select .NET SDK.' }

    $client = Join-Path $PSScriptRoot 'src\VacationPlanner.Web\ClientApp'
    & npm --prefix $client ci
    if ($LASTEXITCODE -ne 0) { throw 'Frontend dependency installation failed.' }
    & npm --prefix $client run build
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
    & $dotnet build VacationPlanner.sln -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }

    if ($Test) {
        & $dotnet test VacationPlanner.sln -c $Configuration --no-build --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
        & $dotnet run --project tests\VacationPlanner.Smoke -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw 'Smoke tests failed.' }
    }
    Write-Host 'Build completed successfully.' -ForegroundColor Green
}
finally {
    $env:PATH = $oldPath
    $env:DOTNET_ROOT = $oldDotnetRoot
    Pop-Location
}
