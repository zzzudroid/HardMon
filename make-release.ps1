# Сборка релиза: self-contained exe (без зависимости от .NET), установщик Inno Setup и портативный zip.
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

# 2. Установщик PawnIO для включения в инсталлятор (скачивается один раз)
$pawn = 'installer\PawnIO_setup.exe'
if (-not (Test-Path $pawn)) {
    Invoke-WebRequest 'https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe' -OutFile $pawn
}

# 3. Установщик
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Не найден Inno Setup: winget install JRSoftware.InnoSetup' }
& $iscc /Q "/DAppVersion=$version" installer\HardMon.iss
if ($LASTEXITCODE -ne 0) { throw 'Сборка установщика не удалась' }

# 4. Портативный zip
$zip = "release\HardMon-$version-portable.zip"
Compress-Archive -Path release\app\HardMon.exe, installer\README.txt -DestinationPath $zip

Get-ChildItem release -File | Select-Object Name, @{ n = 'МБ'; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize
