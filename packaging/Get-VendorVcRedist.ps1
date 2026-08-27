<#
.SYNOPSIS
  Downloads the genuine Microsoft Visual C++ x64 Redistributable and stages
  it into packaging/vendor/vcredist, ready for
  packaging/installer/SceneForge.iss's optional VC++ bootstrap step.

.DESCRIPTION
  packaging/vendor/README.md documents this as a manual step ("download
  the genuine, current installer directly from Microsoft ... and place it
  here"), deliberately left unautomated because the packaging pipeline
  works correctly without it - SceneForge.iss guards every reference to it
  with `#ifexist` and compiles fine in its absence, and the app's own
  NativeDependencyDiagnosticsService detects and reports a missing VC++
  runtime at first launch regardless. This script exists to let an
  automated release pipeline close that gap when it can, not because the
  pipeline requires it.

  Uses https://aka.ms/vs/17/release/vc_redist.x64.exe, Microsoft's own
  stable, permanent redirect for "the current x64 redistributable" (the
  same link named on Microsoft's public downloads page) - not a pinned
  build, deliberately: unlike FFmpeg there is no licensing reason to freeze
  a specific version, and Microsoft is the only legitimate source for this
  binary in the first place.

  Failure here is treated as non-fatal by the caller (see
  .github/workflows/release.yml): a transient network failure fetching an
  optional bootstrap component should not fail an entire release build the
  way a missing FFmpeg build must.

.PARAMETER DestinationDirectory
  Where to place VC_redist.x64.exe. Defaults to packaging/vendor/vcredist
  (packaging/installer/SceneForge.iss's own expected location).

.EXAMPLE
  ./packaging/Get-VendorVcRedist.ps1
#>
[CmdletBinding()]
param(
    [string]$DestinationDirectory
)

$ErrorActionPreference = 'Stop'
# See packaging/Get-VendorFfmpeg.ps1's identical setting - Invoke-WebRequest's
# progress-bar rendering is extremely slow in Windows PowerShell otherwise.
$ProgressPreference = 'SilentlyContinue'

$VcRedistUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if (-not $DestinationDirectory) {
    $DestinationDirectory = Join-Path $repoRoot "packaging\vendor\vcredist"
}

New-Item -ItemType Directory -Force -Path $DestinationDirectory | Out-Null
$destPath = Join-Path $DestinationDirectory "VC_redist.x64.exe"

Write-Host "==> Downloading $VcRedistUrl" -ForegroundColor Cyan
Invoke-WebRequest -Uri $VcRedistUrl -OutFile $destPath -UseBasicParsing

$sizeMb = [Math]::Round((Get-Item $destPath).Length / 1MB, 1)
Write-Host "Vendored VC_redist.x64.exe ($sizeMb MB) into $DestinationDirectory." -ForegroundColor Green
