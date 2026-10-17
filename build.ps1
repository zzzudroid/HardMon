# Сборка HardMon в один exe и копирование в корень проекта.
# Перед сборкой закрывает запущенный виджет (запрос UAC), после — запускает снова через задачу автозапуска.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (Get-Process HardMon -ErrorAction SilentlyContinue) {
    Start-Process taskkill.exe -ArgumentList '/IM HardMon.exe /F' -Verb RunAs -Wait -WindowStyle Hidden
    Start-Sleep 1
}

dotnet publish -c Release -o dist -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --self-contained false -v q
if (-not $?) { throw 'Сборка не удалась' }

Copy-Item dist\HardMon.exe .\HardMon.exe -Force
Remove-Item dist -Recurse -Force
Write-Host 'Готово: HardMon.exe в корне проекта'

schtasks /Query /TN HardMon *> $null
if ($LASTEXITCODE -eq 0) { schtasks /Run /TN HardMon | Out-Null } else { Start-Process .\HardMon.exe }
