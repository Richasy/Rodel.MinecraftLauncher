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

[Messages]
english.ConfirmUninstall=Are you sure you want to completely remove %1 and all of its components?%n%nRunning RMCL processes for this installation will be forcibly closed. Unsaved changes will be lost.
chinesesimplified.ConfirmUninstall=确定要完全卸载 %1 及其所有组件吗？%n%n此安装目录下正在运行的 RMCL 将被强制关闭，未保存的更改将丢失。
chinesetraditional.ConfirmUninstall=確定要完全解除安裝 %1 及其所有元件嗎？%n%n此安裝目錄下正在執行的 RMCL 將被強制關閉，未儲存的變更將遺失。

[CustomMessages]
english.UninstallStopFailed=RMCL could not be terminated. No program files have been removed. Close RMCL and retry uninstalling.
chinesesimplified.UninstallStopFailed=无法强制关闭 RMCL，尚未删除程序文件。请关闭 RMCL 后重试卸载。
chinesetraditional.UninstallStopFailed=無法強制關閉 RMCL，尚未刪除程式檔案。請關閉 RMCL 後重試解除安裝。

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PayloadDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "stop-installed-app.ps1"; DestDir: "{app}\Assets\Setup"; Flags: ignoreversion

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

function GetInstallationMutex(Param: String): String;
begin
  if InstallationMutex = '' then
    if not RegQueryStringValue(HKCU64, 'Software\Richasy\RMCL\Installations\{#AppArchitecture}', 'Mutex', InstallationMutex) then
      InstallationMutex := 'Local\Richasy.RMCL.' + GetSHA256OfUnicodeString(ExpandConstant('{tmp}'));
  Result := InstallationMutex;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
  Parameters: String;
begin
  { This step is after confirmation, but before Inno's blocking AppMutex check or any removal. }
  if CurUninstallStep <> usAppMutexCheck then
    Exit;
  Parameters := '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
    ExpandConstant('{app}\Assets\Setup\stop-installed-app.ps1') + '" -ExecutablePath "' +
    ExpandConstant('{app}\Rodel.MinecraftLauncher.App.exe') + '"';
  if not ExecWithNativeSysDir(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    RaiseException(CustomMessage('UninstallStopFailed'));
  if ResultCode <> 0 then
    RaiseException(CustomMessage('UninstallStopFailed'));
end;
