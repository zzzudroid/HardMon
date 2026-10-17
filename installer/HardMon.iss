; Установщик HardMon (Inno Setup 6). Собирается скриптом make-release.ps1, который передаёт /DAppVersion.
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{6F0D3B7E-4C1A-4B8F-9E5D-2A7C1E9B4D10}
AppName=HardMon
AppVersion={#AppVersion}
AppVerName=HardMon {#AppVersion}
AppPublisher=zzzudroid
AppPublisherURL=https://github.com/zzzudroid/HardMon
DefaultDirName={autopf}\HardMon
DefaultGroupName=HardMon
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\release
OutputBaseFilename=HardMon-Setup-{#AppVersion}
SetupIconFile=..\app.ico
UninstallDisplayIcon={app}\HardMon.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Запускать HardMon при входе в Windows"
Name: "pawnio"; Description: "Установить драйвер PawnIO (нужен для температуры ЦП)"; Check: not IsPawnIOInstalled
Name: "desktopicon"; Description: "Ярлык на рабочем столе"; Flags: unchecked

[Files]
Source: "..\release\app\HardMon.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "PawnIO_setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Tasks: pawnio

[Icons]
Name: "{group}\HardMon"; Filename: "{app}\HardMon.exe"
Name: "{group}\Удалить HardMon"; Filename: "{uninstallexe}"
Name: "{autodesktop}\HardMon"; Filename: "{app}\HardMon.exe"; Tasks: desktopicon

[Run]
Filename: "{tmp}\PawnIO_setup.exe"; Parameters: "-install"; StatusMsg: "Установка PawnIO..."; Tasks: pawnio
Filename: "{app}\HardMon.exe"; Parameters: "--autostart on"; StatusMsg: "Настройка автозапуска..."; Flags: runhidden; Tasks: autostart
Filename: "{app}\HardMon.exe"; Description: "Запустить HardMon"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM HardMon.exe /F"; Flags: runhidden; RunOnceId: "KillHardMon"
Filename: "{app}\HardMon.exe"; Parameters: "--autostart off"; Flags: runhidden; RunOnceId: "RemoveAutostart"

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\HardMon"

[Code]
function IsPawnIOInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO');
end;

// Закрываем запущенный виджет перед установкой/обновлением, иначе exe занят
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM HardMon.exe /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := '';
end;
