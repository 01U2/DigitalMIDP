# Data Model - DigitalMIDP

This document describes the core domain entities, value objects, and application Data Transfer Objects (DTOs) used across DigitalMIDP.

---

## 1. Domain Entities (`DigitalMIDP.Domain`)

### `Project`
Represents an Autodesk Forma (ACC) or BIM 360 project.
- `Id`: string (unique project identifier, e.g. `b.123456...`)
- `Name`: string
- `HubId`: string
- `ProjectType`: string (e.g. `acc`, `bim360`)

### `Folder`
Represents a folder within an ACC project directory tree.
- `Id`: string
- `Name`: string
- `ParentFolderId`: string?
- `ProjectId`: string
- `IsTopLevel`: bool

### `FileItem`
Represents a document/file item inside a folder.
- `Id`: string (APS Item ID, e.g. `urn:adsk.wipprod:dm.lineage:...`)
- `Name`: string
- `FolderId`: string
- `CurrentVersion`: int
- `Size`: long
- `LastModifiedAt`: DateTimeOffset
- `LastModifiedBy`: string

---

## 2. Value Objects (`DigitalMIDP.Domain`)

### `AccessToken`
Represents an APS Bearer token.
- `Value`: string
- `ExpiresAt`: DateTimeOffset
- `IsValid`: bool (checks current time against expiration window)

### `RefreshToken`
Represents an APS OAuth refresh token used to acquire new access tokens.
- `Value`: string

---

## 3. Application DTOs (`DigitalMIDP.Application.Dtos`)

- **`ItemDto`**: Enriched representation of `FileItem` carrying dynamic BIM 360 custom attributes (`IReadOnlyDictionary<string, string?> CustomAttributes`), sync status, and local file path.
- **`CustomAttributeUpdateDto`**: Payload for updating custom attribute values on a specific file version.
- **`DeliverableTemplateDto`**: Metadata definition for creating new documents from templates.
- **`DocumentNumberDto`**: Segments and joined string conforming to ISO 19650 project naming standard.
- **`SyncedProject`**: DTO mapping an ACC cloud project to its corresponding local Desktop Connector path and `SyncStatus`.
