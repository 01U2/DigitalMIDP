---
applyTo: "**/*.cs,**/*.xaml"
---

# Engineering Standards (Shared Agent Guidance)

These standards apply across all implementation and validation tasks in DigitalMIDP.

## Change quality

- Make precise, complete, surgical changes.
- Solve root cause, not only visible symptoms.
- Preserve existing behavior unless scope requires change.
- Avoid unrelated modifications.

## Safety and correctness

- Validate inputs and boundary assumptions.
- Use explicit error handling and useful diagnostics.
- Avoid silent failures and success-shaped fallbacks.
- Preserve type safety and clear contracts.

## Maintainability

- Follow Clean Architecture layering and naming conventions.
- Reuse helpers and converters before adding duplicate logic.
- Keep code DRY, readable, and maintainable.
- Add comments only where logic is genuinely non-obvious.

## Performance and efficiency

- Choose pragmatic, allocation-aware approaches.
- Flag potential bottlenecks or expensive patterns.
- Optimize based on measurable relevance to the task.

## Validation expectations

- Run the smallest meaningful test/build/type-check set that covers the change.
- Provide evidence of validation commands and outcomes.
- Explicitly call out untested areas and residual risk.
- For solution-impacting changes, use this baseline:
  - `dotnet build DigitalMIDP.sln`
  - `dotnet test DigitalMIDP.Tests/DigitalMIDP.Tests.csproj`
- For net-new or significantly changed logic, target >=80% coverage of the changed scope when practical and report any justified gap.
- Test data must contain zero hardcoded secrets or PII.

## Security expectations

- Protect credentials and tokens (use Windows DPAPI via `ProtectedTokenStorage`).
- Verify auth, authorization, and data access boundaries.
- Avoid unsafe deserialization, injection vectors, and insecure defaults.
- Report vulnerabilities with severity and reproducibility detail.
- Validate local file paths in Desktop Connector workflows against path traversal before shell execution.

## WPF MVVM and XAML discipline

- Keep strict MVVM boundaries:
  - no business logic in XAML or code-behind;
  - code-behind limited to view wiring and initialization.
- Bind user actions through `ICommand` / CommunityToolkit `[RelayCommand]`.
- Keep ViewModels UI-framework-agnostic and testable without UI instantiation.
- Prefer binding-driven UI behavior over imperative UI logic.
- In bindings, specify `Mode` and `UpdateSourceTrigger` where behavior depends on them.
- Avoid `async void` command patterns except true fire-and-forget relay command event boundaries.
- Surface user-facing errors through ViewModel state properties, not hidden failures.

## UI quality, validation, and accessibility

- Reflect key application states clearly (loading, signed out, error, empty/no-data states).
- Use inline validation messaging for data entry errors.
- Preserve keyboard navigation and focus behavior during UI changes.
- Avoid modal dialogs unless the scenario requires blocking interaction.
