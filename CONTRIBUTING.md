# Contributing

This project follows strict engineering governance. Contributions must align with the repository rules described in the CLAUDE.md file.

## Required standards

- Build on native Windows WPF using .NET 8.
- Keep the codebase offline and local-first.
- Use FFmpeg/FFprobe and OpenCvSharp as the media stack.
- Keep the UI layer separate from the core logic and domain behavior.
- Use async cancellation for every long-running operation.
- Keep memory usage and concurrency bounded.
- Avoid full-video buffering and destructive file handling.
- Write tests first for algorithm changes.
- Benchmark optimizations with evidence.
- Never claim 100% transition accuracy.

## Before you finish work

- Run formatting.
- Run the relevant build steps.
- Run the relevant tests.
- Update documentation when behavior or architecture changes.
- Confirm the current acceptance criteria pass before moving to the next phase.

## Contribution expectations

- Keep changes focused and reviewable.
- Preserve user files and write only to user-selected new output paths.
- Do not add telemetry, remote services, or runtime network dependencies.
- Treat this as a trustworthy desktop application that must not surprise the user with destructive behavior.
