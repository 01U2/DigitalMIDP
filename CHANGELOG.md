# Changelog

All notable changes to the DigitalMIDP project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Policy

- You should append-only log: do not rewrite past entries except when correcting factual errors.
- Entry heading format: `## [YYYY-MM-DD] - <short title>`
- Ensure your entries are concise, factual, and verified.

---

## [2026-10-22] - Main window starts maximized

### Changed
- `MainWindow` now opens with `WindowState="Maximized"`. Restoring the window returns it to 1200x700.

---

## [2026-10-22] - Per-user installer

### Changed
- MSI installer now uses `Scope="perUser"` and installs to `%LOCALAPPDATA%\Programs\DigitalMIDP`, so no admin elevation is required. Start Menu shortcut goes to the per-user Start Menu.
- Suppressed ICE38/ICE64/ICE91 validation for the per-user layout.

### Added
- `InstallFolderCleanup` component that removes the install folder on uninstall.

---

## [2026-10-21] - Build warnings and open-source readiness

### Fixed
- All build warnings (31 to 0), including a latent bug in `FolderDto.ToEntity` that passed the parent folder id as the project id.

### Removed
- Unused `Autodesk.DataManagement` and `Autodesk.SDKManager` packages from all projects. The Application layer now references `Microsoft.Extensions.Logging.Abstractions` directly instead of receiving it through those packages.

### Changed
- `SECURITY.md` describes what local logs contain and routes vulnerability reports through GitHub private vulnerability reporting.
- Code comments no longer refer to a `CLAUDE.md` file that is not part of the repository.

---

## [2026-10-19] - Pre-production review: logging, APS call reduction, tokens out of the UI

### Added
- `FileLoggerProvider`: persistent daily log at `%LocalAppData%\DigitalMIDP\Logs\DigitalMIDP-yyyyMMdd.log` (14-day retention, credentials redacted). Level/retention overridable via `Logging:File:MinimumLevel` / `Logging:File:RetentionDays`.
- Session-start log line with app version, OS, runtime, machine name, Windows user, culture, time zone and log path; fatal startup errors and application exit are now logged to file.

### Changed
- Auto-refresh is paused while the main window is minimised or the Windows session is locked, with a single catch-up refresh on resume.
- Hub and project lists are cached for five minutes (cleared on sign-out) instead of being refetched on every sign-in and hub switch.

### Security
- ViewModels no longer hold OAuth tokens: the `AccessToken`/`RefreshToken`/`CurrentAccessToken` properties were removed. Where a use case still takes an access token, the ViewModel fetches a current one from `ITokenProvider` for that single call, and the deliverable batch request gets its token in `MainWindow` at the moment it runs. Application and Infrastructure signatures are unchanged.

---

## [2026-10-15] - Company-specific Autodesk Client ID

### Added
- External APS configuration: `%ProgramData%\DigitalMIDP\autodesk.json` (company-wide) and `%LocalAppData%\DigitalMIDP\autodesk.json` (per-user, highest precedence).
- First-run `ClientIdSetupWindow` / `ClientIdSetupViewModel` shown when no valid Client ID is configured.
- `deploy/Set-DigitalMidpClientId.ps1` for installer/IT deployment of a company Client ID.
- `ApsClientConfigurationTests` covering precedence, validation, persistence, and the setup ViewModel.

### Changed
- Startup no longer fails fatally when the Client ID is missing; it prompts for setup instead.

### Security
- Removed the development Client ID from `appsettings.json`. No client secret is used (PKCE public client).

---

## [2026-10-15] - Adopt project branding assets

### Added
- Added the supplied ICO application icon and PNG logo to the WPF application resources.
- Applied the standard icon to the executable and application windows, and added the PNG logo to the main window header.

---

## [2026-10-11] - Composite-key deliverable number allocation

### Changed
- Document numbers are now allocated per numbering group rather than per folder. `DocumentNumberUseCase` parses each existing number's composite key (all naming-standard segments except the trailing sequential one) and takes the maximum sequence only from the group matching the new file's key, so a folder holding `DR-ARC-S0-0007` and `DR-ARC-S1-0025` now allocates the next `DR-ARC-S0` number as `0008` instead of `0026`.
- Sequence width is resolved within the group too, so one group's wider sequences no longer widen unrelated groups.
- The conflict-suggestion path in `CheckAvailabilityAsync` advances within the proposed number's own group.

### Fixed
- Groups not yet present in a discipline folder now start at `0001` instead of continuing a folder-wide maximum.
- File extensions never influence sequence selection or grouping; numbers are compared as delimiter-joined stems.

---

## [2026-10-10] - Filter dropdown descriptions implementation

### Summary
- Enhanced file filter dropdowns to display human-readable descriptions instead of raw attribute codes.
- Added description mappings for custom attribute options while preserving backward compatibility.

### Added
- `FilterOptionViewModel.Description` property to hold metadata option descriptions.

### Changed
- `FileFilterViewModel.Rebuild`: now extracts option descriptions from `MetadataFieldDisplayDto.Options` and filters out null/whitespace option keys before building filter view models.

---

## [2026-10-10] - Deliverable creation title entries

### Changed
- New Deliverable accepts one Deliverable Title per document, writes it directly to Description, and retains entries when quantity changes.
- Project-defined Title1/Title2/Title3 fields remain shared metadata and do not determine Description.

### Fixed
- The wizard reports when a project has no Description attribute for Deliverable Title.

### Security
- Creation no longer includes custom-attribute values directly in write or per-attribute rejection log messages.

## [2026-10-10] - Resolve deliverable title from related metadata

### Fixed
- New Deliverable now resolves each document's title attribute from related project and folder custom attributes, not solely from naming-standard metadata; a field named Description or Deliverable Title is accepted.
- Shared inputs include related custom attributes without duplicating naming-standard fields or treating generic Title as Deliverable Title.
