# Security Policy - DigitalMIDP

## Supported Versions

Security updates are provided for the following releases:

| Version | Supported |
| ------- | --------- |
| 1.0.x   | Yes       |
| < 1.0   | No        |

---

## Security Architecture & Safeguards

### 1. Token Protection at Rest
- User access tokens and refresh tokens are stored locally on the user machine.
- All persistent tokens are encrypted using **Windows Data Protection API (DPAPI)** (`DataProtectionScope.CurrentUser`) via `DpapiTokenProtector`.
- Tokens are never written to disk unencrypted or stored in plain text configuration files.

### 2. OAuth 2.0 with PKCE
- Authentication with Autodesk Platform Services (APS) uses the 3-legged authorization code flow with **Proof Key for Code Exchange (PKCE)**.
- Auth codes and tokens are exchanged dynamically via a local ephemeral loopback listener (`OAuthCallbackListener`).

### 3. Secret Management
- Client credentials and tenant secrets must never be committed to source code or included in repository configuration files.
- During local development, use `dotnet user-secrets` to store `ApsAuthentication:ClientId` and `ApsAuthentication:CallbackUri`.

### 4. Logging & Redaction
- HTTP handlers (`ApsResilienceHandler`, `ApsAuthorizationHandler`) and application loggers must never log:
  - Bearer tokens
  - Authorization headers
  - Refresh tokens
  - Client secrets
  - Personal identifiable information (PII)

### 5. Local File System & Desktop Connector Security
- Desktop Connector path resolution (`GetLocalFilePathUseCase`) must validate and canonicalize paths before delegating to Windows shell execution.
- Path traversal sequences (e.g. `../`, `..\`) or unauthorized system paths must be rejected.

---

## Reporting a Vulnerability

If you discover a security vulnerability in DigitalMIDP:
1. Do **NOT** open a public issue.
2. Email the maintainer directly with a reproduction script and description of the vulnerability.
3. You will receive an initial response within 48 hours, followed by an assessment and patch schedule.
