; QuickVoice installer (Inno Setup 6). Built by .github/workflows/release.yml:
;   ISCC /DAppVersion=1.1.0 /DSourceDir=<publish folder> installer\QuickVoice.iss
; Installs for the current user only (no admin), in %LOCALAPPDATA%\Programs\QuickVoice.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\QuickVoice"
#endif

[Setup]
AppId={{6F3B9A52-4C1E-4F7B-9D25-0B8E3C71A4D9}
AppName=QuickVoice
AppVersion={#AppVersion}
AppPublisher=theuslpszbr15
AppPublisherURL=https://github.com/theuslpszbr15/QuickVoice
AppSupportURL=https://github.com/theuslpszbr15/QuickVoice/issues
DefaultDirName={localappdata}\Programs\QuickVoice
DefaultGroupName=QuickVoice
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=QuickVoice-Setup-{#AppVersion}
SetupIconFile=..\src\QuickVoice\app.ico
UninstallDisplayIcon={app}\QuickVoice.exe
LicenseFile=..\LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\QuickVoice"; Filename: "{app}\QuickVoice.exe"
Name: "{autodesktop}\QuickVoice"; Filename: "{app}\QuickVoice.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\QuickVoice.exe"; Description: "{cm:LaunchProgram,QuickVoice}"; Flags: nowait postinstall skipifsilent
