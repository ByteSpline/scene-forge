<#
.SYNOPSIS
    Zips an existing win-x64 publish output into a portable ZIP under
    packaging/output.

.DESCRIPTION
    Does not publish - run Publish-SceneForge.ps1 first (or pass
    -PublishDir to point at an existing publish output). Names the archive
    from the <Version> already embedded in SceneForge.App.csproj (the same
    value Explorer's Properties dialog and the Inno Setup installer read),
    so the ZIP name and the installer name always agree.

.PARAMETER PublishDir
    Defaults to src/SceneForge.App/bin/publish/win-x64 (Publish-SceneForge.ps1's
    own output location).

.EXAMPLE
    .\Publish-SceneForge.ps1
    .\New-PortableZip.ps1
#>
[CmdletBinding()]
param(
    [string]$PublishDir
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$appProject = Join-Path $repoRoot "src\SceneForge.App\SceneForge.App.csproj"
$outputDir = Join-Path $repoRoot "packaging\output"

if (-not $PublishDir) {
    $PublishDir = Join-Path $repoRoot "src\SceneForge.App\bin\publish\win-x64"
}

if (-not (Test-Path $PublishDir)) {
    throw "'$PublishDir' does not exist. Run Publish-SceneForge.ps1 first."
}

$exePath = Join-Path $PublishDir "SceneForge.App.exe"
if (-not (Test-Path $exePath)) {
    throw "'$PublishDir' does not look like a SceneForge publish output (no SceneForge.App.exe)."
}

$ffmpegExe = Join-Path $PublishDir "tools\ffmpeg\ffmpeg.exe"
if (-not (Test-Path $ffmpegExe)) {
    Write-Warning "tools\ffmpeg\ffmpeg.exe is missing from '$PublishDir' - this ZIP will fail its own first-run diagnostics until ffmpeg is staged (see Publish-SceneForge.ps1 / packaging/vendor/README.md)."
}

[xml]$csprojXml = Get-Content $appProject
$version = $csprojXml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) {
    throw "Could not read <Version> from '$appProject'."
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$zipPath = Join-Path $outputDir "SceneForge-$version-win-x64-portable.zip"

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Write-Host "==> Compressing '$PublishDir' -> '$zipPath'" -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

$zipSizeMb = [Math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host ""
Write-Host "Portable ZIP ready." -ForegroundColor Green
Write-Host "  $zipPath ($zipSizeMb MB)"
Write-Host ""
Write-Host "Verify it with Verify-PortableBuild.ps1 before distributing."
