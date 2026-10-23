; SnipeSync installer (Inno Setup 6). Build with installer\build.cmd, which publishes the dashboard
; self-contained into artifacts\publish and then compiles this script.
;
; Per-user install, no admin prompt. Settings and the audit database live in %LOCALAPPDATA%\SnipeSync,
; outside the install folder, so upgrades and uninstalls never touch them.
;
; Shortcuts point straight at the exe. Never route them through powershell.exe: endpoint protection on
; these machines silently blocks Explorer launching PowerShell (see SESSION_CONTEXT).

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName "SnipeSync"
#define AppExe "SnipeSync.Dashboard.exe"
#define NightlyTask "SnipeSync nightly sync"

[Setup]
AppId={{6F3C1B52-8E0B-4C3B-9B6E-2D6A1F2C7A41}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=DKB IT
AppComments=Keeps Snipe-IT in step with Entra ID and shows the audit trail.
DefaultDirName={localappdata}\Programs\{#AppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=output
OutputBaseFilename=SnipeSync-Setup-{#AppVersion}
SetupIconFile=..\dashboard\SnipeSync.Dashboard\snipesync.ico
WizardSmallImageFile=wizard-small.bmp
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2
SolidCompression=yes
; The dashboard host keeps running in the background after its window closes; let Restart Manager
; close it so an upgrade can replace the exe.
CloseApplications=force

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "nightly"; Description: "Run a full sync every night at 2:00 AM (replaces the old Azure timers)"; GroupDescription: "Automation:"; Flags: unchecked

[InstallDelete]
; The hand-published copy and shortcuts from before there was an installer.
Type: filesandordirs; Name: "{localappdata}\SnipeSyncDashboard"
Type: files; Name: "{autodesktop}\SnipeSync Dashboard.lnk"
Type: files; Name: "{autoprograms}\SnipeSync Dashboard.lnk"
; Bundles from earlier versions, so a stale cached page can't still load the old UI.
Type: filesandordirs; Name: "{app}\wwwroot"

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Open the SnipeSync dashboard"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Open the SnipeSync dashboard"; Tasks: desktopicon

[Run]
; A per-user task needs no elevation. /F replaces an existing one, so reinstalling is safe.
Filename: "{sys}\schtasks.exe"; Parameters: "/Create /F /SC DAILY /ST 02:00 /TN ""{#NightlyTask}"" /TR ""\""{app}\{#AppExe}\"" --sync"""; Flags: runhidden; Tasks: nightly; StatusMsg: "Scheduling the nightly sync..."
Filename: "{app}\{#AppExe}"; Description: "Open SnipeSync now"; Flags: nowait postinstall skipifsilent
; A self-update from the dashboard runs this silently with /RELAUNCH; bring the dashboard back, without
; a second window (the one that started the update reloads itself).
Filename: "{app}\{#AppExe}"; Parameters: "--after-update"; Flags: nowait; Check: ShouldRelaunch

[UninstallRun]
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /F /TN ""{#NightlyTask}"""; Flags: runhidden; RunOnceId: "RemoveNightlySync"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AppExe}"; Flags: runhidden; RunOnceId: "StopDashboard"

[Code]
// /RELAUNCH is passed only by the dashboard's self-update (see Updater.cs).
function ShouldRelaunch: Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/RELAUNCH') = 0 then
      Result := True;
end;

// Unticking the nightly task on a reinstall should remove a task an earlier install created.
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if (CurStep = ssPostInstall) and not WizardIsTaskSelected('nightly') then
    Exec(ExpandConstant('{sys}\schtasks.exe'), '/Delete /F /TN "{#NightlyTask}"', '', SW_HIDE,
      ewWaitUntilTerminated, ResultCode);
end;
