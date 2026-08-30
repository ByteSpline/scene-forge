# Vendor binaries

This folder is where a packager places the third-party binaries that
`packaging/scripts/Publish-SceneForge.ps1` and `packaging/installer/SceneForge.iss`
bundle into the packaged app. **Nothing under this folder is committed to
git** (see `.gitignore`) except this README - every file listed below has
its own redistribution license that must be reviewed before it is shipped
to a real user; see [LICENSE_NOTICE.md](../../LICENSE_NOTICE.md) for the
concrete redistribution notes for each one.

This separation is deliberate: it keeps large, license-encumbered third-
party binaries out of source control (git history would otherwise grow
by hundreds of MB per binary update) and makes the "have you actually
reviewed the license for what you're about to ship" question impossible to
skip, since the build simply does not have anything to bundle until a
packager has explicitly placed it here.

## `ffmpeg/` - ffmpeg.exe, ffprobe.exe, and their dependency DLLs

`SceneForge.Media.Tooling.FfmpegToolLocator` looks for `ffmpeg.exe` and
`ffprobe.exe` under `tools\ffmpeg\` next to the installed/portable exe, and
never on `PATH`. `Publish-SceneForge.ps1` copies **every non-dotfile** in
this folder (not just the two .exe files; `.gitkeep` and any other dotfile
is skipped so it never rides into the shipped package) into that location,
because a shared FFmpeg build's `ffmpeg.exe`/`ffprobe.exe` depend on sibling
DLLs
(`avcodec-*.dll`, `avformat-*.dll`, `avutil-*.dll`, `swresample-*.dll`,
`swscale-*.dll`, and `avfilter-*.dll`/`avdevice-*.dll` if present) that
must sit in the same directory to load (Windows' default DLL search order
checks the launching executable's own directory first, which is exactly
`tools\ffmpeg\` here - no PATH entry needed).

**Required files at minimum:**

```
packaging/vendor/ffmpeg/
  ffmpeg.exe
  ffprobe.exe
  avcodec-*.dll
  avformat-*.dll
  avutil-*.dll
  swresample-*.dll
  swscale-*.dll
```

**Which build to use:** get a **shared, LGPL-only** Windows build (no
GPL-licensed codecs enabled) - for example the "shared" builds published at
gyan.dev or BtbN's LGPL builds on GitHub. Do **not** use a "full"/GPL build
for anything that leaves this machine; see LICENSE_NOTICE.md for why.
`Publish-SceneForge.ps1 -SkipFfmpegStaging` skips this step entirely for a
build that only needs to prove the packaging mechanics (icon, single-file
publish, OpenCV relocation, diagnostics gate) without a real toolchain.

## `vcredist/VC_redist.x64.exe` - Microsoft Visual C++ x64 Redistributable

Only needed for `packaging/installer/SceneForge.iss` (the portable ZIP does
not run installers; see its own first-run diagnostics for the
"already installed or not" story). Download the genuine, current installer
directly from Microsoft (search "Visual C++ Redistributable latest
supported downloads" on microsoft.com and take the **x64** file) and place
it here as `VC_redist.x64.exe`. The Inno Setup script only runs it if the
target machine's registry shows the runtime is not already present, and its
`[Files]`/`[Run]` entries for it are guarded by `#ifexist` so the installer
script still compiles (with a warning) if this file is absent - a packager
must deliberately add it before shipping an installer that depends on it.
