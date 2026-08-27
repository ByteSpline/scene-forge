<#
.SYNOPSIS
  Stamps a version into SceneForge.App.csproj and packaging/installer/SceneForge.iss
  ahead of an automated release build, so a CI-driven version and the
  version baked into the published exe / installer never drift apart.

.DESCRIPTION
  packaging/scripts/Publish-SceneForge.ps1, packaging/scripts/New-PortableZip.ps1,
  and packaging/installer/SceneForge.iss all read (or hardcode) a version
  independently: the csproj's own <Version> (Publish-SceneForge.ps1 via
  `dotnet publish`, New-PortableZip.ps1 by reading the csproj's XML
  directly) and the .iss's own separate `#define MyAppVersion "1.0.0"`
  literal. For local, manual packaging that is fine - a packager bumps the
  csproj's <Version> once per SceneForge.App.csproj's own comment ("Bump
  this (and only this) for every subsequent packaged build") and updates
  the .iss's literal alongside it. An automated pipeline needs the
  equivalent of that same manual step: this script performs both edits
  from one input so the two can never independently drift, without
  changing either file's format or adding a parameter those scripts don't
  already support.

  Edits the working tree in place. Intended for an ephemeral CI checkout
  that is never committed back - not something a contributor runs against
  their own local checkout and commits.

.PARAMETER Version
  Full version string to stamp into <Version> (csproj) and MyAppVersion
  (.iss) verbatim - may include a semver prerelease label, e.g.
  "1.4.0" or "0.0.0-artifact.abcdef1".

.PARAMETER RepoRoot
  Defaults to the repository root relative to this script's own location.

.EXAMPLE
  ./packaging/Set-PackageVersion.ps1 -Version "1.4.0"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$RepoRoot
)

$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
}

function ConvertTo-NumericAssemblyVersion {
    <#
    .SYNOPSIS
      Derives a 4-part numeric AssemblyVersion/FileVersion from a semver
      string, since those MSBuild properties reject prerelease labels.
    #>
    param([Parameter(Mandatory = $true)][string]$Version)

    $core = $Version.Split('-')[0].Split('+')[0]
    $parts = $core.Split('.')
    if ($parts.Count -lt 3) {
        throw "'$Version' does not have a MAJOR.MINOR.PATCH core - cannot derive an AssemblyVersion from it."
    }
    return "$($parts[0]).$($parts[1]).$($parts[2]).0"
}

$numericVersion = ConvertTo-NumericAssemblyVersion -Version $Version

$csprojPath = Join-Path $RepoRoot "src\SceneForge.App\SceneForge.App.csproj"
if (-not (Test-Path $csprojPath)) {
    throw "'$csprojPath' does not exist."
}
$csprojContent = Get-Content -Path $csprojPath -Raw

$csprojReplacements = @(
    @{ Pattern = '<Version>[^<]*</Version>'; Replacement = "<Version>$Version</Version>" }
    @{ Pattern = '<AssemblyVersion>[^<]*</AssemblyVersion>'; Replacement = "<AssemblyVersion>$numericVersion</AssemblyVersion>" }
    @{ Pattern = '<FileVersion>[^<]*</FileVersion>'; Replacement = "<FileVersion>$numericVersion</FileVersion>" }
)
foreach ($replacement in $csprojReplacements) {
    if ($csprojContent -notmatch $replacement.Pattern) {
        throw "Pattern '$($replacement.Pattern)' not found in '$csprojPath' - has its version metadata format changed?"
    }
    $csprojContent = $csprojContent -replace $replacement.Pattern, $replacement.Replacement
}
Set-Content -Path $csprojPath -Value $csprojContent -NoNewline

$issPath = Join-Path $RepoRoot "packaging\installer\SceneForge.iss"
if (-not (Test-Path $issPath)) {
    throw "'$issPath' does not exist."
}
$issContent = Get-Content -Path $issPath -Raw
$issPattern = '#define MyAppVersion "[^"]*"'
if ($issContent -notmatch $issPattern) {
    throw "Pattern '$issPattern' not found in '$issPath' - has its version definition changed?"
}
$issContent = $issContent -replace $issPattern, "#define MyAppVersion `"$Version`""
Set-Content -Path $issPath -Value $issContent -NoNewline

Write-Host "Stamped version '$Version' (AssemblyVersion/FileVersion '$numericVersion') into:" -ForegroundColor Green
Write-Host "  $csprojPath"
Write-Host "  $issPath"
