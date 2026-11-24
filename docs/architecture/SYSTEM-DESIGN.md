# System Design - DigitalMIDP

This document provides a deep-dive architectural view of DigitalMIDP.

---

## 1. Architectural Style: Clean Architecture

DigitalMIDP employs Clean Architecture to ensure that domain business logic is completely isolated from UI technologies (WPF), operating system specifics (Windows Desktop Connector, Registry, DPAPI), and cloud service providers (Autodesk Platform Services).

### Dependency Inversion Rule
Dependencies point strictly inward:
`UI (WPF) -> Infrastructure -> Application -> Domain`

```
┌──────────────────────────────────────────────────────────────────┐
│  Presentation Layer: DigitalMIDP (WPF MVVM)                      │
│  - Views: MainWindow, CreateDeliverableWindow, EditCustomAttr    │
│  - ViewModels: MainViewModel, FilesViewModel, etc.              │
│  - Converters, Stores (ColumnPreferencesStore), App.xaml.cs      │
└─────────────────────────────────┬────────────────────────────────┘
                                  │
┌─────────────────────────────────▼────────────────────────────────┐
│  Infrastructure Layer: DigitalMIDP.Infrastructure               │
│  - Authentication: ApsTokenProvider, ProtectedTokenStorage, DPAPI│
│  - DataManagement: DataManagementService, APS REST Clients       │
│  - DesktopConnector: DesktopConnectorService (Win Registry/Shell)│
│  - Http: ApsAuthorizationHandler, ApsResilienceHandler           │
│  - Caching: ApsMetadataCache                                     │
└─────────────────────────────────┬────────────────────────────────┘
                                  │
┌─────────────────────────────────▼────────────────────────────────┐
│  Application Layer: DigitalMIDP.Application                     │
│  - Use Cases: AuthenticationUseCase, FileListingUseCase,         │
│               CreateDeliverableUseCase, DocumentNumberUseCase,   │
│               GetDesktopConnectorStatusUseCase                   │
│  - Interfaces: IDataManagementService, IDesktopConnectorService  │
│  - DTOs & Validation: DocumentNumberSegmentValidator             │
└─────────────────────────────────┬────────────────────────────────┘
                                  │
┌─────────────────────────────────▼────────────────────────────────┐
│  Domain Layer: DigitalMIDP.Domain                                │
│  - Entities: FileItem, Folder, Hub, Project                      │
│  - Value Objects: AccessToken, RefreshToken                      │
│  - Domain Exceptions: DomainException                            │
└──────────────────────────────────────────────────────────────────┘
```

---

## 2. Component Communication Flows

### Authentication Flow
1. User clicks "Sign In" in `MainViewModel`.
2. `MainViewModel` invokes `IAuthenticationUseCase.LoginAsync()`.
3. `AuthenticationUseCase` calls `AuthenticationService` which spins up a loopback listener (`OAuthCallbackListener`) and opens the system browser to the APS authorization endpoint with PKCE challenge.
4. User completes login in browser; APS redirects to loopback listener with auth code.
5. `AuthenticationService` exchanges auth code and PKCE verifier for tokens via APS Token Endpoint.
6. Tokens are saved securely to disk via `ITokenStorageService` (`ProtectedTokenStorage` using Windows DPAPI) and loaded into `ApsTokenProvider`.
7. `MainViewModel` transitions UI state to Authenticated.

### File Browsing & Custom Attributes Flow
1. The separately signed-in runner schedules a check for each enrolled hub/project. `IManifestPublisher` resolves Templates and reads the previous item tip, if present. `IProjectInventoryReconciler` builds a candidate including folder hierarchy, folder-specific definitions, item identities, paths, version URNs and values keyed by attribute ID.
2. An unchanged scan produces no ACC version. For a change, the publisher rechecks the tip, then creates the reserved item or uploads its next version. An explicit read/validation failure is not an empty inventory. A successful permission-filtered scan, however, can silently omit content.
3. Client hub and project selectors use APS. Project selection starts `ILocalManifestReader.WatchAsync`; Refresh rereads locally and resumes watching. Filesystem notifications and a periodic ten-minute reread detect updates. Neither operation calls the publisher. Older or conflicting revisions cannot roll back the reader's last good in-memory snapshot.
4. `FilesViewModel` builds grid columns from the catalog. Opening the editor uses each selected file's own folder definitions and values, not the grid's display union. Only Save crosses the live [APS validation and write boundary](../integrations/API-INTEGRATION.md).
5. Confirmations overlay only the captured revision and matching file/version in memory. A newer publication wins, even if it sampled the item before Save; revision order is not a write watermark. Changing projects or signing out discards the overlay, and a late response cannot update a different activation.

### Desktop Connector Integration Flow
1. On startup, `GetDesktopConnectorStatusUseCase` calls `DesktopConnectorService` to check Windows Registry for Autodesk Desktop Connector installation.
2. For each project, `DesktopConnectorService` verifies whether a synced workspace folder exists under `%USERPROFILE%\DC\ACCDocs\...`.
3. When user chooses "Open File", `GetLocalFilePathUseCase` resolves the local path and checks file existence and sync status.
4. If local file exists, `DesktopConnectorService` invokes Windows shell process launch; if not, falls back to web browser URL.
