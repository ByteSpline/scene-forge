# Architecture Decisions

This document captures the key architectural decisions that are non-negotiable for the project.

## Decision 1: Native Windows WPF on .NET 8

The application must be a native Windows desktop application using WPF and .NET 8. This decision avoids browser, Electron, and server-based architecture drift and keeps the user experience aligned with a local desktop-first product.

## Decision 2: Clean architecture boundaries

The user interface must remain separate from core logic, media processing, and processing rules. This reduces coupling, keeps business logic testable, and ensures media processing is not mixed directly into UI concerns.

## Decision 3: FFmpeg/FFprobe and OpenCvSharp as the media stack

The media pipeline is rooted in FFmpeg/FFprobe and OpenCvSharp. These components define the processing foundation and are treated as first-class technical dependencies for media decoding, probing, and algorithmic analysis.

## Decision 4: Offline-first and no runtime network requirement

The application must not require cloud APIs, web services, telemetry, or runtime network access to perform its essential functions. This keeps the software local, private, and predictable for users.

## Decision 5: Safe, bounded processing

All long running work must support async cancellation. Memory and concurrency must be explicit and bounded. Full-video buffering is prohibited in normal processing paths.

## Decision 6: User data protection

The application must preserve user files and never overwrite or delete inputs. Outputs must be written to a user-selected new destination.

## Decision 7: Evidence-based development

Algorithmic changes must be test-first. Optimizations must be benchmarked. Claims about accuracy must be carefully qualified and never stated as absolute.

## Decision 8: Documentation and quality gates

Behavior changes require documentation updates. Formatting, build, and relevant tests must pass before the work is considered finished, and no later phase may begin while earlier acceptance criteria remain unresolved.
