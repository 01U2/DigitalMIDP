using DigitalMIDP.Application.UseCases;
using DigitalMIDP.Infrastructure;
using DigitalMIDP.Infrastructure.Logging;
using DigitalMIDP.Services;
using DigitalMIDP.ViewModels;
using DigitalMIDP.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;

namespace DigitalMIDP;

public partial class App : System.Windows.Application
{
    private IServiceProvider? _serviceProvider;
    private FileLoggerProvider? _fileLoggerProvider;

    /// <summary>
    /// Gets the application service provider for resolving dependencies.
    /// </summary>
    public static IServiceProvider Services => ((App)Current)._serviceProvider ?? throw new InvalidOperationException("Service provider not initialized");

    /// <summary>
    /// Shuts the container down on exit, which is what finally stops <see cref="FilesViewModel"/>'s
    /// dispatcher timers and zeroes the protected tokens.
    ///
    /// <para>
    /// Neither happened before. WPF does not dispose a window's DataContext, and the ViewModels are
    /// registered for the application's lifetime, so a running auto-refresh timer simply went down
    /// with the process - which is survivable at exit, but meant nothing in the application ever
    /// released these resources, and left the timers running through a sign-out too.
    /// </para>
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            // Disposing the container is all that is needed, and is deliberately the ONLY thing done
            // here. The container disposes every IDisposable it created, which covers both:
            //
            //   * MainViewModel (registered AddScoped and resolved from the root provider, so the
            //     root scope tracks it) - whose Dispose cascades into FilesViewModel and stops its
            //     dispatcher timers and any in-flight load;
            //   * the singletons - ProtectedTokenStorage, which zeroes its token buffers, and
            //     ApsTokenProvider, which releases its refresh semaphore.
            //
            // Resolving MainViewModel explicitly first would be worse, not safer: GetService
            // *constructs* one if the main window was never opened, so an application that failed
            // during startup would build a ViewModel purely in order to dispose it.
            _serviceProvider?.GetService<ILogger<App>>()?.LogInformation(
                "Application exiting (exit code {ExitCode})", e.ApplicationExitCode);

            (_serviceProvider as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            // Never let cleanup turn a normal exit into a crash dialog.
            System.Diagnostics.Debug.WriteLine($"[WARN] Shutdown cleanup failed: {ex}");
        }
        finally
        {
            // Registered as an instance, so the container does not own it - closed last, after
            // everything that might still log during disposal.
            _fileLoggerProvider?.Dispose();
        }

        base.OnExit(e);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            base.OnStartup(e);

            var configuration = LoadConfiguration();
            _fileLoggerProvider = CreateFileLoggerProvider(configuration);

            if (!ApsClientConfiguration.HasValidClientId(configuration))
            {
                if (!RunClientIdSetup(configuration))
                {
                    Shutdown(0);
                    return;
                }

                configuration = LoadConfiguration();
            }

            // Configure and build the dependency injection container
            var services = new ServiceCollection();
            ConfigureServices(services, configuration, _fileLoggerProvider);
            _serviceProvider = services.BuildServiceProvider();

            var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
            LogEnvironment(logger, _fileLoggerProvider);
            logger.LogInformation("Application started successfully");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FATAL] Startup Error: {ex}");

            // The container may not exist yet, so the file sink is written to directly - a failed
            // startup on a user's machine is exactly what support needs to find in the log.
            try
            {
                (_fileLoggerProvider ?? new FileLoggerProvider(FileLoggerProvider.DefaultDirectory))
                    .CreateLogger(typeof(App).FullName!)
                    .LogCritical(ex, "Fatal startup error");
            }
            catch
            {
                // Logging must never mask the original failure.
            }

            MessageBox.Show(
                $"Fatal Startup Error:\n\n{ex.Message}\n\nDetails:\n{ex.StackTrace}",
                "Application Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            Environment.Exit(1);
        }

        // Handle unhandled exceptions in the UI thread
        DispatcherUnhandledException += (sender, args) =>
        {
            var logger = _serviceProvider?.GetService<ILogger<App>>();
            logger?.LogError(args.Exception, "Unhandled UI exception occurred");
            System.Diagnostics.Debug.WriteLine($"[ERROR] {args.Exception}");

            MessageBox.Show(
                $"An error occurred:\n\n{args.Exception.Message}\n\nStack Trace:\n{args.Exception.StackTrace}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );

            args.Handled = true;
        };

        // Handle unhandled exceptions in background threads
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            var logger = _serviceProvider?.GetService<ILogger<App>>();
            logger?.LogError(ex, "Unhandled background thread exception");
            System.Diagnostics.Debug.WriteLine($"[BACKGROUND ERROR] {ex}");
        };
    }

    /// <summary>
    /// Creates the on-disk log sink. Level and retention can be overridden per machine through the
    /// usual configuration files (e.g. <c>"Logging": { "File": { "MinimumLevel": "Debug" } }</c> in
    /// <c>%LocalAppData%\DigitalMIDP\autodesk.json</c>) when support needs more detail.
    /// </summary>
    private static FileLoggerProvider? CreateFileLoggerProvider(IConfiguration configuration)
    {
        try
        {
            var minimumLevel = Enum.TryParse<LogLevel>(configuration["Logging:File:MinimumLevel"], ignoreCase: true, out var level)
                ? level
                : LogLevel.Information;

            var retentionDays = int.TryParse(configuration["Logging:File:RetentionDays"], out var days)
                ? days
                : FileLoggerProvider.DefaultRetentionDays;

            return new FileLoggerProvider(FileLoggerProvider.DefaultDirectory, minimumLevel, retentionDays);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WARN] File logging unavailable: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// One line describing the machine this session runs on, so a log file sent in by a user says
    /// which build, OS and account it came from without anyone having to ask.
    /// </summary>
    private static void LogEnvironment(ILogger logger, FileLoggerProvider? fileLoggerProvider)
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(App).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? assembly.GetName().Version?.ToString()
                      ?? "unknown";

        logger.LogInformation(
            "Session start: {AppName} {AppVersion} | OS {OsDescription} ({OsArchitecture}) | Runtime {Runtime} ({ProcessArchitecture}) | " +
            "Machine {MachineName} | User {UserDomain}\\{UserName} | Culture {Culture}/{UICulture} | TimeZone {TimeZone} | " +
            "CPUs {ProcessorCount} | PID {ProcessId} | Log {LogFile}",
            AppConstants.AppName,
            version,
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.ProcessArchitecture,
            Environment.MachineName,
            Environment.UserDomainName,
            Environment.UserName,
            System.Globalization.CultureInfo.CurrentCulture.Name,
            System.Globalization.CultureInfo.CurrentUICulture.Name,
            TimeZoneInfo.Local.Id,
            Environment.ProcessorCount,
            Environment.ProcessId,
            fileLoggerProvider?.CurrentFilePath ?? "(file logging unavailable)");
    }

    private static IConfigurationRoot LoadConfiguration() =>
        ApsClientConfiguration.Build(
            AppContext.BaseDirectory,
            ApsClientConfiguration.CompanyConfigPath,
            ApsClientConfiguration.UserConfigPath);

    /// <summary>
    /// Shows the first-run Client ID window. Returns true when a Client ID was saved.
    /// </summary>
    private bool RunClientIdSetup(IConfiguration configuration)
    {
        // The dialog is the only window at this point; closing it must not end the application
        // before MainWindow (StartupUri) is created.
        var previousMode = ShutdownMode;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            var viewModel = new ClientIdSetupViewModel(
                ApsClientConfiguration.UserConfigPath,
                configuration["Autodesk:RedirectUri"] ?? string.Empty);
            return new ClientIdSetupWindow(viewModel).ShowDialog() == true;
        }
        finally
        {
            ShutdownMode = previousMode;
        }
    }

    /// <summary>
    /// The composition root, with configuration supplied rather than read from disk.
    ///
    /// <para>
    /// Split out from the overload above so a test can build the real container and check that every
    /// registration actually resolves. A broken DI graph - a missing registration, a service whose
    /// lifetime cannot satisfy a dependency - otherwise only shows up when the application is
    /// launched, which is the slowest possible place to find out.
    /// </para>
    /// </summary>
    internal static void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration,
        ILoggerProvider? fileLoggerProvider = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton(configuration);

        // Add logging with console provider
        services.AddLogging(logging =>
        {
            logging.AddDebug(); // Output window
            logging.AddConsole();

            // Persistent log on the user's machine. Optional so tests building the real container
            // do not write into the developer's profile.
            if (fileLoggerProvider is not null)
            {
                logging.AddProvider(fileLoggerProvider);
            }

            logging.AddFilter("Microsoft", LogLevel.Warning);
            logging.AddFilter("System", LogLevel.Warning);
            logging.AddFilter(AppConstants.AppName, LogLevel.Debug);
        });

        // Add infrastructure services (APS authentication, data management)
        services.AddApsServices(configuration);

        // Application use cases are registered by AddApsServices above, against their interfaces
        // (IAuthenticationUseCase, ILogoutUseCase, IFileListingUseCase). The ViewModels take those
        // interfaces rather than the concrete classes, so nothing here needs the implementations by
        // name.

        // Add ViewModels
        services.AddScoped<MainViewModel>();
        services.AddScoped<HubSelectionViewModel>();
        services.AddScoped<ProjectSelectionViewModel>();
        services.AddScoped<FilesViewModel>();
        services.AddScoped<ColumnSelectorViewModel>();
        services.AddScoped<FileFilterViewModel>();
        services.AddSingleton<FilterPreferencesStore>();
        services.AddSingleton<ColumnPreferencesStore>();
        services.AddTransient<EditCustomAttributesViewModel>();
        // Per-dialog state, opened fresh each time from MainWindow - must not be reused across opens.
        services.AddTransient<CreateDeliverableViewModel>();
    }
}
