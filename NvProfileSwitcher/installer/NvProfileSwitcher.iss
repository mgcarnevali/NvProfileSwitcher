#define MyAppName "NvProfileSwitcher"
#define MyAppPublisher "Maximiliano Carnevali"
#define MyAppURL "https://github.com/mgcarnevali/NvProfileSwitcher"
#define MyAppExeName "NvProfileSwitcher.exe"

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0-dev"
#endif

[Setup]
AppId={{6A94327B-EC07-4F34-9887-8E7D2A8F55E8}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

DefaultDirName={autopf}\NvProfileSwitcher
DefaultGroupName=NvProfileSwitcher
DisableProgramGroupPage=yes

OutputDir=output
OutputBaseFilename=NvProfileSwitcher-Setup-v{#MyAppVersion}

SetupIconFile=..\assets\branding\NvProfileSwitcher.ico
WizardImageFile=..\assets\branding\installer-large.bmp
WizardSmallImageFile=..\assets\branding\installer-small.bmp
UninstallDisplayIcon={app}\NvProfileSwitcher.exe

Compression=lzma2
SolidCompression=yes
WizardStyle=modern

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

PrivilegesRequired=admin
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\NvProfileSwitcher.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\NvProfileSwitcher"; Filename: "{app}\NvProfileSwitcher.exe"
Name: "{autodesktop}\NvProfileSwitcher"; Filename: "{app}\NvProfileSwitcher.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\NvProfileSwitcher.exe"; Description: "Launch NvProfileSwitcher"; Flags: nowait postinstall skipifsilent
