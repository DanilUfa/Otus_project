<#
  Установка окружения для проекта «Планирование отпусков»:
  - .NET 10 SDK
  - Docker Desktop (для docker compose)

  Запуск (в PowerShell, лучше от имени администратора):
      .\setup-dev.ps1
#>

$ErrorActionPreference = "Stop"

Write-Host "==> 1/5 Проверяем winget..." -ForegroundColor Cyan
if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
    Write-Host "winget не найден. Установите App Installer из Microsoft Store и повторите." -ForegroundColor Red
    exit 1
}

Write-Host "==> 2/5 Устанавливаем .NET 10 SDK..." -ForegroundColor Cyan
winget install --id Microsoft.DotNet.SDK.10 -e --accept-source-agreements --accept-package-agreements --silent

Write-Host "==> 3/5 Устанавливаем Docker Desktop..." -ForegroundColor Cyan
winget install --id Docker.DockerDesktop -e --accept-source-agreements --accept-package-agreements --silent

Write-Host "==> 4/5 Проверяем установку..." -ForegroundColor Cyan
Start-Sleep -Seconds 5

if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    dotnet --version
    dotnet --list-sdks
} else {
    Write-Host "dotnet не в PATH — возможно, нужно переоткрыть терминал или перезапустить сессию." -ForegroundColor Yellow
}

if (Get-Command docker -ErrorAction SilentlyContinue) {
    docker --version
} else {
    Write-Host "docker не в PATH — перезапустите терминал. Docker Desktop может потребовать первый запуск/WSL2." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "==> 5/5 Готово. Дальше:" -ForegroundColor Green
Write-Host "  1) Запустите Docker Desktop один раз и дождитесь 'Engine running'."
Write-Host "  2) Из корня проекта C:\Otus выполните:"
Write-Host "       docker compose up -d --build"
Write-Host "  3) Откройте Swagger: http://localhost:5000/swagger"
Write-Host ""
Write-Host "Если winget требует прав администратора — перезапустите терминал 'От имени администратора'." -ForegroundColor Green