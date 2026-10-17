# Development Guide - DigitalMIDP

This guide covers local environment setup, solution structure, and development workflows.

---

## 1. Prerequisites

- **OS**: Windows 10 or Windows 11 (required for WPF desktop application and Desktop Connector APIs).
- **IDE**: Visual Studio 2022 (v17.8+ recommended) with the **.NET Desktop Development** workload installed.
- **SDK**: .NET 8.0 SDK.
- **Autodesk Desktop Connector**: Installed and running for local file open workflows (optional for offline unit tests).
- **Autodesk Platform Services Account**: Client ID registered on [APS Developer Portal](https://aps.autodesk.com/) with 3-legged OAuth callback configured to `http://localhost:8080/callback/` or similar loopback.

---

## 2. Getting Started

### 1. Clone the repository
```powershell
git clone https://github.com/01U2/DigitalMIDP.git
cd DigitalMIDP
git checkout develop
```

### 2. Restore NuGet packages
```powershell
dotnet restore DigitalMIDP.sln
```

### 3. Configure APS User Secrets
Configure your developer client credentials via .NET user secrets (never commit them to `appsettings.json`):
```powershell
cd DigitalMIDP
dotnet user-secrets set "ApsAuthentication:ClientId" "YOUR_ACTUAL_CLIENT_ID"
dotnet user-secrets set "ApsAuthentication:CallbackUri" "http://localhost:8080/callback/"
```

### 4. Build the Solution
```powershell
dotnet build DigitalMIDP.sln
```

### 5. Run the Automated Tests
```powershell
dotnet test DigitalMIDP.Tests/DigitalMIDP.Tests.csproj
```

### 6. Run the Application
Launch via Visual Studio (F5) or via PowerShell:
```powershell
dotnet run --project DigitalMIDP/DigitalMIDP.csproj
```

---

## 3. Solution Overview

The solution consists of five projects:

1. **DigitalMIDP** (`net8.0-windows`): Presentation layer containing Views, ViewModels, converters, and DI composition root.
2. **DigitalMIDP.Application** (`net8.0`): Use cases, DTOs, domain service contracts, and validation rules.
3. **DigitalMIDP.Infrastructure** (`net8.0-windows`): External API implementations (APS, OAuth, DPAPI, Desktop Connector, Caching).
4. **DigitalMIDP.Domain** (`net8.0`): Core domain entities, value objects, domain exceptions.
5. **DigitalMIDP.Tests** (`net8.0-windows`): Comprehensive xUnit test suite with FluentAssertions and NSubstitute.
