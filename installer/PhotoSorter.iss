; Inno Setup script for PhotoSorterApp
; Build via GitHub Actions using iscc.exe

#define AppName "PhotoSorter"
#define AppExeName "PhotoSorterApp.exe"
#define AppPublisher "Kvant2501"

; App version is passed from CI (/DAppVersion=...)
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{E8C2E4B8-7B21-4A1D-96F6-2A3A02C349E6}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputBaseFilename={#AppName}_Setup_{#AppVersion}_x64
Compression=lzma
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать значок на рабочем столе"; GroupDescription: "Дополнительно:"; Flags: unchecked

[Files]
; PublishDir is passed from CI (/DPublishDir=...)
; Copies everything from dotnet publish output to the install folder.
Source: "{#PublishDir}\\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\\{#AppName}"; Filename: "{app}\\{#AppExeName}"
Name: "{autodesktop}\\{#AppName}"; Filename: "{app}\\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\\{#AppExeName}"; Description: "Запустить {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
// Require .NET 8 Desktop Runtime (WindowsDesktop) x64.
// Strategy: check shared runtime folder for Microsoft.WindowsDesktop.App\\8.*
// If missing, show message with official download link.

function RuntimePathExists(): Boolean;
var
  basePath: string;
  findPath: string;
begin
  // On x64 Windows, .NET shared runtimes are typically in: %ProgramFiles%\dotnet\shared\Microsoft.WindowsDesktop.App\8.*
  basePath := ExpandConstant('{pf64}') + '\\dotnet\\shared\\Microsoft.WindowsDesktop.App';
  findPath := basePath + '\\8.*';

  Result := DirExists(basePath) and (FindFirst(findPath, FindRec) );
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  // If runtime is present, proceed.
  if RuntimePathExists() then
  begin
    Result := True;
    exit;
  end;

  MsgBox(
    'Для запуска требуется .NET 8 Desktop Runtime (x64).'+#13#10+#13#10+
    'Пожалуйста, установите его и запустите установщик снова.'+#13#10+#13#10+
    'Официальная страница загрузки откроется в браузере.',
    mbInformation, MB_OK);

  ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/8.0/runtime', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
  Result := False;
end;
