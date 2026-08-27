# Shared semantic-version validation for .github/workflows/release.yml's
# 'version' input. Kept as its own module (not inlined in the workflow, and
# not duplicated across the workflow's "Validate inputs" step) so
# packaging/Tests/Test-ReleaseVersioning.ps1 can exercise exactly the rule
# the workflow enforces - see docs/CI_RELEASE_GUIDE.md.

$script:SemVerPattern = '^\d+\.\d+\.\d+(-[0-9A-Za-z][0-9A-Za-z-.]*)?$'

function Test-ReleaseVersion {
    <#
    .SYNOPSIS
      Validates a release workflow 'version' input string.

    .DESCRIPTION
      Required, non-blank, and a semantic version core (MAJOR.MINOR.PATCH)
      with an optional prerelease label (e.g. "1.4.0" or "1.4.0-rc.1").
      Deliberately does not accept build metadata ("+...") - not needed for
      a release tag - or a leading "v" - the workflow adds that itself when
      forming the git tag.

    .OUTPUTS
      $null if the version is valid; otherwise a human-readable error
      message string describing why it was rejected.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Version
    )

    if ([string]::IsNullOrWhiteSpace($Version)) {
        return "The 'version' input is required when build_type is 'versioned-release'."
    }
    if ($Version -notmatch $script:SemVerPattern) {
        return "The 'version' input '$Version' is not a valid semantic version (expected e.g. 1.4.0 or 1.4.0-rc.1)."
    }
    return $null
}

Export-ModuleMember -Function Test-ReleaseVersion
