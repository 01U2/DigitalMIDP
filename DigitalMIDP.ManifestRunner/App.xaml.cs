using System.Windows;
using System.Windows.Threading;
using DigitalMIDP.Infrastructure;
using DigitalMIDP.ManifestRunner.ViewModels;
using DigitalMIDP.Services;
using DigitalMIDP.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalMIDP.ManifestRunner;

public partial class App : System.Windows.Application
{
    private Mutex? instance;
    private ServiceProvider? services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            instance = new Mutex(false, @"Local\DigitalMIDP.ManifestRunner", out var first);
            if (!first)
            {
                MessageBox.Show("Runner is already open in this Windows session.", "DigitalMIDP Runner");
                Shutdown();
                return;
            }
            var config = LoadConfiguration();
            if (!ApsClientConfiguration.HasValidClientId(config))
            {
                var setup = new ClientIdSetupViewModel(ApsClientConfiguration.UserConfigPath, config["Autodesk:RedirectUri"] ?? "");
                if (new SetupWindow(setup).ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
                config = LoadConfiguration();
            }
            var collection = new ServiceCollection();
            collection.AddLogging();
            collection.AddApsServices(config);
            collection.AddSingleton<SynchronizationContext>(new DispatcherSynchronizationContext(Dispatcher));
            collection.AddSingleton<RunnerViewModel>();
            services = collection.BuildServiceProvider();
            MainWindow = new RunnerWindow(services.GetRequiredService<RunnerViewModel>());
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cannot start Runner: {ex.Message}", "Startup error");
            Shutdown(1);
        }
    }

    private static Microsoft.Extensions.Configuration.IConfigurationRoot LoadConfiguration() =>
        ApsClientConfiguration.Build(AppContext.BaseDirectory, ApsClientConfiguration.CompanyConfigPath, ApsClientConfiguration.UserConfigPath);

    protected override void OnExit(ExitEventArgs e)
    {
        try { services?.Dispose(); }
        finally { instance?.Dispose(); }
        base.OnExit(e);
    }
}
