; Inno Setup script for Session Recorder.
; Build with:  .\scripts\build-installer.ps1      (publishes, then compiles this)
;
; PER-USER install (PrivilegesRequired=lowest, under %LocalAppData%\Programs), so a
; non-technical user never sees an admin prompt, and the recorded Chrome runs as them.

#define AppName        "Session Recorder"
#define AppVersion     "1.1.0"
#define AppPublisher   "iSoutien"
#define AppCopyright   "Copyright (c) 2026 Moad Dahbi"
#define AppExeName     "SessionRecorder.exe"
; Derived from AppVersion so bumping the version cannot leave a stale icon name.
#define AppIconName    "SessionRecorder-" + AppVersion + ".ico"
#define SourceDir      "..\publish"

[Setup]
AppId={{3E8A5C21-7B4D-4F96-A1C3-9D2E6F0B7A58}
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

DefaultDirName={autopf}\SessionRecorder
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto

; No admin prompt: everything lands in the current user's profile.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

OutputDir=..\dist
OutputBaseFilename=SessionRecorder-Setup-{#AppVersion}
SetupIconFile=..\src\Assets\session-recorder.ico
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
Source: "..\src\Assets\session-recorder.ico"; DestDir: "{app}"; DestName: "{#AppIconName}"; Flags: ignoreversion

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
procedure StopSessionRecorder;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /T /IM SessionRecorder.exe', '',
       SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1500);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopSessionRecorder;
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  StopSessionRecorder;
  Result := True;
end;

// Uninstall leaves nothing behind: the log and the recording browser's profile (which
// holds the site logins) live in %LocalAppData%\SessionRecorder. Recordings already
// saved in Downloads belong to the user and are kept.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\SessionRecorder');
    if DirExists(DataDir) then
      DelTree(DataDir, True, True, True);
  end;
end;
