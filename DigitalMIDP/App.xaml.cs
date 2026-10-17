using DigitalMIDP.Application.UseCases;
using DigitalMIDP.Infrastructure;
using DigitalMIDP.Services;
using DigitalMIDP.ViewModels;
using DigitalMIDP.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Windows;

namespace DigitalMIDP;

public partial class App : System.Windows.Application
{
    private IServiceProvider? _serviceProvider;

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
            (_serviceProvider as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            // Never let cleanup turn a normal exit into a crash dialog.
            System.Diagnostics.Debug.WriteLine($"[WARN] Shutdown cleanup failed: {ex}");
        }

        base.OnExit(e);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            base.OnStartup(e);

            var configuration = LoadConfiguration();
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
            ConfigureServices(services, configuration);
            _serviceProvider = services.BuildServiceProvider();

            var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
            logger.LogInformation("Application started successfully");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FATAL] Startup Error: {ex}");
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
    internal static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton(configuration);

        // Add logging with console provider
        services.AddLogging(logging =>
        {
            logging.AddDebug(); // Output window
            logging.AddConsole();
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
