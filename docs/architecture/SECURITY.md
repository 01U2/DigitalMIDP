# Security Architecture - DigitalMIDP

This document details the security model, cryptographic controls, and token protection mechanisms in DigitalMIDP.

---

## 1. Authentication: OAuth 2.0 with PKCE

DigitalMIDP operates as a public desktop client interacting with Autodesk Platform Services (APS).

- **Flow**: 3-legged authorization code grant with **Proof Key for Code Exchange (PKCE)** (RFC 7636).
- **Code Challenge**: Cryptographic high-entropy random string generated via `PkceHelper`, hashed with SHA-256 (`code_challenge_method=S256`).
- **Loopback Listener**: `OAuthCallbackListener` dynamically binds to a local loopback port (`http://localhost:8080/callback/`) to receive the authorization response, preventing external interception.

---

## 2. Token Protection at Rest: Windows DPAPI

Storing tokens in plaintext files or registry settings poses a severe security risk. DigitalMIDP enforces local encryption:

- **Technology**: **Windows Data Protection API (DPAPI)** via `System.Security.Cryptography.ProtectedData`.
- **Scope**: `DataProtectionScope.CurrentUser`.
- **Implementation**:
  - `DpapiTokenProtector` encrypts token bytes before writing to `%APPDATA%\DigitalMIDP\tokens.dat`.
  - Only processes running under the same Windows user account can decrypt the data.
  - Machine migration or other user accounts on the same PC cannot read or decrypt the tokens.

---

## 3. In-Memory Protection & Redaction

- `ApsTokenProvider` maintains the active bearer token in memory with serialized refresh guards to prevent concurrent token refresh storms.
- `ApsAuthorizationHandler` automatically injects the `Authorization: Bearer <token>` header for outgoing requests to Autodesk endpoints.
- Loggers are configured to omit sensitive headers, bearer tokens, client IDs, and user profile emails.

---

## 4. Local Execution & Path Traversal Safeguards

- When launching local files via Desktop Connector (`DesktopConnectorService`), all paths are canonicalized (`Path.GetFullPath`) and verified to reside within authenticated user workspace roots before invoking shell process execution.
