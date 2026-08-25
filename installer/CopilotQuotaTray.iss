#ifndef AppVersion
  #define AppVersion "0.0.0-local"
#endif

#define AppName "Copilot Quota Tray"
#define AppExeName "CopilotQuotaTray.exe"
#define AppPublisher "ZacheryGlass"
#define AppUrl "https://github.com/ZacheryGlass/copilot-quota-tray"

[Setup]
AppId={{D5381338-BB5E-4B0C-A0B8-01F0D02450DD}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={localappdata}\Programs\CopilotQuotaTray
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
CloseApplicationsFilter={#AppExeName}
RestartApplications=no
OutputDir=..\artifacts
OutputBaseFilename=CopilotQuotaTray-Setup
UninstallDisplayIcon={app}\{#AppExeName}
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} installer
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autostartup}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AppExeName}"; Flags: runhidden; RunOnceId: "StopCopilotQuotaTray"

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\CopilotPace"
