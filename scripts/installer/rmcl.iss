; Current-user EXE distribution. Player data and app preferences live outside {app}.
#ifndef PayloadDirectory
  #error PayloadDirectory must point to a complete unpackaged NativeAOT publish.
#endif
#ifndef AppVersion
  #error AppVersion is required.
#endif
#ifndef AppArchitecture
  #define AppArchitecture "x64"
#endif

[Setup]
AppId=Richasy.RMCL.Desktop.{#AppArchitecture}
AppMutex={code:GetInstallationMutex}
AppName=RMCL
AppVersion={#AppVersion}
AppPublisher=Richasy
AppPublisherURL=https://rmcl.richasy.net
AppSupportURL=https://github.com/Richasy/Rodel.MinecraftLauncher/issues
DefaultDirName={localappdata}\Programs\RMCL-{#AppArchitecture}
DefaultGroupName=RMCL
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=10.0.19041
#if AppArchitecture == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
OutputBaseFilename=RMCL-{#AppVersion}-win-{#AppArchitecture}-setup
SetupIconFile=..\..\src\Rodel.MinecraftLauncher.App\app.ico
UninstallDisplayIcon={app}\Rodel.MinecraftLauncher.App.exe
Compression=lzma2/normal
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "chinesetraditional"; MessagesFile: "compiler:Languages\ChineseTraditional.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PayloadDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\RMCL"; Filename: "{app}\Rodel.MinecraftLauncher.App.exe"; Check: not WizardNoIcons
Name: "{userdesktop}\RMCL"; Filename: "{app}\Rodel.MinecraftLauncher.App.exe"; Tasks: desktopicon

[Registry]
Root: HKCU64; Subkey: "Software\Richasy\RMCL\Installations\{#AppArchitecture}"; ValueType: string; ValueName: "Path"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKCU64; Subkey: "Software\Richasy\RMCL\Installations\{#AppArchitecture}"; ValueType: string; ValueName: "Mutex"; ValueData: "{code:GetInstallationMutex}"

[Run]
Filename: "{app}\Rodel.MinecraftLauncher.App.exe"; Description: "{cm:LaunchProgram,RMCL}"; Flags: nowait postinstall skipifsilent

[Code]
var
  InstallationMutex: String;

function OpenEvent(DesiredAccess: LongWord; InheritHandle: Boolean; Name: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(Event: THandle): Boolean;
  external 'SetEvent@kernel32.dll stdcall';
function ResetEvent(Event: THandle): Boolean;
  external 'ResetEvent@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function GetInstallationMutex(Param: String): String;
begin
  if InstallationMutex = '' then
    if not RegQueryStringValue(HKCU64, 'Software\Richasy\RMCL\Installations\{#AppArchitecture}', 'Mutex', InstallationMutex) then
      InstallationMutex := 'Local\Richasy.RMCL.' + GetSHA256OfUnicodeString(ExpandConstant('{tmp}'));
  Result := InstallationMutex;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Event: THandle;
  Attempts: Integer;
begin
  { This step is after confirmation, but before Inno's blocking AppMutex check or any removal. }
  if CurUninstallStep <> usAppMutexCheck then
    Exit;
  Event := OpenEvent($0002, False, GetInstallationMutex('') + '.Close');
  if Event = 0 then
    Exit;
  try
    SetEvent(Event);
    Sleep(500);
    ResetEvent(Event);
    Attempts := 0;
    while CheckForMutexes(GetInstallationMutex('')) and (Attempts < 100) do begin
      Sleep(100);
      Attempts := Attempts + 1;
    end;
  finally
    CloseHandle(Event);
  end;
end;
