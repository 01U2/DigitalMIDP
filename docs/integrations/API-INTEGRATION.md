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
- **Required Scopes**: `data:read`, `data:write`, `data:create`, `bucket:create`, `bucket:read`.

### 1.2 Data Management API & Project API
- **Hub Discovery**: `GET /project/v1/hubs`
- **Project Discovery**: `GET /project/v1/hubs/{hub_id}/projects`
- **Folder Contents**: `GET /data/v1/projects/{project_id}/folders/{folder_id}/contents`
- **Folder Creation**: `POST /data/v1/projects/{project_id}/folders`
- **Item Creation**: `POST /data/v1/projects/{project_id}/items`
- **Version Creation**: `POST /data/v1/projects/{project_id}/versions`

### 1.3 BIM 360 Docs / ACC Custom Attributes
- **Definitions**: `GET /bim360/docs/v1/projects/{project_id}/folders/{folder_id}/custom-attribute-definitions`
- **Batch Version Update**: `POST /bim360/docs/v1/projects/{project_id}/versions:batch-update`
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
