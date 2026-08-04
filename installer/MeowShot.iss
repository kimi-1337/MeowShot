#define MyAppName "MeowShot"
#define MyAppVersion "0.1.3"
#define MyAppPublisher "kimi"
#define MyAppExeName "MeowShot.exe"

[Setup]
AppId={{2C4F1379-7CA3-45C1-9F5B-15A8FA73D8ED}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=MeowShot-Setup-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=MeowShot installer
VersionInfoProductName={#MyAppName}
SetupIconFile=..\src\MeowShot\Assets\meowshot.ico

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\MeowShot"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\MeowShot"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MeowShot"; ValueData: """{app}\{#MyAppExeName}"" --background"; Flags: uninsdeletevalue

[Tasks]
Name: "desktopicon"; Description: "Создать значок на рабочем столе"; GroupDescription: "Дополнительные значки:"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить MeowShot"; Flags: nowait postinstall skipifsilent
