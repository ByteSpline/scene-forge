<#
.SYNOPSIS
  Stages real ffmpeg/ffprobe binaries (never the Chocolatey PATH shims) into
  a destination directory, for bundling into a self-contained publish output.

.DESCRIPTION
  SceneForge.Media.Tooling.FfmpegToolLocator resolves ffmpeg/ffprobe strictly
  relative to the running application (see src/SceneForge.Media/Tooling/FfmpegToolLocator.cs)
  and never searches PATH. ffmpeg/ffprobe are also deliberately excluded from
  source control (see .gitignore: "tools/", "**/tools/ffmpeg/"), so every
  build that needs a working app - CI's accuracy-regression job and the
  release workflow alike - must vendor real binaries into a "tools/ffmpeg"
  folder itself.

  `choco install ffmpeg` puts tiny launcher shims on PATH
  (C:\ProgramData\chocolatey\bin), not the real binaries, and the real
  binaries may be either a self-contained static build (runs standalone) or
  a shared build (needs sibling avcodec-*.dll etc. alongside it). This
  mirrors the resolution logic already proven in .github/workflows/ci.yml's
  accuracy-regression job: probe each candidate exe, and fall back to
  locating a folder that holds both the exe and an avcodec-*.dll together.

.PARAMETER DestinationDirectory
  Directory to copy ffmpeg.exe, ffprobe.exe, and their sibling DLLs into.
  Created if it does not already exist.

.EXAMPLE
  ./packaging/Stage-Ffmpeg.ps1 -DestinationDirectory C:\publish\tools\ffmpeg
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DestinationDirectory
)

$ErrorActionPreference = 'Stop'

function Resolve-RealFfmpegBinaryDirectory {
    param(
        [string]$ExeName,
        [string]$ChocoLib
    )

    $candidates = @(Get-ChildItem -Path $ChocoLib -Recurse -Filter $ExeName -ErrorAction SilentlyContinue)
    Write-Host "Found $($candidates.Count) candidate $ExeName file(s) under $ChocoLib`:"
    $candidates | ForEach-Object { Write-Host "  $($_.FullName)" }

    if ($candidates.Count -eq 0) {
        return $null
    }

    Write-Host "Case 1: probing each candidate to see if it runs standalone (a self-contained static build)..."
    foreach ($candidate in $candidates) {
        Write-Host "  Probing $($candidate.FullName) -version ..."
        $probeExitCode = -1
        try {
            & $candidate.FullName -version *> $null
            $probeExitCode = $LASTEXITCODE
        } catch {
            Write-Host "    -> failed to launch: $($_.Exception.Message)"
            continue
        }

        if ($probeExitCode -eq 0) {
            Write-Host "    -> exit 0: self-contained static build, using as-is."
            return $candidate.DirectoryName
        }

        Write-Host "    -> exit $probeExitCode (not runnable standalone - likely needs a sibling DLL)."
    }

    Write-Host "Case 2: no candidate ran standalone - searching the ffmpeg package tree for a folder containing both $ExeName and avcodec-*.dll together..."
    $packageRoot = Join-Path $ChocoLib "ffmpeg"
    if (-not (Test-Path $packageRoot)) {
        Write-Host "  ($packageRoot not found - falling back to searching all of $ChocoLib)"
        $packageRoot = $ChocoLib
    }

    $dllCandidates = @(Get-ChildItem -Path $packageRoot -Recurse -Filter "avcodec-*.dll" -ErrorAction SilentlyContinue)
    Write-Host "  Found $($dllCandidates.Count) avcodec-*.dll file(s) under $packageRoot`:"
    $dllCandidates | ForEach-Object { Write-Host "    $($_.FullName)" }

    foreach ($dll in $dllCandidates) {
        $dllDir = $dll.DirectoryName
        if (Test-Path (Join-Path $dllDir $ExeName)) {
            Write-Host "  -> matched: $dllDir contains both $ExeName and avcodec-*.dll."
            return $dllDir
        }
    }

    return $null
}

New-Item -ItemType Directory -Force -Path $DestinationDirectory | Out-Null

$chocolateyRoot = $env:ChocolateyInstall
if ([string]::IsNullOrEmpty($chocolateyRoot)) {
    $chocolateyRoot = "C:\ProgramData\chocolatey"
}
$chocoLib = Join-Path $chocolateyRoot "lib"
Write-Host "Searching for the real ffmpeg/ffprobe binaries under: $chocoLib"

Write-Host "--- Resolving ffmpeg.exe ---"
$ffmpegRealDir = Resolve-RealFfmpegBinaryDirectory -ExeName "ffmpeg.exe" -ChocoLib $chocoLib
if (-not $ffmpegRealDir) {
    Write-Error "Could not resolve a working ffmpeg.exe (neither standalone nor paired with avcodec-*.dll) under $chocoLib."
    exit 1
}
Write-Host "Resolved ffmpeg.exe source directory: $ffmpegRealDir"

Write-Host "--- Resolving ffprobe.exe ---"
$ffprobeRealDir = Resolve-RealFfmpegBinaryDirectory -ExeName "ffprobe.exe" -ChocoLib $chocoLib
if (-not $ffprobeRealDir) {
    Write-Error "Could not resolve a working ffprobe.exe (neither standalone nor paired with avcodec-*.dll) under $chocoLib."
    exit 1
}
Write-Host "Resolved ffprobe.exe source directory: $ffprobeRealDir"

$sourceDirs = @(@($ffmpegRealDir, $ffprobeRealDir) | Select-Object -Unique)
Write-Host "Copying from $($sourceDirs.Count) distinct source directory(ies): $($sourceDirs -join ', ')"

foreach ($dir in $sourceDirs) {
    $exeFiles = @(Get-ChildItem -Path $dir -Filter "*.exe")
    $dllFiles = @(Get-ChildItem -Path $dir -Filter "*.dll")
    Write-Host "  $($dir): copying $($exeFiles.Count) .exe file(s) and $($dllFiles.Count) .dll file(s)"
    if ($exeFiles.Count -gt 0) {
        Copy-Item -Path $exeFiles.FullName -Destination $DestinationDirectory -Force
    }
    if ($dllFiles.Count -gt 0) {
        Copy-Item -Path $dllFiles.FullName -Destination $DestinationDirectory -Force
    }
}

$stagedFfmpeg = Join-Path $DestinationDirectory "ffmpeg.exe"
$stagedFfprobe = Join-Path $DestinationDirectory "ffprobe.exe"
if (-not (Test-Path $stagedFfmpeg) -or -not (Test-Path $stagedFfprobe)) {
    Write-Error "Staging failed: ffmpeg.exe and/or ffprobe.exe missing from $DestinationDirectory after copy (ffmpeg source: $ffmpegRealDir, ffprobe source: $ffprobeRealDir)."
    exit 1
}

Write-Host "Staged $DestinationDirectory contains:"
Get-ChildItem -Path $DestinationDirectory | ForEach-Object { Write-Host "  $($_.Name)" }
