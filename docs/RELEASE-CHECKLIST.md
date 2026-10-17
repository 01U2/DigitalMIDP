# Release Checklist - DigitalMIDP

Perform this verification before tagging and releasing a new version of DigitalMIDP.

---

## 1. Code & Build Verification
- [ ] Clean build passes in Release mode with 0 errors:
  ```powershell
  dotnet build DigitalMIDP.sln -c Release
  ```
- [ ] Visual Studio Error List contains 0 compiler errors and 0 blocking analyzer warnings.
- [ ] All unit and integration tests pass:
  ```powershell
  dotnet test DigitalMIDP.Tests/DigitalMIDP.Tests.csproj -c Release
  ```

## 2. Versioning & Documentation
- [ ] `Directory.Build.props` version bumped to the target version.
- [ ] `CHANGELOG.md` updated with release notes and date under `## [vX.Y.Z] - YYYY-MM-DD`.
- [ ] Dedicated release notes document created under `docs/releases/vX.Y.Z.md`.
- [ ] Documentation links and user guides verified.

## 3. Security & Secrets Check
- [ ] Git working copy verified to ensure zero secrets, API keys, or private configs are committed:
  ```powershell
  git status
  ```
- [ ] Confirm `appsettings.json` contains only placeholder credentials.

## 4. Packaging & Smoke Testing
- [ ] Publish standalone binary:
  ```powershell
  dotnet publish DigitalMIDP/DigitalMIDP.csproj -c Release -r win-x64 --self-contained true
  ```
- [ ] Execute binary smoke test on a clean machine:
  - [ ] App launches and displays login screen.
  - [ ] OAuth login completes successfully.
  - [ ] Hubs, projects, and files load.
  - [ ] Desktop Connector status correctly detected.
