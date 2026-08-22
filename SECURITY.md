# Security Policy

This project is designed as a local Windows desktop application and must not introduce runtime network requirements or external service dependency.

## Security principles

- Keep processing local-first and offline by default.
- Do not collect telemetry, analytics, or usage reports without explicit scope and review.
- Treat all user-provided media and project files as sensitive. Do not expose them to external systems.
- Validate all file paths, input metadata, and media streams before processing.
- Preserve the original user files and never overwrite or delete them without an explicit user-selected output destination.
- Restrict third-party components and review bundled binaries for licensing and security risk before release.

## Safe handling expectations

- Prefer bounded and explicit resource usage for image and video processing.
- Fail safely when a file is malformed, unsupported, or cannot be decoded.
- Never silently write to a path that could overwrite user data.
- Keep documentation and code review aligned with the security posture of the application.

## Reporting concerns

If a security issue is discovered, document the issue, assess the impact, and escalate it for remediation before any public release or distribution decision.
