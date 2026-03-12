#define MyAppName "MicVolumeFixer"
#define MyAppExeName "MicVolumeFixer.exe"
#define MyAppPublisher "MicVolumeFixer"
#define MyAppURL "https://github.com"

[Setup]
AppId={{B8F3A2E1-7C4D-4E9A-B5F6-1A2B3C4D5E6F}
AppName={#MyAppName}
AppVersion={#GetEnv('GITHUB_REF_NAME')}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=MicVolumeFixer-{#GetEnv('GITHUB_REF_NAME')}-Setup-arm64
SetupIconFile=..\microphone.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\bin\Release\net10.0-windows\win-arm64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Registry]
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueName: "MicVolumeFixer"; Flags: uninsdeletevalue

[UninstallDelete]
Type: files; Name: "{app}\settings.json"
