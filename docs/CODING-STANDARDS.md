# Coding Standards - DigitalMIDP

These coding standards are enforced across all C# and XAML development in DigitalMIDP.

---

## 1. Architectural Guardrails (Clean Architecture)

- **Dependency Inversion**: Dependencies point strictly inward: `UI -> Infrastructure -> Application -> Domain`.
- **Domain Independence**: The `DigitalMIDP.Domain` project must never reference any external libraries, UI assemblies, or infrastructure concerns.
- **Contract-First Design**: Define service interfaces in `DigitalMIDP.Application` and implement them in `DigitalMIDP.Infrastructure`.
- **DTO Isolation**: Never pass domain entities directly to Views or API responses. Map between entities and DTOs using explicit mapping extensions.

---

## 2. C# Language & Runtime Conventions

### Async & Concurrency
- **DO**: Use `async`/`await` for all I/O, network, and disk operations.
- **DO**: Propagate `CancellationToken` through all asynchronous call chains.
- **DO**: Use `ConfigureAwait(false)` in library/infrastructure code where synchronization context is not required.
- **DON'T**: Call `.Result` or `.Wait()` on asynchronous tasks (causes UI thread deadlocks).
- **DON'T**: Write `async void` methods (leaks unhandled exceptions). Use `async Task` or relay commands.

### Type Safety & Nullability
- **DO**: Enable and respect C# Nullable Reference Types (`#nullable enable`).
- **DO**: Treat compiler and analyzer warnings in the Error List as blocking.
- **DON'T**: Use the null-forgiving operator (`!`) without an explicit runtime assertion or comment explaining why null is impossible.

### Dependency Injection
- **DO**: Inject dependencies via constructor injection.
- **DO**: Register services using the extension method `ServiceCollectionExtensions.AddApsServices(...)`.
- **DON'T**: Use Service Locator patterns (`IServiceProvider.GetService`) inside business classes or ViewModels.

---

## 3. WPF MVVM & Presentation Standards

- **View-ViewModel Decoupling**: Views (XAML) are declarative and passive. Zero business logic in code-behind files (`*.xaml.cs`).
- **Commands**: Bind user actions using CommunityToolkit.Mvvm `[RelayCommand]` attributes.
- **Observable Properties**: Use CommunityToolkit.Mvvm `[ObservableProperty]` for automatic `INotifyPropertyChanged` implementation.
- **Resource Dictionaries**: Keep UI styles, colors, and templates in shared resource dictionaries rather than hardcoding colors/fonts in individual controls.
- **Value Converters**: Keep converters small, stateless, and defensive against null values (e.g. `CustomAttributeValueConverter`).

---

## 4. Security & Logging Standards

- **DO NOT** commit secrets, client secrets, or private keys to source control.
- **DO NOT** log access tokens, refresh tokens, authorization headers, or sensitive user data.
- **DO**: Log entry/exit of significant operations and error states using structured logging (`_logger.LogInformation`, `_logger.LogError`).
- **DO**: Sanitize and validate local file paths in Desktop Connector integrations to prevent directory traversal attacks.
