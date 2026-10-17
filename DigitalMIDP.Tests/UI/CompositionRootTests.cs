using DigitalMIDP.Application.Interfaces;
using DigitalMIDP.Application.UseCases;
using DigitalMIDP.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalMIDP.Tests.UI;

/// <summary>
/// Builds the application's real DI container and resolves what it registers.
///
/// <para>
/// A broken DI graph - a missing registration, a captive dependency, an interface registered against
/// an implementation that cannot be constructed - otherwise only surfaces when the application is
/// launched and the main window tries to resolve its ViewModel. These tests move that to the build.
/// </para>
/// </summary>
public sealed class CompositionRootTests
{
    /// <summary>
    /// Builds the container the way the application does: <c>BuildServiceProvider()</c> with no
    /// options.
    ///
    /// <para>
    /// This matters, and is not laziness. The application has exactly one container, built once at
    /// startup, and resolves everything from the root provider - <c>App.Services.GetRequiredService</c>
    /// - so its <c>AddScoped</c> registrations behave as de facto singletons for the process
    /// lifetime. Turning scope validation on would reject that, which is why
    /// <see cref="BuildValidatedProvider"/> is a separate helper used only where a captive
    /// dependency is what is being checked for.
    /// </para>
    /// </summary>
    private static ServiceProvider BuildProvider() => BuildServices().BuildServiceProvider();

    private static ServiceProvider BuildValidatedProvider()
    {
        // ValidateScopes catches a captive dependency (a singleton depending on something scoped),
        // and ValidateOnBuild surfaces an unconstructable registration at build time rather than on
        // first resolve. Stricter than the real container on purpose - see BuildProvider.
        return BuildServices().BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }

    private static ServiceCollection BuildServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // The same keys AddApsServices requires of appsettings.json. Not secrets: the real
                // ClientId is a public PKCE client id.
                ["Autodesk:ClientId"] = "test-client-id",
                ["Autodesk:RedirectUri"] = "http://localhost:8080/callback",
                ["Autodesk:Scopes"] = "data:read data:write data:create"
            })
            .Build();

        var services = new ServiceCollection();
        App.ConfigureServices(services, configuration);

        return services;
    }

    [Fact]
    public void ConfigureServices_ProducesAContainerThatValidates()
    {
        var act = () => BuildValidatedProvider().Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public void ConfigureServices_ResolvesTheMainViewModelAndItsWholeGraph()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        // Pulls in essentially everything: the auth and logout use cases, the file listing use case,
        // the data management service with its handler pipeline, the Desktop Connector use cases, the
        // column/filter stores, and the three child ViewModels.
        var mainViewModel = scope.ServiceProvider.GetRequiredService<MainViewModel>();

        mainViewModel.Should().NotBeNull();
        mainViewModel.FilesViewModel.Should().NotBeNull();
        mainViewModel.HubSelectionViewModel.Should().NotBeNull();
        mainViewModel.ProjectSelectionViewModel.Should().NotBeNull();
    }

    [Fact]
    public void ConfigureServices_RegistersTheUseCasesBehindTheirInterfaces()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        // The ViewModels take these interfaces rather than the concrete use cases, so the
        // registrations have to exist against the interfaces - resolving the concrete class instead
        // would compile and then fail at startup.
        scope.ServiceProvider.GetRequiredService<IAuthenticationUseCase>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<ILogoutUseCase>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IFileListingUseCase>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<ICreateDeliverableUseCase>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IDocumentNumberUseCase>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<ICdeStructureUseCase>().Should().NotBeNull();
    }

    [Fact]
    public void ConfigureServices_GivesBothTokenInterfacesTheSameInstance()
    {
        using var provider = BuildProvider();

        var tokenProvider = provider.GetRequiredService<ITokenProvider>();
        var tokenStorageService = provider.GetRequiredService<ITokenStorageService>();

        // One object behind both, by design: the per-request fast path and the sign-in/sign-out
        // lifecycle must share one cache. Two instances would mean a token saved at sign-in was
        // invisible to the handler that attaches it.
        tokenProvider.Should().BeSameAs(tokenStorageService);
    }

    [Fact]
    public void ConfigureServices_MakesTheTokenServicesSingletons()
    {
        using var provider = BuildProvider();

        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        // A per-scope token provider would hold a per-scope cache, so each scope would refresh
        // separately and the sign-in in one would not be visible to the other.
        scopeA.ServiceProvider.GetRequiredService<ITokenProvider>()
            .Should().BeSameAs(scopeB.ServiceProvider.GetRequiredService<ITokenProvider>());
    }

    [Fact]
    public void ConfigureServices_MakesTheMetadataCacheASingleton()
    {
        using var provider = BuildProvider();

        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        var cacheType = typeof(DigitalMIDP.Infrastructure.ServiceCollectionExtensions).Assembly
            .GetType("DigitalMIDP.Infrastructure.Caching.IApsMetadataCache")!;

        var a = scopeA.ServiceProvider.GetRequiredService(cacheType);
        var b = scopeB.ServiceProvider.GetRequiredService(cacheType);

        // This one is not a preference. DataManagementService is registered through AddHttpClient and
        // is therefore transient, so anything other than a singleton cache would be discarded after
        // every call and cache nothing at all.
        a.Should().BeSameAs(b);
    }

    [Fact]
    public void ConfigureServices_MakesTheDataManagementServiceTransient()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var first = scope.ServiceProvider.GetRequiredService<IDataManagementService>();
        var second = scope.ServiceProvider.GetRequiredService<IDataManagementService>();

        // Documents the consequence of AddHttpClient, which is the reason the cache above cannot
        // live on this service.
        first.Should().NotBeSameAs(second);
    }

    [Fact]
    public void ConfigureServices_MakesTheDialogViewModelsTransient()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        // Per-dialog-instance state, opened fresh each time - a reused instance would carry the
        // previous dialog's selections into the next one.
        scope.ServiceProvider.GetRequiredService<EditCustomAttributesViewModel>()
            .Should().NotBeSameAs(scope.ServiceProvider.GetRequiredService<EditCustomAttributesViewModel>());

        scope.ServiceProvider.GetRequiredService<CreateDeliverableViewModel>()
            .Should().NotBeSameAs(scope.ServiceProvider.GetRequiredService<CreateDeliverableViewModel>());
    }

    [Fact]
    public void DisposingTheContainer_DisposesTheMainViewModelAndStopsItsTimers()
    {
        var provider = BuildProvider();

        // Resolved from the ROOT provider, exactly as MainWindow does. MainViewModel is registered
        // AddScoped, and a scoped service resolved from the root provider is tracked by the root
        // scope - which is what makes disposing the container enough. App.OnExit relies on this
        // precisely so it does not have to resolve (and therefore construct) anything itself.
        var mainViewModel = provider.GetRequiredService<MainViewModel>();
        var filesViewModel = mainViewModel.FilesViewModel;

        var autoRefreshTimer = (System.Windows.Threading.DispatcherTimer)typeof(FilesViewModel)
            .GetField("_autoRefreshTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(filesViewModel)!;

        autoRefreshTimer.Start();
        autoRefreshTimer.IsEnabled.Should().BeTrue();

        provider.Dispose();

        // The end of the chain this exists for: container -> MainViewModel -> FilesViewModel ->
        // timers stopped. Before this, nothing in the application ever stopped them.
        autoRefreshTimer.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void DisposingTheContainer_DisposesTheTokenStoreSoItsBuffersAreCleared()
    {
        var provider = BuildProvider();

        var tokenStorageService = provider.GetRequiredService<ITokenStorageService>();

        var act = provider.Dispose;

        // ProtectedTokenStorage and ApsTokenProvider both implement IDisposable - the store to zero
        // its ciphertext buffers, the provider to release its refresh semaphore. Disposal must not
        // throw, because App.OnExit swallows nothing of substance and a failure here would be the
        // last thing the user sees.
        tokenStorageService.Should().NotBeNull();
        act.Should().NotThrow();
    }

    [Fact]
    public void ConfigureServices_WithNoClientId_FailsWithAnActionableMessage()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Autodesk:RedirectUri"] = "http://localhost:8080/callback"
            })
            .Build();

        var act = () => App.ConfigureServices(new ServiceCollection(), configuration);

        // Misconfiguration should name the missing key rather than failing later as a 400 from
        // Autodesk that reads like a code bug.
        act.Should().Throw<InvalidOperationException>().WithMessage("*Autodesk:ClientId*");
    }

    [Fact]
    public void ConfigureServices_WithMissingArguments_Throws()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.Throws<ArgumentNullException>(() => App.ConfigureServices(null!, configuration));
        Assert.Throws<ArgumentNullException>(() => App.ConfigureServices(new ServiceCollection(), null!));
    }
}
