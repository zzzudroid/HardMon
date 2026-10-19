# Сборка релиза: self-contained exe (без зависимости от .NET), упакованный в портативный zip. Установщика нет.
# Результат — в папке release\. Версия берётся из HardMon.csproj (<Version>).
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$version = ([xml](Get-Content HardMon.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'В HardMon.csproj не задан <Version>' }
Write-Host "Версия $version"

if (Test-Path release) { Remove-Item release -Recurse -Force }

# 1. Self-contained single-file exe
dotnet publish -c Release -r win-x64 --self-contained true -o release\app `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -v q
if (-not $?) { throw 'Сборка не удалась' }

# 2. Портативный zip: exe + краткая инструкция
$zip = "release\HardMon-$version-portable.zip"
Compress-Archive -Path release\app\HardMon.exe, portable\README.txt -DestinationPath $zip
Remove-Item release\app -Recurse -Force

Get-ChildItem release -File | Select-Object Name, @{ n = 'МБ'; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize
