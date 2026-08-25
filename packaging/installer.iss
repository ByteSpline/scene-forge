; SceneForge Windows installer definition (Inno Setup 6).
;
; Built exclusively by .github/workflows/release.yml from a self-contained
; `dotnet publish` output directory - never run this against a repo
; checkout directly. The installer only copies files that already exist in
; the publish output (including the vendored tools\ffmpeg and OpenCvSharp
; native assets); it never downloads anything and the installed app makes
; no network calls (CLAUDE.md: no runtime network requirement, no
; telemetry).
;
; Required command-line defines (passed via `iscc /D...`):
;   AppVersion  - display/file version string, e.g. 1.4.0 or
;                 0.0.0-artifact.abcdef1 for artifact-only builds.
;   PublishDir  - absolute path to the self-contained `dotnet publish`
;                 output folder (must contain SceneForge.App.exe).
;   OutputDir   - absolute path to write the built setup .exe into.
;
; Example:
;   iscc packaging\installer.iss ^
;     /DAppVersion=1.4.0 ^
;     /DPublishDir=C:\publish\SceneForge.App ^
;     /DOutputDir=C:\out

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\src\SceneForge.App\bin\Release\net8.0-windows\win-x64\publish"
#endif
#ifndef OutputDir
  #define OutputDir "output"
#endif

#define AppName "SceneForge"
#define AppPublisher "ByteSpline"
#define AppExeName "SceneForge.App.exe"

[Setup]
; Fixed GUID: keeps upgrade/uninstall identity stable across versions.
AppId={{9D8C6C3E-4C8B-4C8E-9C2E-6D6B0E7D9B1A}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=SceneForge-{#AppVersion}-win-x64-setup
Compression=lzma2/fast
SolidCompression=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
; Copies the entire self-contained publish output as-is, including the
; vendored tools\ffmpeg folder and OpenCvSharp native assets - nothing here
; is downloaded or generated at install time.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent unchecked
