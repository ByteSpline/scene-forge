<#
.SYNOPSIS
  Regression test for New-PortableZip.ps1's "runtime files only" gate.

.DESCRIPTION
  Builds throwaway fake publish directories under %TEMP% (never touches the
  real publish output or packaging/output/), points New-PortableZip.ps1 at
  them via -PublishDir, and asserts it refuses to package a directory that
  contains source / project / debug-symbol / dotfiles, and does NOT refuse
  one that is clean. Plain assert-and-exit script, matching the style used
  elsewhere in packaging/Tests.

.EXAMPLE
  powershell -File packaging/Tests/Test-PortableZipContents.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$scriptPath = Join-Path $repoRoot "packaging\scripts\New-PortableZip.ps1"
$failures = @()

function New-FakePublishDir {
    param([string[]]$ExtraFiles)

    $dir = Join-Path ([System.IO.Path]::GetTempPath()) ("sf-zipguard-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
    New-Item -ItemType Directory -Force -Path (Join-Path $dir "tools\ffmpeg") | Out-Null
    Set-Content -Path (Join-Path $dir "SceneForge.App.exe") -Value "MZ fake" -NoNewline
    Set-Content -Path (Join-Path $dir "tools\ffmpeg\ffmpeg.exe") -Value "MZ fake" -NoNewline
    foreach ($f in $ExtraFiles) {
        $full = Join-Path $dir $f
        New-Item -ItemType Directory -Force -Path (Split-Path $full) | Out-Null
        Set-Content -Path $full -Value "x" -NoNewline
    }
    return $dir
}

function Test-Rejects {
    param([string]$Name, [string[]]$ExtraFiles)

    $dir = New-FakePublishDir -ExtraFiles $ExtraFiles
    try {
        & $scriptPath -PublishDir $dir 2>$null
        $script:failures += "$Name`: expected New-PortableZip.ps1 to throw, but it did not"
    } catch {
        if ($_.Exception.Message -notmatch 'Refusing to package') {
            $script:failures += "$Name`: threw, but not the content gate - '$($_.Exception.Message)'"
        }
    } finally {
        Remove-Item -Recurse -Force $dir -ErrorAction SilentlyContinue
    }
}

Test-Rejects -Name "rejects .pdb"        -ExtraFiles @("SceneForge.App.pdb")
Test-Rejects -Name "rejects nested .pdb" -ExtraFiles @("SceneForge.Core.pdb")
Test-Rejects -Name "rejects .cs"         -ExtraFiles @("Program.cs")
Test-Rejects -Name "rejects .csproj"     -ExtraFiles @("SceneForge.App.csproj")
Test-Rejects -Name "rejects .gitkeep"    -ExtraFiles @("tools\ffmpeg\.gitkeep")

# A runtime-only directory must get PAST the content gate and actually
# produce a zip. Output is redirected to a temp folder so the real release
# artifact under packaging/output/ is never touched.
$cleanDir = New-FakePublishDir -ExtraFiles @("D3DCompiler_47_cor3.dll", "tools\opencv\OpenCvSharpExtern.dll")
$cleanOut = Join-Path ([System.IO.Path]::GetTempPath()) ("sf-zipguard-out-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
try {
    & $scriptPath -PublishDir $cleanDir -OutputDir $cleanOut 2>$null
    $zip = Get-ChildItem $cleanOut -Filter *.zip -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $zip) {
        $failures += "clean dir: no zip was produced from a runtime-only directory"
    } else {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [System.IO.Compression.ZipFile]::OpenRead($zip.FullName)
        $names = $archive.Entries.FullName
        $archive.Dispose()
        $leaked = $names | Where-Object { $_ -match '\.(pdb|cs|csproj|sln)$' -or $_ -match '(^|[\\/])\.[^\\/]+$' }
        if ($leaked) {
            $failures += "clean dir: zip contains non-runtime entries: $($leaked -join ', ')"
        }
    }
} catch {
    if ($_.Exception.Message -match 'Refusing to package') {
        $failures += "clean dir: content gate wrongly rejected a runtime-only directory - '$($_.Exception.Message)'"
    } else {
        $failures += "clean dir: threw unexpectedly - '$($_.Exception.Message)'"
    }
} finally {
    Remove-Item -Recurse -Force $cleanDir, $cleanOut -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    Write-Error "Test-PortableZipContents: $($failures.Count) case(s) FAILED:`n$($failures -join "`n")"
    exit 1
}

Write-Host "Test-PortableZipContents: all cases passed." -ForegroundColor Green
