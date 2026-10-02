; Inno Setup script for SessionLens.
; Build with:  .\scripts\build-installer.ps1      (publishes, then compiles this)
;
; PER-USER install (PrivilegesRequired=lowest, under %LocalAppData%\Programs), so a
; non-technical user never sees an admin prompt, and the recorded Chrome runs as them.

#define AppName        "SessionLens"
#define AppVersion     "1.0.0"
#define AppPublisher   "iSoutien"
#define AppCopyright   "Copyright (c) 2026 Moad Dahbi"
#define AppExeName     "SessionLens.exe"
; Derived from AppVersion so bumping the version cannot leave a stale icon name.
#define AppIconName    "SessionLens-" + AppVersion + ".ico"
#define SourceDir      "..\publish"

[Setup]
AppId={{9A8D8056-C3FA-4904-8557-6BF79DC61D61}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppCopyright={#AppCopyright}
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoCopyright={#AppCopyright}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup

DefaultDirName={autopf}\SessionLens
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto

; No admin prompt: everything lands in the current user's profile.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

OutputDir=..\dist
OutputBaseFilename=SessionLens-Setup-{#AppVersion}
SetupIconFile=..\src\Assets\sessionlens.ico
UninstallDisplayIcon={app}\{#AppIconName}
WizardStyle=modern

; The payload is a self-contained .NET runtime plus the Playwright driver.
Compression=lzma2/max
SolidCompression=yes

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
; The whole self-contained publish folder, including the hidden .playwright driver folder.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; A versioned path prevents Explorer from reusing a cached icon from an older release.
Source: "..\src\Assets\sessionlens.ico"; DestDir: "{app}"; DestName: "{#AppIconName}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppIconName}"
Name: "{autodesktop}\{#AppName}";  Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppIconName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Start {#AppName} now"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
// The app runs the Playwright driver (node.exe from {app}\.playwright) as a child
// process; both lock files under {app}. /T ends the whole tree.
procedure StopSessionLens;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /T /IM SessionLens.exe', '',
       SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1500);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopSessionLens;
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  StopSessionLens;
  Result := True;
end;

// Uninstall leaves nothing behind: the log and the recording browser's profile (which
// holds the site logins) live in %LocalAppData%\SessionLens. Recordings already
// saved in Downloads belong to the user and are kept.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\SessionLens');
    if DirExists(DataDir) then
      DelTree(DataDir, True, True, True);
  end;
end;
