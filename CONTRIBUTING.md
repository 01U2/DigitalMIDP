# Contributing to DigitalMIDP

Thank you for your interest in contributing to DigitalMIDP! We welcome contributions that maintain code quality, architectural integrity, and security standards.

---

## 1. Development Principles

- **Clean Architecture**: Adhere strictly to the inward-pointing dependency rule: UI -> Infrastructure -> Application -> Domain. Never let external SDK or UI types leak into Domain or Application layers.
- **WPF MVVM Discipline**: Views must remain passive and declarative. Zero business logic in code-behind. Use CommunityToolkit `[RelayCommand]` and `[ObservableProperty]`.
- **Async/Await Correctness**: Always propagate `CancellationToken`. Never block with `.Result` or `.Wait()`. Avoid `async void` except in relay command event boundaries.
- **Surgical Changes**: Make precise, focused commits that address root causes without speculative refactoring.

---

## 2. Git & Branching Strategy

- **Default Branch**: `develop` is the primary integration branch.
- **Feature Branches**: Branch from `develop` with naming convention:
  - `feature/<short-description>`
  - `fix/<short-description>`
  - `chore/<short-description>`
- **Pull Requests**: Target `develop`. All PRs must pass the test suite and build cleanly with 0 compiler or analyzer warnings.

---

## 3. Pull Request Process

1. Fork or branch from `develop`.
2. Follow coding standards documented in `docs/CODING-STANDARDS.md`.
3. Add or update unit tests in `DigitalMIDP.Tests` for all modified or new functionality.
4. Verify tests and build locally:
   ```powershell
   dotnet build DigitalMIDP.sln
   dotnet test DigitalMIDP.Tests/DigitalMIDP.Tests.csproj
   ```
5. Update `CHANGELOG.md` following the [Keep a Changelog](https://keepachangelog.com/) format.
6. Open a Pull Request referencing the task or issue ID.

---

## 4. Coding & Documentation Standards

- Read [Coding Standards](docs/CODING-STANDARDS.md) before submitting code.
- Read [Testing Strategy](docs/TESTING.md) for test guidelines and mocking patterns.
- Read [Development Setup](docs/DEVELOPMENT.md) for environment configuration.
