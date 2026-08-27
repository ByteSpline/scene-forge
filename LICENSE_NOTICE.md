# License Notice

SceneForge is a closed/internal-distribution native Windows application
that bundles third-party binaries at packaging time (see
`packaging/scripts/Publish-SceneForge.ps1` and
`packaging/installer/SceneForge.iss`). Nothing third-party is committed to
this repository - see `packaging/vendor/README.md` for where a packager
places each binary before building. This document records what those
binaries are, their license terms, and what a packager must do to stay
compliant before shipping a build to anyone outside this repository.

**This is not legal advice.** It is a concrete, project-specific summary
to make compliance checkable in code review. Get real legal review before
any public release.

## FFmpeg / FFprobe (`tools\ffmpeg\`)

FFmpeg's own license depends entirely on which optional components a given
build enables:

- A build with only LGPL-compatible components enabled is **LGPL v2.1+**.
- A build that also enables GPL-only components (commonly `libx264`,
  `libx265`, `libass`, and similar) is **GPL v2/v3**, which is copyleft and
  imposes source-availability obligations on the whole distributed work.

**Requirement: SceneForge must only ship an LGPL-only FFmpeg build.**
Shipping a GPL build would put the whole packaged application under GPL
obligations, which conflicts with distributing SceneForge as a closed
build. Concretely:

- Use a "shared, LGPL" Windows build (for example the LGPL-labeled
  "shared" builds at gyan.dev, or BtbN's `*-lgpl-shared` builds on GitHub)
  - never a "full"/GPL build. **The build used to verify the packaging
    pipeline for this phase (a local Gyan "full_build-shared", already
    present on the development machine for convenience) is GPL v3 and was
    used only for local, non-distributed verification - see
    `docs/PACKAGING_REPORT.md`. It must never be the build a packager
    stages into `packaging/vendor/ffmpeg/` for a real release.**
- SceneForge only ever invokes `ffmpeg.exe`/`ffprobe.exe` as external
  child processes (`SceneForge.Media.Processes.IProcessRunner`) - it never
  links against `libav*`/`libsw*` directly. This is the simplest LGPL
  compliance case (mere aggregation via dynamic invocation, not linking),
  but the obligations below still apply.
- **Before shipping:** copy the exact LGPL license text that ships with
  the chosen FFmpeg build (its own `LICENSE.md`/`COPYING.LGPLv2.1` file)
  into a `licenses\ffmpeg\` folder next to the installed/portable exe, and
  keep a note of exactly which build (source URL + version) was used, so
  the required "written offer" of source/build scripts can point somewhere
  real if ever asked. FFmpeg's own source is already public
  (https://ffmpeg.org), so the offer can simply point there plus the
  specific build's own published build configuration.

## OpenCV / OpenCvSharp (`tools\opencv\OpenCvSharpExtern.dll`)

- Both the `OpenCvSharp4` (managed wrapper) and `OpenCvSharp4.runtime.win`
  (native `OpenCvSharpExtern.dll`) NuGet packages declare **Apache License
  2.0** in their own `.nuspec` (`<license type="expression">Apache-2.0</license>`
  - confirmed directly from the restored 4.13.0.20260627 packages used by
  this project). Permissive - no copyleft, no source-availability
  obligation - but still requires including the license text and
  preserving copyright/attribution notices in the distributed product. If
  a future package upgrade changes this, re-check the new version's
  `.nuspec` rather than assuming it stayed the same.
- **Before shipping:** copy the license files from the restored
  `OpenCvSharp4` and `OpenCvSharp4.runtime.win` NuGet packages (under each
  package's folder in the local NuGet cache, or their project pages) into
  `licenses\opencv\` next to the installed/portable exe.

## Visual C++ Redistributable

Microsoft's official redistributable installer (`VC_redist.x64.exe`),
covered by its own Microsoft license terms presented during that
installer's own execution - **only ever obtain and bundle the genuine
installer directly from Microsoft**; never copy raw `vcruntime140*.dll`/
`msvcp140.dll` files out of a development machine's `System32` folder,
which is not a licensed redistribution method. See
`packaging/vendor/README.md` for where to place it.

## .NET runtime (bundled by the self-contained, single-file publish)

The .NET runtime itself is **MIT-licensed** and explicitly designed for
self-contained redistribution; no additional action is required beyond
optionally including its license text for completeness.

## Packaging checklist (before any build leaves this machine)

1. `packaging/vendor/ffmpeg/` contains an **LGPL-only** shared build, not
   a GPL build.
2. `packaging/vendor/vcredist/VC_redist.x64.exe` is the genuine installer
   from Microsoft, if the installer (`SceneForge.iss`) is being built.
3. A `licenses\` folder with the FFmpeg LGPL text, the OpenCV/OpenCvSharp
   license text(s), and their version/source notes ships next to the exe
   (portable ZIP root and installed directory alike).
4. `docs/PACKAGING_REPORT.md` for this build records which exact FFmpeg
   build (URL + version) and OpenCvSharp/OpenCV package versions were
   used, so this checklist is auditable after the fact.
