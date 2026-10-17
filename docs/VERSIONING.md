# Versioning Policy - DigitalMIDP

DigitalMIDP follows [Semantic Versioning (SemVer 2.0.0)](https://semver.org/).

---

## 1. Version Format: `MAJOR.MINOR.PATCH`

- **MAJOR**: Incompatible API, architectural, or breaking workflow changes (e.g. 2.0.0).
- **MINOR**: Backward-compatible functionality additions or significant new features (e.g. 1.1.0).
- **PATCH**: Backward-compatible bug fixes, security patches, or minor internal enhancements (e.g. 1.0.1).

---

## 2. Managing Versions

- The primary version number is maintained in the root `Directory.Build.props` file:
  ```xml
  <PropertyGroup>
    <Version>1.0.0</Version>
    <AssemblyVersion>1.0.0.0</AssemblyVersion>
    <FileVersion>1.0.0.0</FileVersion>
  </PropertyGroup>
  ```
- Every version bump must correspond to an entry in root `CHANGELOG.md` under the heading `## [vX.Y.Z] - YYYY-MM-DD`.
