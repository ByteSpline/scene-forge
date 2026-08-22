# Environment Report

Collected 2026-08-22 from `C:\Users\Bwp COmputers\Desktop\scene-forge`.

## Findings

- **Operating system:** Microsoft Windows 10 Home, version 10.0.19045, build 19045, 64-bit.
- **Installed .NET SDKs:** .NET SDK 8.0.424 (`C:\Program Files\dotnet\sdk`).
- **Git status:** `main`; no commits yet; no tracked or untracked changes reported.
- **Repository remotes:**
  - `origin` fetch: `https://github.com/ByteSpline/scene-forge.git`
  - `origin` push: `https://github.com/ByteSpline/scene-forge.git`
- **FFmpeg:** Not available on `PATH`.
- **GitHub CLI (`gh`):** Not available on `PATH`.
- **Inno Setup compiler (`iscc`/`ISCC`):** Not available on `PATH`.

## Missing Prerequisites

FFmpeg, GitHub CLI, and Inno Setup are missing. The .NET 8 SDK is installed.

## Installation Commands

Run these in an elevated PowerShell or Command Prompt with WinGet available:

```powershell
winget install --id Gyan.FFmpeg.Shared --exact
winget install --id GitHub.cli --exact
winget install --id JRSoftware.InnoSetup --exact
```

Close and reopen the terminal afterward, then verify:

```powershell
ffmpeg -version
gh --version
iscc /?
```
