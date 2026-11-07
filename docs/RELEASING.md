# Releasing DigitalMIDP

Releases are tag-driven. Pushing a `vMAJOR.MINOR.PATCH` tag on `main` runs
[`.github/workflows/release.yml`](../.github/workflows/release.yml), which:

1. Verifies the tagged commit is on `main`.
2. Restores, builds (Release) and tests the solution.
3. Stamps the tag version into the app (`DigitalMIDP.exe`) and the MSI (`-p:Version=x.y.z`).
4. Publishes a GitHub Release with `DigitalMIDP-Setup-x.y.z.msi` attached.

## Branches

| Branch | Purpose |
|---|---|
| `develop` | Default integration branch. Feature/fix/chore PRs target it. |
| `main` | Release branch. Only receives merges from `develop`; every release tag is created here. |

## Release steps

1. Make sure `develop` builds and all tests pass:
   ```powershell
   dotnet build DigitalMIDP.sln
   dotnet test DigitalMIDP.Tests/DigitalMIDP.Tests.csproj
   ```
2. Add a version section to `CHANGELOG.md`, e.g. `## [0.2.0] - 2026-11-07`, summarising the release.
3. Open a PR from `develop` to `main` and merge it.
4. Tag `main` and push the tag:
   ```powershell
   git checkout main
   git pull
   git tag v0.2.0
   git push origin v0.2.0
   ```
5. Watch the **Release** run in GitHub Actions. When it succeeds, the release appears under
   **Releases** with the MSI attached.

## Versioning rules

- Use [Semantic Versioning](https://semver.org/): `vMAJOR.MINOR.PATCH` only (no suffixes).
- Windows Installer compares only the first three version fields. Every release must increase
  one of them, otherwise `MajorUpgrade` will not replace the installed version.
- Local builds without `-p:Version` default to `0.1.0` (see `DigitalMIDP.Installer.wixproj`).

## Building the MSI locally

```powershell
dotnet build DigitalMIDP.Installer/DigitalMIDP.Installer.wixproj -c Release -p:Version=0.2.0
```

Output: `DigitalMIDP.Installer\bin\x64\<Configuration>\en-US\DigitalMIDP-Setup.msi`
(Debug builds go to `bin\x64\Debug\en-US`).

## Fixing a failed release

If the workflow fails, delete the tag, fix the issue on `develop`, merge to `main`, and re-tag:

```powershell
git tag -d v0.2.0
git push origin :refs/tags/v0.2.0
```

If a GitHub Release was already created, delete it in the GitHub UI before re-tagging.
