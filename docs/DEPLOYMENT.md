# Deployment & Packaging - DigitalMIDP

DigitalMIDP targets Windows desktop runtimes (`net8.0-windows`).

---

## 1. Prerequisites for Target Machines

- **Operating System**: Windows 10 (Build 19041+) or Windows 11.
- **Runtime**: [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (if deploying framework-dependent).
- **Autodesk Desktop Connector**: Recommended (version 16.x or newer) for local file opening and sync features.

---

## 2. Publishing Builds

### Framework-Dependent Publish
Creates lightweight binaries requiring the .NET 8 Desktop Runtime to be installed on the machine:

```powershell
dotnet publish DigitalMIDP/DigitalMIDP.csproj `
  -c Release `
  -r win-x64 `
  --no-self-contained `
  -o ./publish/framework-dependent
```

### Self-Contained Publish
Bundles the .NET runtime with the application:

```powershell
dotnet publish DigitalMIDP/DigitalMIDP.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -o ./publish/self-contained
```

---

## 3. Post-Publish Verification

Before distributing binaries:
1. Verify `DigitalMIDP.exe` launches on a clean Windows VM.
2. Confirm OAuth loopback callback functions cleanly on the specified port.
3. Confirm DPAPI token storage encrypts tokens successfully under user profile directory.
