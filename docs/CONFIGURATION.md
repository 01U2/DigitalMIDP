# Configuration Guide - DigitalMIDP

The client and runner load JSON configuration at startup. Restart the affected application after changing it.

---

## 1. Configuration Sources

From lowest to highest precedence: `appsettings.json` beside the executable, `%ProgramData%\DigitalMIDP\autodesk.json`, then `%LocalAppData%\DigitalMIDP\autodesk.json`. Neither environment variables nor user secrets are configuration sources for this setup.

---

## 2. Configuration Schema (`appsettings.json`)

The shipped authentication defaults are:

```json
{
  "Autodesk": {
    "ClientId": "",
    "RedirectUri": "http://localhost:8080/callback/",
    "Scopes": "data:read data:write account:read data:create",
    "AuthorizeEndpoint": "https://developer.api.autodesk.com/authentication/v2/authorize",
    "TokenEndpoint": "https://developer.api.autodesk.com/authentication/v2/token",
    "UserInfoEndpoint": "https://api.userprofile.autodesk.com/userinfo"
  }
}
```

---

## 3. Desktop Connector and runner settings

`DesktopConnector:SyncRootPath` optionally overrides `%USERPROFILE%\DC\ACCDocs`. Supply an absolute local path to the ACCDocs root, not the hub, project, a UNC share or a mapped network drive. For example, merge `"DesktopConnector": { "SyncRootPath": "D:\\DC\\ACCDocs" }` into an override file. The reader checks the path; this setting does not configure Desktop Connector or hydrate files.

Set polling **per project in the runner UI**, not in `appsettings.json`. See the [operator guide](../DigitalMIDP.ManifestRunner/README.md) for enrollment, interval and publication checks.

## Company-specific Autodesk Client ID

Each company registers its own APS Desktop/Native application with callback `http://localhost:8080/callback/` (trailing slash included) and supplies its Client ID. Resolution order (later wins):

1. `appsettings.json` beside the executable - defaults only; `ClientId` is intentionally empty.
2. `%ProgramData%\DigitalMIDP\autodesk.json` - company-wide; deploy with `deploy/Set-DigitalMidpClientId.ps1 -ClientId <id>` (administrator).
3. `%LocalAppData%\DigitalMIDP\autodesk.json` - per-user; written by the first-run setup window.

File format: `{ "Autodesk": { "ClientId": "<id>" } }`. The Client ID is public; never deploy a client secret.

> Development: user secrets are no longer loaded by the app. Put your dev Client ID in `%LocalAppData%\DigitalMIDP\autodesk.json` (or run the app and use the setup window).
