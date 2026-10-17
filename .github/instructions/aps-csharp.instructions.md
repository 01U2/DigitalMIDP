---
applyTo: "**/*.cs"
---

# APS and C# Implementation Guidance

Shared guidance for Autodesk Platform Services and C#/.NET tasks.

## Autodesk Platform Services guidance

- Verify required APS products, endpoints, and OAuth scopes against official documentation (https://aps.autodesk.com/developer/documentation).
- Keep client credentials and refresh tokens out of logs and source.
- Protect runtime tokens using Windows DPAPI (`DpapiTokenProtector`).
- Handle token renewal, expiry, and retry behavior explicitly.
- Design external calls for rate-limit handling (HTTP 429) and transient-failure retries (`ApsResilienceHandler`).
- Capture correlation identifiers for diagnostics across APS interactions.

## C#/.NET guidance

- Follow Clean Architecture: Domain -> Application -> Infrastructure -> UI.
- Prefer dependency injection and interface-driven design.
- Use cancellation tokens and async patterns consistently.
- Avoid blocking calls (`.Result`, `.Wait()`) in async flows.
- Handle exceptions at clear boundaries; enrich logs with context without logging sensitive tokens.
- Keep DTO/domain separation clear and stable.
- Validate serialization contracts and nullability assumptions.

## Desktop Connector guidance

- Treat Desktop Connector as a first-class local capability.
- Detect installation via Windows Registry and running tray processes.
- Resolve and canonicalize local paths before shell execution; fallback to web browser if local file is un-synced or unavailable.
