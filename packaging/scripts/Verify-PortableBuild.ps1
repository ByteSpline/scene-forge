<#
.SYNOPSIS
    Best-effort automated check that a portable SceneForge build runs
    without relying on this dev machine's project checkout or PATH.

.DESCRIPTION
    Copies the publish output (or extracts a portable ZIP) into a fresh
    throwaway folder OUTSIDE the repository, launches SceneForge.App.exe
    there with PATH cleared from its environment, and uses UI Automation to
    read the startup diagnostics window's result instead of just the exit
    code (a broken build still exits 0 if the user clicks Exit).

    This is NOT a substitute for testing on a real clean machine/VM with no
    .NET SDK, no dev tools, and no prior SceneForge install - it still runs
    on this machine, under this user, with this OS's registry/WinSxS state
    (including whatever VC++ runtime is already installed here). What it
    DOES prove: the app is not accidentally reading anything from the repo
    checkout path, the source bin\ output folder, or PATH - only from
    files copied into its own isolated folder. See docs/PACKAGING_REPORT.md,
    "Portable build verification", for what was additionally checked
    manually and why a real clean-VM pass is still recommended before
    public distribution.

.PARAMETER SourceDir
    Publish output to copy from. Defaults to
    src/SceneForge.App/bin/publish/win-x64.

.PARAMETER ZipPath
    Instead of -SourceDir, extract this portable ZIP (as produced by
    New-PortableZip.ps1) and verify that.

.EXAMPLE
    .\Publish-SceneForge.ps1
    .\Verify-PortableBuild.ps1

.EXAMPLE
    .\New-PortableZip.ps1
    .\Verify-PortableBuild.ps1 -ZipPath ..\output\SceneForge-1.0.0-win-x64-portable.zip
#>
[CmdletBinding()]
param(
    [string]$SourceDir,
    [string]$ZipPath
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
if (-not $SourceDir) {
    $SourceDir = Join-Path $repoRoot "src\SceneForge.App\bin\publish\win-x64"
}

$verifyRoot = Join-Path $env:TEMP ("SceneForgePortableVerify-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Force -Path $verifyRoot | Out-Null
Write-Host "==> Isolated verification folder: $verifyRoot" -ForegroundColor Cyan

try {
    if ($ZipPath) {
        if (-not (Test-Path $ZipPath)) { throw "'$ZipPath' does not exist." }
        Write-Host "==> Extracting '$ZipPath'" -ForegroundColor Cyan
        Expand-Archive -Path $ZipPath -DestinationPath $verifyRoot -Force
    } else {
        if (-not (Test-Path $SourceDir)) { throw "'$SourceDir' does not exist. Run Publish-SceneForge.ps1 first." }
        Write-Host "==> Copying '$SourceDir'" -ForegroundColor Cyan
        Copy-Item -Path (Join-Path $SourceDir "*") -Destination $verifyRoot -Recurse -Force
    }

    $exePath = Join-Path $verifyRoot "SceneForge.App.exe"
    if (-not (Test-Path $exePath)) {
        throw "No SceneForge.App.exe found in '$verifyRoot' after copy/extract."
    }

    foreach ($required in @("tools\ffmpeg\ffmpeg.exe", "tools\ffmpeg\ffprobe.exe", "tools\opencv\OpenCvSharpExtern.dll")) {
        $path = Join-Path $verifyRoot $required
        if (-not (Test-Path $path)) {
            Write-Warning "Missing '$required' - diagnostics are expected to report this component as failed."
        }
    }

    Write-Host "==> Launching with PATH cleared from the child process environment" -ForegroundColor Cyan
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exePath
    $psi.WorkingDirectory = $verifyRoot
    $psi.UseShellExecute = $false
    $psi.EnvironmentVariables.Clear()
    foreach ($key in @("SystemRoot", "WINDIR", "TEMP", "TMP", "USERPROFILE", "LOCALAPPDATA")) {
        $val = [System.Environment]::GetEnvironmentVariable($key)
        if ($val) { $psi.EnvironmentVariables[$key] = $val }
    }

    $proc = [System.Diagnostics.Process]::Start($psi)
    try {
        $deadline = (Get-Date).AddSeconds(15)
        $hwnd = [IntPtr]::Zero
        while ((Get-Date) -lt $deadline) {
            $proc.Refresh()
            if ($proc.HasExited) {
                throw "Process exited early (code $($proc.ExitCode)) before the diagnostics window appeared."
            }
            if ($proc.MainWindowHandle -ne [IntPtr]::Zero -and $proc.MainWindowTitle -like "*Startup Diagnostics*") {
                $hwnd = $proc.MainWindowHandle
                break
            }
            Start-Sleep -Milliseconds 300
        }

        if ($hwnd -eq [IntPtr]::Zero) {
            throw "Diagnostics window did not appear within 15 seconds."
        }

        Add-Type -AssemblyName UIAutomationClient
        Add-Type -AssemblyName UIAutomationTypes
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
        $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty, "Continue to SceneForge")
        $continueButton = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)

        if ($continueButton -eq $null) {
            throw "Could not find the Continue button via UI Automation - the window layout may have changed."
        }

        # The diagnostics check itself runs asynchronously after the window
        # appears (StartupDiagnosticsViewModel's constructor kicks it off,
        # it does not block window creation - see that class's remarks), so
        # Continue starts disabled and only flips once the check actually
        # finishes. Poll instead of reading it once immediately, or a
        # genuinely passing build reports a false FAIL just from being
        # asked too early.
        $checkDeadline = (Get-Date).AddSeconds(10)
        $allPassed = $false
        while ((Get-Date) -lt $checkDeadline) {
            if ($continueButton.Current.IsEnabled) {
                $allPassed = $true
                break
            }
            Start-Sleep -Milliseconds 250
        }

        if ($allPassed) {
            Write-Host ""
            Write-Host "PASS: all native-component diagnostics reported success in the isolated folder." -ForegroundColor Green
        } else {
            Write-Host ""
            Write-Host "FAIL: at least one native-component diagnostic failed. Re-run interactively to read which one (this script does not read individual result rows)." -ForegroundColor Red
        }
    } finally {
        if (-not $proc.HasExited) {
            $proc.Kill()
        }
    }
} finally {
    Write-Host ""
    Write-Host "Isolated folder left in place for inspection: $verifyRoot"
    Write-Host "Delete it manually once you're done (Remove-Item -Recurse -Force '$verifyRoot')."
}
