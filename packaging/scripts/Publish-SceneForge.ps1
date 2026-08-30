<#
.SYNOPSIS
    Produces a self-contained, single-file win-x64 Release publish of
    SceneForge, with ffmpeg/ffprobe staged under tools\ffmpeg\ next to the
    published exe.

.DESCRIPTION
    Runs `dotnet publish` against the win-x64-Release publish profile (see
    src/SceneForge.App/Properties/PublishProfiles/win-x64-Release.pubxml),
    which produces the single-file exe and relocates OpenCvSharp's native
    library into tools\opencv\ via an MSBuild target in SceneForge.App.csproj
    (RelocateOpenCvNativeAssetForPackaging). This script's own job is the
    part MSBuild cannot do: copying the ffmpeg/ffprobe vendor binaries
    (never fetched over the network - see packaging/vendor/README.md) into
    tools\ffmpeg\.

    Does not build an installer or a ZIP - see New-PortableZip.ps1 and
    packaging/installer/SceneForge.iss for those.

.PARAMETER VendorFfmpegDir
    Directory containing ffmpeg.exe, ffprobe.exe, and their dependency
    DLLs. Defaults to packaging/vendor/ffmpeg (see that folder's README).

.PARAMETER SkipFfmpegStaging
    Skip copying ffmpeg entirely - useful for proving the rest of the
    packaging pipeline (single-file publish, icon, OpenCV relocation,
    startup diagnostics) without a real ffmpeg toolchain on hand. The
    resulting build will fail its own first-run diagnostics until ffmpeg is
    added.

.EXAMPLE
    .\Publish-SceneForge.ps1
    .\Publish-SceneForge.ps1 -VendorFfmpegDir "D:\ffmpeg-shared\bin"
#>
[CmdletBinding()]
param(
    [string]$VendorFfmpegDir,
    [switch]$SkipFfmpegStaging
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$appProject = Join-Path $repoRoot "src\SceneForge.App\SceneForge.App.csproj"
$publishDir = Join-Path $repoRoot "src\SceneForge.App\bin\publish\win-x64"

if (-not $VendorFfmpegDir) {
    $VendorFfmpegDir = Join-Path $repoRoot "packaging\vendor\ffmpeg"
}

Write-Host "==> dotnet publish (win-x64-Release profile)" -ForegroundColor Cyan
& dotnet publish $appProject -c Release -p:PublishProfile=win-x64-Release
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exePath = Join-Path $publishDir "SceneForge.App.exe"
if (-not (Test-Path $exePath)) {
    throw "Publish did not produce '$exePath' - check the dotnet publish output above."
}

# The portable ZIP and the installer both package this directory verbatim,
# so it must contain only what the app needs at runtime. `dotnet publish`
# emits a .pdb next to the exe (and copies each referenced project's .pdb as
# a "reference-related file"); debug symbols are not runtime-required for a
# distributed build and this repo builds deterministically
# (Directory.Build.props: <Deterministic>true</Deterministic>), so exact-
# commit symbols can be regenerated from source if a crash ever needs them.
# Strip them here rather than in a .csproj so `dotnet build`/`dotnet test`/F5
# keep their symbols untouched. See docs/PACKAGING_REPORT.md, "Debug symbols".
$pdbFiles = Get-ChildItem -Path $publishDir -Recurse -File -Filter *.pdb
if ($pdbFiles) {
    $pdbFiles | Remove-Item -Force
    Write-Host "Removed $($pdbFiles.Count) .pdb symbol file(s) from the publish output." -ForegroundColor Green
}

$openCvDll = Join-Path $publishDir "tools\opencv\OpenCvSharpExtern.dll"
if (-not (Test-Path $openCvDll)) {
    throw "'$openCvDll' is missing - the RelocateOpenCvNativeAssetForPackaging MSBuild target should have created it. See SceneForge.App.csproj."
}
Write-Host "OpenCV native library staged at tools\opencv\ (verified)." -ForegroundColor Green

if ($SkipFfmpegStaging) {
    Write-Host "==> Skipping ffmpeg staging (-SkipFfmpegStaging)." -ForegroundColor Yellow
} else {
    if (-not (Test-Path $VendorFfmpegDir)) {
        throw "Vendor ffmpeg directory '$VendorFfmpegDir' does not exist. See packaging/vendor/README.md for what to place there, or pass -SkipFfmpegStaging to publish without it."
    }

    $requiredFiles = @("ffmpeg.exe", "ffprobe.exe")
    $missing = $requiredFiles | Where-Object { -not (Test-Path (Join-Path $VendorFfmpegDir $_)) }
    if ($missing.Count -gt 0) {
        throw "Vendor ffmpeg directory '$VendorFfmpegDir' is missing: $($missing -join ', '). See packaging/vendor/README.md."
    }

    $toolsFfmpegDir = Join-Path $publishDir "tools\ffmpeg"
    New-Item -ItemType Directory -Force -Path $toolsFfmpegDir | Out-Null

    Write-Host "==> Staging ffmpeg from '$VendorFfmpegDir' into tools\ffmpeg\" -ForegroundColor Cyan
    # Copy everything a shared FFmpeg build needs (the .exe pair plus their
    # sibling av*/sw* DLLs, and any license .txt) - but never the dotfiles
    # git uses to keep this otherwise-gitignored folder tracked (.gitkeep),
    # which would otherwise ride along into the shipped package.
    Get-ChildItem -Path $VendorFfmpegDir -File |
        Where-Object { $_.Name -notlike '.*' } |
        Copy-Item -Destination $toolsFfmpegDir -Force

    Write-Host "ffmpeg staged: $((Get-ChildItem $toolsFfmpegDir -File).Count) file(s)." -ForegroundColor Green
}

$exeSizeMb = [Math]::Round((Get-Item $exePath).Length / 1MB, 1)
Write-Host ""
Write-Host "Publish complete." -ForegroundColor Green
Write-Host "  Output:   $publishDir"
Write-Host "  Exe size: $exeSizeMb MB (single-file, self-contained)"
Write-Host ""
Write-Host "Next: .\New-PortableZip.ps1 for a portable ZIP, or compile packaging\installer\SceneForge.iss for an installer."
