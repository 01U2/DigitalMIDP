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
1. User selects Hub and Project.
2. `FilesViewModel` invokes `IFileListingUseCase.GetFolderContentsAsync()`.
3. `DataManagementService` queries APS Data Management API for folder items.
4. Concurrently, `DataManagementService` fetches BIM 360 Docs custom attribute definitions and project naming standards (cached via `IApsMetadataCache`).
5. Items and attributes are merged into `ItemDto` objects and returned to `FilesViewModel`.
6. Grid displays dynamic columns; dropdown filter options are populated with human-readable descriptions.

### Desktop Connector Integration Flow
1. On startup, `GetDesktopConnectorStatusUseCase` calls `DesktopConnectorService` to check Windows Registry for Autodesk Desktop Connector installation.
2. For each project, `DesktopConnectorService` verifies whether a synced workspace folder exists under `%USERPROFILE%\DC\ACCDocs\...`.
3. When user chooses "Open File", `GetLocalFilePathUseCase` resolves the local path and checks file existence and sync status.
4. If local file exists, `DesktopConnectorService` invokes Windows shell process launch; if not, falls back to web browser URL.
