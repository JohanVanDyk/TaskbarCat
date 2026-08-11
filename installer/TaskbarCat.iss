; Inno Setup script for Taskbar Cat.
;
; Build:  tools\build_installer.ps1     (publishes first, then compiles this)
;
; Deliberately a PER-USER install into %LOCALAPPDATA%\Programs. That is not laziness:
;   - No UAC prompt, so installing is one fewer scary dialog on top of the SmartScreen one.
;   - The cat writes its autostart entry to HKCU\...\Run and its state to %APPDATA%. A
;     machine-wide install would put the exe somewhere a standard user cannot update while
;     leaving both of those per-user anyway.
;   - Nothing here needs admin. Asking for it invites the question of why.

#define AppName        "Taskbar Cat"
#define AppExe         "TaskbarCat.exe"
#define AppPublisher   "Johan van Dyk"
#define AppUrl         "https://github.com/JohanVanDyk/TaskbarCat"
#define SourceDir      "..\dist\TaskbarCat"

; Read the version straight off the built exe so the installer can never disagree with it.
#define AppVersion GetVersionNumbersString(SourceDir + "\" + AppExe)

[Setup]
; Never change AppId — it is what lets a new build recognise and replace an older install.
AppId={{8A3C1F4E-6B22-4D71-9E0A-1C5D7F2B93A6}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases

DefaultDirName={localappdata}\Programs\TaskbarCat
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; The single-instance mutex from Program.cs. Setup checks for it and asks the user to quit
; the cat rather than failing halfway through overwriting a running exe.
AppMutex=TaskbarCat.SingleInstance

OutputDir=..\dist
OutputBaseFilename=TaskbarCat-Setup-{#AppVersion}
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}

; The payload is a self-contained .NET publish — ~180MB of mostly-compressible runtime.
Compression=lzma2/max
SolidCompression=yes
LZMANumBlockThreads=4

WizardStyle=modern
DisableProgramGroupPage=yes
DisableWelcomePage=no
LicenseFile=
InfoBeforeFile=

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "startup"; Description: "Start {#AppName} when I sign in"; GroupDescription: "Startup:"

[Files]
; The exe and the assets folder beside it. The sprites load from AppContext.BaseDirectory\assets,
; so the layout here has to match the publish output exactly.
Source: "{#SourceDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\assets\*"; DestDir: "{app}\assets"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Must match what StartupService writes byte for byte — same key, same value name, same quoting
; — or the tray's "Start with Windows" tick will disagree with reality.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "TaskbarCat"; ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "Let the cat out"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\assets"

[Code]
// The cat's state — name, coat, size, needs — lives in %APPDATA%\TaskbarCat, not under {app}.
// Uninstalling should not silently delete a pet someone has been feeding for months, so ask.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StatePath: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    StatePath := ExpandConstant('{userappdata}\TaskbarCat');
    if DirExists(StatePath) then
    begin
      // SuppressibleMsgBox, NOT MsgBox, and the suppressed answer is explicitly IDNO.
      // A plain MsgBox under /SUPPRESSMSGBOXES deleted the state anyway — an unattended
      // uninstall silently destroyed a cat somebody had named and fed. Keeping it is the
      // only safe default when there is no one there to ask.
      if SuppressibleMsgBox('Also delete your cat''s saved state?' + #13#10#13#10 +
                'This is its name, coat, size and how hungry it is, in' + #13#10 +
                StatePath + #13#10#13#10 +
                'Choose No to keep it for next time.',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
        DelTree(StatePath, True, True, True);
    end;
  end;
end;
