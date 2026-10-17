# Configuration Guide - DigitalMIDP

DigitalMIDP uses `Microsoft.Extensions.Configuration` to manage application settings and environment overrides.

---

## 1. Configuration Sources

Settings are loaded in the following precedence order:
1. Environment variables
2. User Secrets (`dotnet user-secrets`) - Development only
3. `DigitalMIDP/appsettings.json` - Default file

---

## 2. Configuration Schema (`appsettings.json`)

The primary configuration section is `Autodesk` / `ApsAuthentication`:

```json
{
  "Autodesk": {
    "ClientId": "",
    "RedirectUri": "http://localhost:8080/callback/",
    "Scopes": "data:read data:write data:create bucket:create bucket:read",
    "AuthorizeEndpoint": "https://developer.api.autodesk.com/authentication/v2/authorize",
    "TokenEndpoint": "https://developer.api.autodesk.com/authentication/v2/token",
    "UserInfoEndpoint": "https://api.userprofile.autodesk.com/userinfo"
  },
  "DesktopConnector": {
    "Enabled": true,
    "PreferredPathCheck": true
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "System.Net.Http.HttpClient": "Warning"
    }
  }
}
```

---

## 3. Configuring Development Secrets

Never commit your `ClientId` or test credentials to `appsettings.json`. Instead, use `dotnet user-secrets`:

```powershell
cd DigitalMIDP
dotnet user-secrets init
dotnet user-secrets set "Autodesk:ClientId" "YOUR_APS_CLIENT_ID"
dotnet user-secrets set "Autodesk:RedirectUri" "http://localhost:8080/callback/"
```

Verify secrets are loaded:
```powershell
dotnet user-secrets list
```

## Company-specific Autodesk Client ID

Each company registers its own APS Desktop/Native application with callback `http://localhost:8080/callback/` (trailing slash included) and supplies its Client ID. Resolution order (later wins):

1. `appsettings.json` beside the executable - defaults only; `ClientId` is intentionally empty.
2. `%ProgramData%\DigitalMIDP\autodesk.json` - company-wide; deploy with `deploy/Set-DigitalMidpClientId.ps1 -ClientId <id>` (administrator).
3. `%LocalAppData%\DigitalMIDP\autodesk.json` - per-user; written by the first-run setup window.

File format: `{ "Autodesk": { "ClientId": "<id>" } }`. The Client ID is public; never deploy a client secret.

> Development: user secrets are no longer loaded by the app. Put your dev Client ID in `%LocalAppData%\DigitalMIDP\autodesk.json` (or run the app and use the setup window).
