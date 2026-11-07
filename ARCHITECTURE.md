# Architecture - DigitalMIDP

## Architectural Overview

DigitalMIDP is a .NET 8 desktop application built using **Clean Architecture** and the **WPF MVVM pattern**.

The primary design goals are:
* Strict separation of concerns between domain rules, use case orchestration, and infrastructure integrations.
* Protection of core business logic from external changes in UI or Autodesk Platform Services (APS) APIs.
* Full testability via dependency inversion and interface-driven design.
* Robust local integration with Autodesk Desktop Connector as a first-class citizen.

## Layer Structure & Dependencies

Dependencies strictly point **inward** toward the Domain:

```
┌────────────────────────────────────────────────────────┐
│               DigitalMIDP (Presentation)               │
│          WPF Views (XAML), ViewModels, Converters      │
└──────────────────────────┬─────────────────────────────┘
                           │ depends on
                           ▼
┌────────────────────────────────────────────────────────┐
│              DigitalMIDP.Infrastructure                │
│    APS REST APIs, OAuth 2.0 PKCE, DPAPI Token Storage, │
│      Desktop Connector Service, Resilience & Caching   │
└──────────────────────────┬─────────────────────────────┘
                           │ implements / depends on
                           ▼
┌────────────────────────────────────────────────────────┐
│               DigitalMIDP.Application                  │
│       Use Cases, Service Interfaces, DTOs, Validators  │
└──────────────────────────┬─────────────────────────────┘
                           │ depends on
                           ▼
┌────────────────────────────────────────────────────────┐
│                 DigitalMIDP.Domain                     │
│         Entities, Value Objects, Domain Exceptions     │
└────────────────────────────────────────────────────────┘
```

---

## Layer Responsibilities

### 1. DigitalMIDP.Domain
**Purpose:** Pure core business entities and rules.
* **Contains:** Entities (`FileItem`, `Folder`, `Hub`, `Project`), Value Objects (`AccessToken`, `RefreshToken`), Domain Exceptions.
* **Forbidden:** External framework references, UI logic, HTTP libraries, filesystem operations.
* **Rule:** Must remain completely independent of external dependencies and APIs.

### 2. DigitalMIDP.Application
**Purpose:** Use case orchestration and application abstractions.
* **Contains:** Use cases (`AuthenticationUseCase`, `FileListingUseCase`, `CreateDeliverableUseCase`, `DocumentNumberUseCase`, `GetDesktopConnectorStatusUseCase`, etc.), service interfaces (`IDataManagementService`, `ITokenProvider`, `IDesktopConnectorService`), DTOs, and input validation rules.
* **Forbidden:** UI logic, direct external SDK implementations.
* **Rule:** Defines *what the system does* without dictating *how it is done*.

### 3. DigitalMIDP.Infrastructure
**Purpose:** External system adapters, API clients, and OS-level integrations.
* **Contains:**
  * Autodesk Platform Services (APS) clients: Authentication (3-legged OAuth + PKCE), Data Management API, BIM 360 Docs custom attributes, and OSS signed S3 upload transfers.
  * Token lifecycle & security: `ApsTokenProvider`, `ProtectedTokenStorage` with Windows DPAPI encryption (`DpapiTokenProtector`).
  * Autodesk Desktop Connector integration: `DesktopConnectorService` (Windows registry checks, filesystem discovery, sync status checks, and local file launch).
  * HTTP Resilience & Caching: `ApsResilienceHandler` (exponential backoff, 429 throttling mitigation), `IApsMetadataCache`.
* **Forbidden:** UI elements or presentation state.

### 4. DigitalMIDP (UI / Presentation)
**Purpose:** User presentation, composition root, and UI state management.
* **Contains:**
  * WPF Views (XAML) and ViewModels (`MainViewModel`, `FilesViewModel`, `CreateDeliverableViewModel`, `EditCustomAttributesViewModel`, etc.).
  * Value converters, UI services, and DI composition (`App.xaml.cs`).
* **MVVM Discipline:** Views are passive and declarative. Zero business logic in code-behind. All user actions driven by `ICommand` / CommunityToolkit `[RelayCommand]`.

---

## Autodesk Desktop Connector Integration

Autodesk Desktop Connector is a **first-class integration**:
* Desktop Connector allows users to interact with ACC files directly on the local filesystem.
* When a user requests to open a file or folder, the application dynamically resolves the local Desktop Connector path first.
* If local file access is available and verified, it is preferred for speed and native application interaction.
* If Desktop Connector is not installed, not running, or the file is not yet synced locally, the application gracefully provides web-based fallback access.

---

## Security Architecture

1. **Authentication:** 3-legged OAuth 2.0 with PKCE via APS `/authentication/v2/authorize` and `/authentication/v2/token`.
2. **Local Token Protection:** Tokens at rest are encrypted using Windows Data Protection API (DPAPI) via `DataProtectionScope.CurrentUser`.
3. **No Cleartext Credentials:** Client secrets and bearer tokens are never logged, checked into source control, or persisted unencrypted.
4. **No Tokens in UI State:** ViewModels never store OAuth tokens. Where a use case takes one, it is fetched from `ITokenProvider` for that call only; `ApsAuthorizationHandler` attaches the current token to each APS request.
5. **Local Diagnostics:** Logs are written to `%LocalAppData%\DigitalMIDP\Logs` by `FileLoggerProvider`, which also redacts anything shaped like a credential before it reaches disk.
