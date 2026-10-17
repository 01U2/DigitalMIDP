# Testing Strategy - DigitalMIDP

This document outlines the testing conventions, tools, and practices for DigitalMIDP.

---

## 1. Test Architecture & Frameworks

- **Framework**: [xUnit](https://xunit.net/)
- **Assertions**: [FluentAssertions](https://fluentassertions.com/)
- **Mocking**: [NSubstitute](https://nsubstitute.github.io/)
- **Test Project**: `DigitalMIDP.Tests/DigitalMIDP.Tests.csproj`

---

## 2. Directory Layout

The test suite mirrors the folder structure of the production projects:

```
DigitalMIDP.Tests/
├── Application/
│   └── UseCases/                 # Use case tests (AuthenticationUseCaseTests, FileListingUseCaseTests, etc.)
├── Infrastructure/
│   ├── Authentication/           # Auth service and token storage tests
│   ├── Caching/                  # ApsMetadataCache tests
│   ├── DataManagement/           # DataManagementService integration tests
│   ├── DesktopConnector/        # DesktopConnectorService tests
│   ├── Http/                     # ApsResilienceHandler, ApsAuthorizationHandler tests
│   └── Mappings/                 # DTO mapping tests
├── UI/
│   ├── CompositionRootTests.cs   # DI container validation tests
│   ├── Converters/               # Value converter unit tests
│   └── ViewModels/               # ViewModel tests (FilesViewModelTests, MainViewModelTests, etc.)
└── Fixtures/
    └── TestFixtures.cs           # Reusable DTO and mock builders
```

---

## 3. Testing Rules & Best Practices

1. **Arrange-Act-Assert (AAA)**: Structure every test cleanly into AAA phases.
2. **Mocking External APIs**: Never execute live HTTP calls against Autodesk Platform Services in unit tests. Mock `IDataManagementService` and `ITokenProvider` using NSubstitute.
3. **Async Test Execution**: All asynchronous methods must be tested using `async Task` test methods and awaited properly.
4. **Coverage Baseline**:
   - For net-new logic and bug fixes, aim for >= 80% branch coverage of the modified scope.
   - Run coverage via:
     ```powershell
     dotnet test DigitalMIDP.Tests/DigitalMIDP.Tests.csproj --collect:"XPlat Code Coverage"
     ```
5. **No Hardcoded Secrets**: Ensure test fixtures contain zero real secrets, tenant tokens, or real user identifiers.
