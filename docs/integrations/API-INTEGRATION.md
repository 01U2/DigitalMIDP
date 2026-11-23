# API & External Integrations - DigitalMIDP

This document covers all external integrations utilized by DigitalMIDP, including Autodesk Platform Services (APS) REST APIs and Autodesk Desktop Connector.

---

## 1. Autodesk Platform Services (APS)

Official Documentation: [https://aps.autodesk.com/developer/documentation](https://aps.autodesk.com/developer/documentation)

DigitalMIDP integrates with the following APS API families:

### 1.1 Authentication API (OAuth 2.0)
- **Authorize Endpoint**: `https://developer.api.autodesk.com/authentication/v2/authorize`
- **Token Endpoint**: `https://developer.api.autodesk.com/authentication/v2/token`
- **User Profile Endpoint**: `https://api.userprofile.autodesk.com/userinfo`
- **Flow**: 3-legged authorization code grant with PKCE (`code_challenge_method=S256`).
- **Shipped scopes**: `data:read data:write account:read data:create`. Consent does not grant missing ACC folder or version permissions.

### 1.2 Data Management API & Project API
- **Hub Discovery**: `GET /project/v1/hubs`
- **Project Discovery**: `GET /project/v1/hubs/{hub_id}/projects`
- **Folder Contents**: `GET /data/v1/projects/{project_id}/folders/{folder_id}/contents`
- **Folder Creation**: `POST /data/v1/projects/{project_id}/folders`
- **Item Creation**: `POST /data/v1/projects/{project_id}/items`
- **Version Creation**: `POST /data/v1/projects/{project_id}/versions`

### 1.3 BIM 360 Docs / ACC Custom Attributes
- **Definitions**: `GET /bim360/docs/v1/projects/{project_id}/folders/{folder_id}/custom-attribute-definitions`
- **Batch attribute read**: `POST /bim360/docs/v1/projects/{project_id}/versions:batch-get`
- **Version-scoped attribute update**: `POST /bim360/docs/v1/projects/{project_id}/versions/{versionUrn}/custom-attributes:batch-update`
- **Project Naming Standards**: `GET /bim360/docs/v1/projects/{project_id}/naming-standards`

### 1.4 Object Storage Service (OSS) & Signed S3 Transfers
- **Direct Upload URL Reservation**: `GET /oss/v2/buckets/{bucket_key}/objects/{object_key}/signeds3upload`
- **Binary Content Upload**: Direct `PUT` to AWS S3 signed URL.
- **Upload Completion**: `POST /oss/v2/buckets/{bucket_key}/objects/{object_key}/signeds3upload`

### 1.5 Rate Limiting & Resilience
- Handled through `ApsResilienceHandler` using Polly-style exponential backoff.
- Honors `Retry-After` HTTP headers returned on status code 429.

---

## 2. Autodesk Desktop Connector

Autodesk Desktop Connector is a Windows desktop application that syncs Autodesk Forma files to the local filesystem.

### 2.1 Detection
- **Service**: `DesktopConnectorService` checks the Windows Registry key:
  `HKCU\Software\Autodesk\Desktop Connector`
- Inspects running processes for `DesktopConnector.Applications.Tray.exe`.

### 2.2 Local Workspace Discovery
- Resolves user workspace paths, typically located at:
  `%USERPROFILE%\DC\ACCDocs\<HubName>\<ProjectName>\Project Files\...`
- Checks whether a project is synced locally via `GetSyncedProjectStatusUseCase`.

### 2.3 Local Shell File Launch
- If local file path exists and is confirmed synced, launches the file directly via Windows Process Shell Execution (`Process.Start`).
- If not synced or Desktop Connector is missing, falls back to web browser URL navigation.

## 3. Manifest traffic and consistency

- **Runner check:** Lists paginated folder contents below Project Files, excluding the hidden admin subtree and reserved manifest. It reads project/folder definitions and batches of up to 500 files' attribute values. When a listing lacks a web link, an unchanged item identity, tip, name, parent and path allow reuse of its previous link instead of an item-detail GET. Warm checks still use HTTP. The fixture's **3 cold / 0 warm / 1 changed** counts are file-detail GETs only, not total requests or a production benchmark.
- **Publication:** The publisher validates the previous manifest, compares normalized inventory and rechecks the reserved item's ID and tip before writing. No inventory change means no upload. The check and write are separate requests, not compare-and-swap. Use one runner machine per project; do not rely on APS listings to detect silently hidden files.
- **Client read:** Discovery is live, but grid selection, refresh, filters and opening the editor read the local manifest. The client does not fetch remote inventory to repair a missing, stale or invalid catalog. See [reader recovery](../USER-GUIDE.md).
- **Client Save:** Preflight reads current parent/tip, uncached project/folder schema and options, and original values of changed fields (absent differs from null). A stale baseline or permission failure is not success. Only a matching attribute ID/value response confirms a submitted field; partial confirmation is not batch success. Preflight cannot lock out a concurrent change. A lost response can leave an applied but unconfirmed write: inspect ACC, reconcile, then retry outstanding fields. The client never publishes the manifest.
