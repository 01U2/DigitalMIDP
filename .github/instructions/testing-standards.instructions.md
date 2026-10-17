---
applyTo: "**/*Test*.cs,**/*Tests.cs"
---

# Testing Standards & Verification Guidance (DigitalMIDP)

Guidance for writing, maintaining, and executing automated tests in `DigitalMIDP.Tests`.

## Framework & Tooling

- Test Framework: **xUnit**
- Assertions: **FluentAssertions**
- Mocking: **NSubstitute**

## Quality & Coverage Expectations

1. **Test Scope & Architecture**:
   - Unit tests mirror the solution structure (`DigitalMIDP.Application.Tests`, `DigitalMIDP.Infrastructure.Tests`, etc.).
   - Test domain models and business logic in isolation without spinning up WPF UI or live network connections.
   - Mock all external dependencies (APS HTTP client, Desktop Connector file system, Windows DPAPI) using interfaces.

2. **Security & Defensive Checks**:
   - **Token Protection**: Verify that authentication tokens are stored using DPAPI (`ProtectedTokenStorage`, `DpapiTokenProtector`) and never serialized in cleartext.
   - **Credential Redaction**: Ensure error messages, HTTP resilience logs, and exception dumps redact bearer tokens and secrets.
   - **Path Traversal Prevention**: Test boundary cases in Desktop Connector local file paths to ensure path traversal attempts are rejected.

3. **WPF & Lifecycle Safety**:
   - Test ViewModels without UI thread attachment where possible.
   - Verify that commands execute properly and update observable properties (`INotifyPropertyChanged`).
   - Validate cancellation tokens (`CancellationToken`) across async methods.

4. **Execution Baselines**:
   - Filtered run: `dotnet test DigitalMIDP.Tests/DigitalMIDP.Tests.csproj --filter <FullyQualifiedName>`
   - Full suite run: `dotnet test DigitalMIDP.Tests/DigitalMIDP.Tests.csproj`
   - Target 0 failed tests and zero blocking warnings.
