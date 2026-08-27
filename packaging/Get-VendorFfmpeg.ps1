<#
.SYNOPSIS
  Downloads a pinned, verifiably LGPL-only FFmpeg/FFprobe Windows build and
  stages it into packaging/vendor/ffmpeg, ready for
  packaging/scripts/Publish-SceneForge.ps1 to bundle.

.DESCRIPTION
  packaging/vendor/README.md and LICENSE_NOTICE.md both require an
  LGPL-only "shared" FFmpeg build (never a "full"/GPL build) and name
  BtbN's `*-lgpl-shared` GitHub releases as an explicitly acceptable
  source - vendoring is otherwise a manual, human step by design (see that
  README's "the build simply does not have anything to bundle until a
  packager has explicitly placed it here"), so this script exists only to
  let an automated release pipeline perform that same, deliberate choice
  reproducibly instead of skipping it.

  Downloads from a specific, immutable release tag (never the mutable
  "latest" alias, which can change what it points to without notice) and
  extracts only the .exe/.dll files FfmpegToolLocator actually needs -
  not the import libraries, headers, or ffplay.exe also present in the
  archive.

.PARAMETER DestinationDirectory
  Where to place ffmpeg.exe, ffprobe.exe, and their dependency DLLs.
  Defaults to packaging/vendor/ffmpeg (Publish-SceneForge.ps1's own
  default source).

.EXAMPLE
  ./packaging/Get-VendorFfmpeg.ps1
#>
[CmdletBinding()]
param(
    [string]$DestinationDirectory
)

$ErrorActionPreference = 'Stop'
# Invoke-WebRequest's default progress-bar rendering is extremely slow in
# Windows PowerShell for large downloads (a well-known issue, not specific
# to this file) - disabling it is the difference between this finishing in
# seconds and timing out.
$ProgressPreference = 'SilentlyContinue'

# Pinned to a specific, immutable BtbN release tag + asset - see this
# script's own regression coverage expectations in docs/CI_RELEASE_GUIDE.md
# for why "latest" is deliberately not used here. Confirmed to exist and
# resolve to a real ffmpeg-n9.0.1 LGPL-shared win64 build (bin/ffmpeg.exe,
# bin/ffprobe.exe, and matching avcodec-63/avformat-63/avutil-61/
# swresample-7/swscale-10/avdevice-63/avfilter-12 DLLs, plus LICENSE.txt)
# before pinning. Bump deliberately, not silently, when a newer build is
# needed - re-verify the new asset actually exists first.
$FfmpegReleaseTag = "autobuild-2026-08-26-13-06"
$FfmpegAssetName = "ffmpeg-n9.0.1-8-g16dfae5c88-win64-lgpl-shared-9.0.zip"
$FfmpegDownloadUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/$FfmpegReleaseTag/$FfmpegAssetName"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if (-not $DestinationDirectory) {
    $DestinationDirectory = Join-Path $repoRoot "packaging\vendor\ffmpeg"
}

$workDir = Join-Path ([System.IO.Path]::GetTempPath()) ("scene-forge-ffmpeg-vendor-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Force -Path $workDir | Out-Null
$zipPath = Join-Path $workDir $FfmpegAssetName

try {
    Write-Host "==> Downloading $FfmpegDownloadUrl" -ForegroundColor Cyan
    Invoke-WebRequest -Uri $FfmpegDownloadUrl -OutFile $zipPath -UseBasicParsing

    Write-Host "==> Extracting to $workDir" -ForegroundColor Cyan
    Expand-Archive -Path $zipPath -DestinationPath $workDir -Force

    $binDir = Get-ChildItem -Path $workDir -Recurse -Directory -Filter "bin" | Select-Object -First 1
    if (-not $binDir) {
        throw "No 'bin' folder found after extracting $FfmpegAssetName - the archive layout may have changed."
    }

    New-Item -ItemType Directory -Force -Path $DestinationDirectory | Out-Null

    $required = @("ffmpeg.exe", "ffprobe.exe")
    $missing = $required | Where-Object { -not (Test-Path (Join-Path $binDir.FullName $_)) }
    if ($missing.Count -gt 0) {
        throw "Extracted build is missing: $($missing -join ', ') under $($binDir.FullName)."
    }

    Get-ChildItem -Path $binDir.FullName -Filter "*.exe" | Where-Object { $_.Name -in $required } | Copy-Item -Destination $DestinationDirectory -Force
    Get-ChildItem -Path $binDir.FullName -Filter "*.dll" | Copy-Item -Destination $DestinationDirectory -Force

    $stagedCount = (Get-ChildItem -Path $DestinationDirectory -File | Where-Object { $_.Extension -in ".exe", ".dll" }).Count
    Write-Host "Vendored $stagedCount file(s) into $DestinationDirectory (LGPL-only, BtbN $FfmpegReleaseTag / $FfmpegAssetName)." -ForegroundColor Green
} finally {
    Remove-Item -Recurse -Force $workDir -ErrorAction SilentlyContinue
}
