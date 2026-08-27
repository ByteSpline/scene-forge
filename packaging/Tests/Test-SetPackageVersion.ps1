<#
.SYNOPSIS
  Regression test for packaging/Set-PackageVersion.ps1.

.DESCRIPTION
  Copies the real csproj/iss into a throwaway temp folder (never mutates
  the actual working tree), runs Set-PackageVersion.ps1 against that copy,
  and asserts both files were rewritten consistently. Plain assert-and-exit
  script, matching the style already used elsewhere in packaging/Tests.

.EXAMPLE
  powershell -File packaging/Tests/Test-SetPackageVersion.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$scriptPath = Join-Path $repoRoot "packaging\Set-PackageVersion.ps1"

$failures = @()

function Test-Case {
    param(
        [string]$Name,
        [string]$Version,
        [string]$ExpectedNumericVersion
    )

    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("scene-forge-setversion-test-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
    New-Item -ItemType Directory -Force -Path (Join-Path $tempRoot "src\SceneForge.App") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $tempRoot "packaging\installer") | Out-Null

    Copy-Item -Path (Join-Path $repoRoot "src\SceneForge.App\SceneForge.App.csproj") -Destination (Join-Path $tempRoot "src\SceneForge.App\SceneForge.App.csproj")
    Copy-Item -Path (Join-Path $repoRoot "packaging\installer\SceneForge.iss") -Destination (Join-Path $tempRoot "packaging\installer\SceneForge.iss")

    try {
        & $scriptPath -Version $Version -RepoRoot $tempRoot
        if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) {
            $script:failures += "$Name`: script exited $LASTEXITCODE"
            return
        }

        $csprojText = Get-Content (Join-Path $tempRoot "src\SceneForge.App\SceneForge.App.csproj") -Raw
        $issText = Get-Content (Join-Path $tempRoot "packaging\installer\SceneForge.iss") -Raw

        if ($csprojText -notmatch [regex]::Escape("<Version>$Version</Version>")) {
            $script:failures += "$Name`: csproj <Version> was not stamped to '$Version'"
        }
        if ($csprojText -notmatch [regex]::Escape("<AssemblyVersion>$ExpectedNumericVersion</AssemblyVersion>")) {
            $script:failures += "$Name`: csproj <AssemblyVersion> was not stamped to '$ExpectedNumericVersion'"
        }
        if ($csprojText -notmatch [regex]::Escape("<FileVersion>$ExpectedNumericVersion</FileVersion>")) {
            $script:failures += "$Name`: csproj <FileVersion> was not stamped to '$ExpectedNumericVersion'"
        }
        if ($issText -notmatch [regex]::Escape("#define MyAppVersion `"$Version`"")) {
            $script:failures += "$Name`: .iss MyAppVersion was not stamped to '$Version'"
        }
    } catch {
        $script:failures += "$Name`: threw unexpectedly - $($_.Exception.Message)"
    } finally {
        Remove-Item -Recurse -Force $tempRoot -ErrorAction SilentlyContinue
    }
}

Test-Case -Name "plain release version" -Version "1.4.0" -ExpectedNumericVersion "1.4.0.0"
Test-Case -Name "prerelease version" -Version "2.1.3-rc.1" -ExpectedNumericVersion "2.1.3.0"
Test-Case -Name "artifact-only version tag" -Version "0.0.0-artifact.abcdef1" -ExpectedNumericVersion "0.0.0.0"

# A version missing a MAJOR.MINOR.PATCH core must fail loudly, not silently
# stamp something wrong.
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("scene-forge-setversion-test-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Force -Path (Join-Path $tempRoot "src\SceneForge.App") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $tempRoot "packaging\installer") | Out-Null
Copy-Item -Path (Join-Path $repoRoot "src\SceneForge.App\SceneForge.App.csproj") -Destination (Join-Path $tempRoot "src\SceneForge.App\SceneForge.App.csproj")
Copy-Item -Path (Join-Path $repoRoot "packaging\installer\SceneForge.iss") -Destination (Join-Path $tempRoot "packaging\installer\SceneForge.iss")
try {
    & $scriptPath -Version "1.4" -RepoRoot $tempRoot 2>$null
    $failures += "invalid version core: expected Set-PackageVersion.ps1 to throw for '1.4', but it did not"
} catch {
    # expected
} finally {
    Remove-Item -Recurse -Force $tempRoot -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    Write-Error "Test-SetPackageVersion: $($failures.Count) case(s) FAILED:`n$($failures -join "`n")"
    exit 1
}

Write-Host "Test-SetPackageVersion: all cases passed."
exit 0
