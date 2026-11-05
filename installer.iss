; ============================================================
;  Skrip installer Photo Booth (Inno Setup 6.3 atau lebih baru)
;  Taruh file ini di folder proyek (sejajar dengan app.ico).
; ============================================================

#define MyAppName      "Photo Booth"
#define MyAppVersion   "1.0.0"
#define MyAppPublisher "Alfian Mahendra Ifandia"
#define MyAppExeName   "PhotoBooth.exe"

; Folder hasil "dotnet publish -o publish" (relatif terhadap file .iss ini).
; Kalau hasil publish kamu ada di tempat lain, ganti, misalnya: "D:\PhotoBooth-publish"
#define SourceDir      "publish"

[Setup]
AppId={{8B212B6A-9C13-4D66-A71E-908BADA2AE53}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=installer-output
OutputBaseFilename=PhotoBooth-Setup-{#MyAppVersion}
SetupIconFile=app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Install per-pengguna tanpa admin; pengguna bisa memilih "untuk semua pengguna" lewat dialog
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
