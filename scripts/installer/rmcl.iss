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

[Run]
Filename: "{app}\Rodel.MinecraftLauncher.App.exe"; Description: "{cm:LaunchProgram,RMCL}"; Flags: nowait postinstall skipifsilent
