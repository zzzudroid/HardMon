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

# Get-ScheduledTask вместо schtasks /Query: при Stop ошибка нативной команды «задача не найдена» роняет скрипт
if (Get-ScheduledTask -TaskName HardMon -ErrorAction SilentlyContinue) { Start-ScheduledTask -TaskName HardMon } else { Start-Process .\HardMon.exe }
