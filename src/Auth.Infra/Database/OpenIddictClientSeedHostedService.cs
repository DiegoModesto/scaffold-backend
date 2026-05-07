using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;

namespace Auth.Infra.Database;

internal sealed class OpenIddictClientSeedHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<OpenIddictClientSeedHostedService> logger)
    : IHostedService
{
    private const string DevBffSecret = "dev-only-bff-secret-change-me";
    private const string DevWebApiSecret = "dev-only-web-api-secret-change-me";
    private const string DevGatewaySecret = "dev-only-gateway-secret-change-me";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        await SeedBffAsync(manager, cancellationToken);
        await SeedWebApiAsync(manager, cancellationToken);
        await SeedGatewayAsync(manager, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedBffAsync(IOpenIddictApplicationManager manager, CancellationToken ct)
    {
        const string clientId = "bff-blazor";
        object? existing = await manager.FindByClientIdAsync(clientId, ct);
        if (existing is not null)
        {
            logger.LogDebug("OpenIddict application '{ClientId}' already exists; skipping seed.", clientId);
            return;
        }

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientType = OpenIddictConstants.ClientTypes.Confidential,
            ClientSecret = ResolveSecret("OPENIDDICT_BFF_SECRET", DevBffSecret),
            DisplayName = "Blazor BFF",
#pragma warning disable S1075 // Hardcoded URI is intentional: dev redirect URI for the BFF client.
            RedirectUris = { new Uri("http://localhost:5002/signin-oidc") },
            PostLogoutRedirectUris = { new Uri("http://localhost:5002/signout-callback-oidc") },
#pragma warning restore S1075
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.Endpoints.EndSession,
                OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                OpenIddictConstants.Permissions.ResponseTypes.Code,
                OpenIddictConstants.Permissions.Scopes.Email,
                OpenIddictConstants.Permissions.Scopes.Profile,
                OpenIddictConstants.Permissions.Prefixes.Scope + "api:web",
                OpenIddictConstants.Permissions.Prefixes.Scope + "offline_access"
            }
        };

        await manager.CreateAsync(descriptor, ct);
        logger.LogInformation("Seeded OpenIddict application '{ClientId}'.", clientId);
    }

    private async Task SeedWebApiAsync(IOpenIddictApplicationManager manager, CancellationToken ct)
    {
        await SeedIntrospectionClientAsync(
            manager, "web-api", "Web API Resource Server",
            "OPENIDDICT_WEB_API_SECRET", DevWebApiSecret, ct);
    }

    private async Task SeedGatewayAsync(IOpenIddictApplicationManager manager, CancellationToken ct)
    {
        await SeedIntrospectionClientAsync(
            manager, "gateway", "API Gateway",
            "OPENIDDICT_GATEWAY_SECRET", DevGatewaySecret, ct);
    }

    private async Task SeedIntrospectionClientAsync(
        IOpenIddictApplicationManager manager,
        string clientId,
        string displayName,
        string envVar,
        string devFallback,
        CancellationToken ct)
    {
        object? existing = await manager.FindByClientIdAsync(clientId, ct);
        if (existing is not null)
        {
            logger.LogDebug("OpenIddict application '{ClientId}' already exists; skipping seed.", clientId);
            return;
        }

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientType = OpenIddictConstants.ClientTypes.Confidential,
            ClientSecret = ResolveSecret(envVar, devFallback),
            DisplayName = displayName,
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Introspection
            }
        };

        await manager.CreateAsync(descriptor, ct);
        logger.LogInformation("Seeded OpenIddict application '{ClientId}'.", clientId);
    }

    private string ResolveSecret(string envVar, string devFallback)
    {
        string? configured = configuration[envVar] ?? Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        if (environment.IsDevelopment())
        {
            logger.LogWarning(
                "{EnvVar} not set; using DEV-ONLY fallback secret. Do NOT use this in non-Development environments.",
                envVar);
            return devFallback;
        }

        throw new InvalidOperationException(
            $"{envVar} env var is required in non-Development environments.");
    }
}
