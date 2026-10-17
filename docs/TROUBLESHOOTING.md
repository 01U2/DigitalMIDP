# Troubleshooting Guide - DigitalMIDP

Common issues encountered during development and runtime, with resolution steps.

---

## 1. Authentication Issues

### Problem: OAuth browser window opens, but callback hangs or times out
- **Cause**: Port conflict on the local loopback listener (e.g. `http://localhost:8080/callback/`) or Windows Defender Firewall blocking incoming local connections.
- **Solution**:
  1. Check if another process is listening on the port: `Get-NetTCPConnection -LocalPort 8080`.
  2. Verify that the `RedirectUri` registered in the Autodesk Developer Portal matches the application configuration exactly (including trailing slash).

### Problem: `InvalidTokenException` or repeated 401 Unauthorized
- **Cause**: Expired refresh token or corrupted DPAPI token cache.
- **Solution**:
  1. Trigger manual logout in the UI or delete the local DPAPI encrypted token cache file in `%APPDATA%\DigitalMIDP`.
  2. Re-authenticate via the sign-in prompt.

---

## 2. Desktop Connector Issues

### Problem: Desktop Connector indicates "Not Installed" or "Not Running"
- **Cause**: Desktop Connector service is not running or registry keys are inaccessible.
- **Solution**:
  1. Verify Autodesk Desktop Connector process (`DesktopConnector.Applications.Tray.exe`) is running in Task Manager.
  2. Check Windows registry key: `HKCU\Software\Autodesk\Desktop Connector`.

### Problem: Local file open launches web browser instead of local file
- **Cause**: The project or folder is not yet synced locally by Desktop Connector.
- **Solution**:
  1. In Windows File Explorer, right-click the file/folder in Autodesk Docs and select "Always keep on this device".
  2. Wait for the sync status icon to turn green, then retry.

---

## 3. Build & Runtime Errors

### Problem: `HttpRequestException` (429 Too Many Requests)
- **Cause**: Autodesk Platform Services rate limiting.
- **Solution**: The `ApsResilienceHandler` automatically retries requests using exponential backoff. If limits persist, reduce request concurrency or cache folder listings.
