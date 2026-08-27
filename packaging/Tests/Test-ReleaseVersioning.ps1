<#
.SYNOPSIS
  Regression test for packaging/ReleaseVersioning.psm1's semantic-version
  validation rule - the same rule .github/workflows/release.yml's
  "Validate inputs" step enforces for the 'version' workflow input.

.DESCRIPTION
  A plain assert-and-exit script, not a test framework, matching the style
  already used by packaging/Stage-Ffmpeg.ps1 elsewhere in this directory.
  Covers the accept/reject cases the workflow depends on, plus two
  adversarial strings shaped like the shell/PowerShell injection this
  module's caller was fixed to no longer be vulnerable to (see "Known risks
  and follow-ups" in docs/CI_RELEASE_GUIDE.md) - these must be rejected by
  the semver check itself, independent of the env-var passing fix.

.EXAMPLE
  pwsh -File packaging/Tests/Test-ReleaseVersioning.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot "..\ReleaseVersioning.psm1") -Force

$cases = @(
    @{ Version = "1.4.0"; ShouldBeValid = $true }
    @{ Version = "0.0.1"; ShouldBeValid = $true }
    @{ Version = "1.4.0-rc.1"; ShouldBeValid = $true }
    @{ Version = "2.0.0-beta"; ShouldBeValid = $true }
    @{ Version = "10.20.30"; ShouldBeValid = $true }
    @{ Version = ""; ShouldBeValid = $false }
    @{ Version = "   "; ShouldBeValid = $false }
    @{ Version = "1.4"; ShouldBeValid = $false }
    @{ Version = "v1.4.0"; ShouldBeValid = $false }
    @{ Version = "1.4.0.0"; ShouldBeValid = $false }
    @{ Version = "1.4.0-"; ShouldBeValid = $false }
    @{ Version = "1.4.0+build.1"; ShouldBeValid = $false }
    @{ Version = 'not-a-version'; ShouldBeValid = $false }
    @{ Version = '1.4.0" ; Remove-Item -Recurse -Force $HOME #'; ShouldBeValid = $false }
    @{ Version = '1.4.0`nWrite-Host pwned'; ShouldBeValid = $false }
)

$failures = @()
foreach ($case in $cases) {
    $errorMessage = Test-ReleaseVersion -Version $case.Version
    $isValid = ($null -eq $errorMessage)
    if ($isValid -ne $case.ShouldBeValid) {
        $failures += "Version '$($case.Version)': expected ShouldBeValid=$($case.ShouldBeValid), got $isValid (message: '$errorMessage')"
    }
}

if ($failures.Count -gt 0) {
    Write-Error "Test-ReleaseVersioning: $($failures.Count) of $($cases.Count) case(s) FAILED:`n$($failures -join "`n")"
    exit 1
}

Write-Host "Test-ReleaseVersioning: all $($cases.Count) cases passed."
exit 0
