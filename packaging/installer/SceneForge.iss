; SceneForge Inno Setup installer script.
;
; Compiled and run end-to-end with Inno Setup 6 (see
; docs/PACKAGING_REPORT.md, "Installer (Inno Setup)", for exactly what was
; verified: per-user install, Start Menu/registry entries, launch from the
; installed location, and uninstall leaving %LOCALAPPDATA%\SceneForge
; untouched). The per-machine/admin install path compiles from this same
; script but was not run end-to-end - see that section for details.
;
; Prerequisites before compiling:
;   1. Run packaging\scripts\Publish-SceneForge.ps1 (produces
;      src\SceneForge.App\bin\publish\win-x64\, referenced below by
;      #define SourceDir).
;   2. Optionally place VC_redist.x64.exe under packaging\vendor\vcredist\
;      (see packaging\vendor\README.md) if you want the installer to offer
;      it - the [Files]/[Run] entries for it are guarded by #ifexist so the
;      script still compiles without it.
;   3. Compile with: iscc packaging\installer\SceneForge.iss
;
; CLAUDE.md rules this script deliberately respects:
;   - Never overwrites/deletes user files: only removes what it installed
;     under {app}; it never touches %LOCALAPPDATA%\SceneForge (the user's
;     projects/logs/thumbnail cache - see ProjectLayout.DefaultAppDataRoot)
;     on uninstall.
;   - No runtime network requirement: everything installed comes from local
;     files staged ahead of time (Publish-SceneForge.ps1's output and the
;     optional local VC_redist.x64.exe) - this script never downloads
;     anything itself.

#define MyAppName "SceneForge"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "SceneForge contributors"
#define MyAppExeName "SceneForge.App.exe"
#define SourceDir "..\..\src\SceneForge.App\bin\publish\win-x64"
#define VcRedistPath "..\vendor\vcredist\VC_redist.x64.exe"

[Setup]
AppId={{0DE63023-C55F-4F36-84A5-D43585AECC26}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppSupportURL=https://github.com/ByteSpline/scene-forge
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=..\output
OutputBaseFilename=SceneForge-{#MyAppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Lets the installer run either per-machine (Program Files + all-users
; Start Menu, needs elevation) or per-user (no UAC prompt at all) - the
; {autopf}/{autoprograms}/{autodesktop} constants used below already
; resolve to the right location for whichever mode is actually chosen.
; Defaults to per-user so a non-admin can still install SceneForge
; without a UAC prompt; /ALLUSERS on the command line (or the wizard's own
; prompt in interactive installs) switches to per-machine.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline dialog
SetupIconFile=..\..\src\SceneForge.App\Resources\app.ico
WizardStyle=modern
; SceneForge.App.exe is already a self-contained single-file publish (see
; win-x64-Release.pubxml) - nothing else needs to be on the target machine
; except the OS itself and, if missing, the VC++ runtime handled below.
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
; recursesubdirs/createallsubdirs picks up tools\ffmpeg\ and tools\opencv\
; alongside the exe - exactly the layout FfmpegToolLocator and
; OpenCvNativeLibraryResolver expect relative to the installed exe (never
; PATH - see those classes' remarks).
; Excludes: Publish-SceneForge.ps1 already produces a runtime-only directory
; (it strips .pdb and never stages dotfiles); this is a defensive gate so a
; stray source/project/debug/dev file in the publish dir is never installed.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,*.cs,*.csproj,*.sln,*.user,*.pubxml,.gitkeep,.gitignore,.gitattributes,.editorconfig"; Flags: ignoreversion recursesubdirs createallsubdirs

#ifexist VcRedistPath
Source: "{#VcRedistPath}"; DestDir: "{tmp}"; Flags: deleteafterinstall
#endif

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
#ifexist VcRedistPath
Filename: "{tmp}\VC_redist.x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Installing the Visual C++ Runtime..."; Check: VCRedistNeedsInstall; Flags: waituntilterminated
#endif
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
// True when the x64 Visual C++ 2015-2022 runtime is not already present,
// per the registry key the redistributable's own installer writes -
// checked so a machine that already has it (a very common case) is never
// re-installed, and so the [Run] entry above is a no-op (not skipped
// entirely) when VC_redist.x64.exe was not bundled at all (#ifexist above
// already keeps the whole entry out of the compiled installer in that
// case). Also requires IsAdminInstallMode: VC_redist.x64.exe installs a
// machine-wide component and needs elevation itself, which a per-user
// SceneForge install (see PrivilegesRequired/PrivilegesRequiredOverridesAllowed
// above) does not have - running it un-elevated would just fail or prompt
// mid-install, so a per-user install instead leaves this to the app's own
// first-run diagnostics (SceneForge.Media.Tooling.NativeDependencyDiagnosticsService)
// to detect and tell the user how to fix.
function VCRedistNeedsInstall(): Boolean;
var
  Installed: Cardinal;
begin
  Result := False;
  if not IsAdminInstallMode() then
  begin
    exit;
  end;
  Result := True;
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64', 'Installed', Installed) then
  begin
    Result := Installed <> 1;
  end;
end;
