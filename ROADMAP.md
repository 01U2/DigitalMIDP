# DigitalMIDP - Project Roadmap

This roadmap tracks past milestones, current capabilities, and planned features for DigitalMIDP.

---

## Completed Milestones

- [x] **Phase 1: Authentication & Token Lifecycle**
  - OAuth 2.0 PKCE 3-legged browser login via APS.
  - In-memory token caching (`ApsTokenProvider`) and secure Windows DPAPI persistence (`ProtectedTokenStorage`).
  - Automated unit test coverage for authentication workflows and PKCE generation.

- [x] **Phase 2: Project & File Explorer**
  - Hub and Project discovery via APS Project API.
  - Project folder and file browsing via WPF DataGrid with sorting and pagination.
  - Column selector and display preferences persistence.

- [x] **Phase 3: Custom Metadata Attributes & Filtering**
  - Reading and displaying BIM 360 Docs custom metadata fields and project naming standards.
  - Filter dropdowns with human-readable option descriptions and fallback handling.
  - Edit custom attributes dialog for file version updates.

- [x] **Phase 4: Desktop Connector Integration**
  - Registry inspection to detect Autodesk Desktop Connector installation.
  - Local project sync detection (`SyncedProject`, `SyncStatus`).
  - Shell execution to open local files or containing folders directly via Desktop Connector.
	
- [x] **Phase 5: File Upload & Versioning**
  - File upload with progress reporting and cancellation support.
  - Automatic versioning of existing files with user confirmation.
  - Conflict resolution for simultaneous edits and uploads.
  - Full batch update workflow for custom metadata attributes.
  - In-line validation of attribute constraints and enum options before committing updates.

- [x] **Phase 6: Deliverable Creation & S3 Upload Verification**
  - Template catalog and deliverable configuration UI.
  - Multi-stage upload: storage reservation -> direct signed S3 transfer -> upload completion.
  - Document number validation against Projects pre-defined naming conventions (`DocumentNumberSegmentValidator`).
  - End-to-end verification against live ACC test tenants.

---

## Active & Upcoming Milestones

- [ ] **Phase 7: Performance & Offline Resilience**
  - Extended caching of folder hierarchies and metadata schemas (`IApsMetadataCache`).
  - Advanced background synchronization status indicators for large datasets.
- [ ] **Phase 8: dotnet 10 Implmentation**

