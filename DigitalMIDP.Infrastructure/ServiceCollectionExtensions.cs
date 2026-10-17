using DigitalMIDP.Application.Interfaces;
using DigitalMIDP.Application.UseCases;
using DigitalMIDP.Infrastructure.Authentication;
using DigitalMIDP.Infrastructure.Caching;
using DigitalMIDP.Infrastructure.Configuration;
using DigitalMIDP.Infrastructure.DataManagement;
using DigitalMIDP.Infrastructure.DesktopConnector;
using DigitalMIDP.Infrastructure.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalMIDP.Infrastructure;

/// <summary>
/// Extension methods for configuring APS (Autodesk Platform Services) infrastructure.
/// </summary>
public static class ServiceCollectionExtensions
{
    private const string UserAgentHeaderName = "User-Agent";
    private const string UserAgentValue = "DigitalMIDP/1.0";

    /// <summary>
    /// Adds APS authentication and data management services to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddApsServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Configure APS authentication settings
        var apsSection = configuration.GetSection("Autodesk");
        var apsSettings = new ApsAuthenticationSettings
        {
            ClientId = string.IsNullOrWhiteSpace(apsSection["ClientId"])
                ? throw new InvalidOperationException("Missing Autodesk:ClientId in configuration")
                : apsSection["ClientId"]!.Trim(),
            RedirectUri = apsSection["RedirectUri"] ?? throw new InvalidOperationException("Missing Autodesk:RedirectUri in configuration"),
            Scopes = apsSection["Scopes"] ?? "data:read data:write",
            AuthorizeEndpoint = apsSection["AuthorizeEndpoint"] ?? "https://developer.api.autodesk.com/authentication/v2/authorize",
            TokenEndpoint = apsSection["TokenEndpoint"] ?? "https://developer.api.autodesk.com/authentication/v2/token",
            UserInfoEndpoint = apsSection["UserInfoEndpoint"] ?? "https://api.userprofile.autodesk.com/userinfo"
        };

        services.AddSingleton(apsSettings);

        // Caches project configuration and file paths that APS would otherwise be asked for again on
        // every refresh. Must be a singleton: DataManagementService is registered through
        // AddHttpClient below, which makes it transient, so a cache held by that service would be
        // discarded after every call.
        services.AddSingleton<IApsMetadataCache, ApsMetadataCache>();

        // Tokens are held encrypted for as long as the process holds them - see
        // DpapiTokenProtector for what that does and does not defend against. The unprotected
        // fallback exists only so a non-Windows host still runs; it is never chosen here on Windows.
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<ITokenProtector, DpapiTokenProtector>();
        }
        else
        {
            services.AddSingleton<ITokenProtector, UnprotectedTokenProtector>();
        }

        services.AddSingleton<ITokenStorage, ProtectedTokenStorage>();

        // One object behind both token interfaces, so the per-request fast path and the
        // sign-in/sign-out lifecycle cannot end up looking at different caches.
        services.AddSingleton<ApsTokenProvider>();
        services.AddSingleton<ITokenProvider>(sp => sp.GetRequiredService<ApsTokenProvider>());
        services.AddSingleton<ITokenStorageService>(sp => sp.GetRequiredService<ApsTokenProvider>());

        // Add HTTP client for authentication.
        //
        // Deliberately WITHOUT ApsAuthorizationHandler: the token endpoint is how a token is
        // obtained, so asking the token provider for one here would recurse. It does get the retry
        // handler - a dropped connection during sign-in or a token refresh is exactly as transient as
        // one during any other call, and failing it forces the user back through the browser.
        services.AddHttpClient<IAuthenticationService, AuthenticationService>()
            .AddHttpMessageHandler<ApsResilienceHandler>()
            .ConfigureHttpClient(client =>
            {
                client.DefaultRequestHeaders.Add(UserAgentHeaderName, UserAgentValue);
            });

        // Add HTTP client for data management.
        //
        // Handler order matters and is the reason these are listed this way round: handlers run
        // outermost-first, so the retry handler wraps the authorization handler. A retried request
        // therefore re-enters authorization and picks up a refreshed token if one fell due between
        // attempts. Reversed, a retry would keep re-presenting the token the first attempt used.
        services.AddHttpClient<IDataManagementService, DataManagementService>()
            .AddHttpMessageHandler<ApsResilienceHandler>()
            .AddHttpMessageHandler<ApsAuthorizationHandler>()
            .ConfigureHttpClient(client =>
            {
                client.DefaultRequestHeaders.Add(UserAgentHeaderName, UserAgentValue);
            });

        // Pre-signed S3 transfers deliberately have NO authorization handler. The signature travels
        // in the URL, and S3 rejects a request that also presents an APS bearer token, so uploads
        // and downloads cannot share the authenticated client above.
        services.AddHttpClient(DataManagementService.TransferHttpClientName)
            .ConfigureHttpClient(client =>
            {
                client.DefaultRequestHeaders.Add(UserAgentHeaderName, UserAgentValue);
                // File transfers can legitimately take far longer than an API call.
                client.Timeout = TimeSpan.FromMinutes(10);
            });

        // Register message handlers as Transient (required for HttpClient pipeline)
        services.AddTransient<ApsAuthorizationHandler>();
        services.AddTransient<ApsResilienceHandler>();

        // Add Desktop Connector service
        services.AddSingleton<IDesktopConnectorService, DesktopConnectorService>();

        // Add Desktop Connector use cases
        services.AddScoped<IOpenFileLocallyUseCase, OpenFileLocallyUseCase>();
        services.AddScoped<IOpenFileLocationUseCase, OpenFileLocationUseCase>();
        services.AddScoped<IGetLocalFilePathUseCase, GetLocalFilePathUseCase>();
        services.AddScoped<IGetDesktopConnectorStatusUseCase, GetDesktopConnectorStatusUseCase>();
        services.AddScoped<IGetSyncedProjectStatusUseCase, GetSyncedProjectStatusUseCase>();

        // Deliverable creation use cases (read-only groundwork: folder resolution, template
        // catalogue, document number allocation).
        services.AddScoped<ICdeStructureUseCase, CdeStructureUseCase>();
        services.AddScoped<ITemplateCatalogUseCase, TemplateCatalogUseCase>();
        services.AddScoped<IDocumentNumberUseCase, DocumentNumberUseCase>();
        services.AddScoped<ICreateDeliverableUseCase, CreateDeliverableUseCase>();

        // Authentication use cases. Registered here rather than in App.ConfigureServices so that the
        // sign-in and sign-out pair lives next to the token services it drives.
        services.AddScoped<IAuthenticationUseCase, AuthenticationUseCase>();
        services.AddScoped<ILogoutUseCase, LogoutUseCase>();
        services.AddScoped<IFileListingUseCase, FileListingUseCase>();

        return services;
    }
}
