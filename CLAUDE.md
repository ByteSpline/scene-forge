# CLAUDE.md

This repository is governed by the following non-negotiable rules. They apply to all work, reviews, and future changes.

## Non-negotiable rules

1. Native Windows desktop application only. The product must be implemented as a native Windows WPF application on .NET 8.
2. No Electron, no browser-based UI, no web server, no cloud API dependency, no telemetry, no analytics, and no runtime network requirement.
3. The media stack is FFmpeg/FFprobe and OpenCvSharp. Any media processing must use these technologies as the basis of the implementation.
4. Maintain clean architecture with UI concerns separated from core logic, domain rules, and processing pipelines.
5. Every long-running or potentially blocking operation must support async cancellation and cooperative shutdown.
6. Memory and concurrency must be bounded. Do not create unbounded queues, unbounded caches, or unbounded worker fan-out.
7. No full-video buffering in memory or on disk for normal processing flows. Stream, process, and discard data within bounded windows.
8. Algorithms must follow test-first development. New or changed algorithmic behavior must be covered by tests before implementation is considered complete.
9. Benchmark every optimization before and after the change, and include evidence of the impact in the relevant documentation or review notes.
10. Never claim 100% transition accuracy. Accuracy must be described as measured, bounded, and context-aware, never absolute.
11. Preserve user files at all times. Never delete, overwrite, or mutate input files in place.
12. All outputs must be written to a user-selected new path. The application must never silently replace or overwrite a source file.
13. Before ending each task, run formatting, build, and all relevant tests.
14. Update documentation whenever behavior changes, especially around architecture, processing rules, risk, and user-visible workflows.
15. Do not proceed to a later phase while the current acceptance criteria fail. Resolve the current phase before advancing.

## Additional repository constraints

- Treat this repository as a local-first, offline-capable desktop system.
- Prefer deterministic, explainable processing flows over opaque automation.
- Keep user trust first: preserve data, minimize destructive behavior, and avoid hidden external dependencies.
- Do not implement product features in this repository as part of this file creation task. This document defines constraints and governance only.

## Working expectations

- Make decisions with the system constraints and user safety in mind.
- Prefer explicit, reviewable code paths over magic behavior.
- When a requirement conflicts with these rules, the rule wins.
- When behavior changes, update the related documentation so the repository remains accurate and trustworthy.
