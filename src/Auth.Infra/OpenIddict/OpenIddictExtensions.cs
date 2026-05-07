using System.Security.Cryptography.X509Certificates;
using Auth.Infra.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;
using Quartz;

namespace Auth.Infra.OpenIddict;

public static class OpenIddictExtensions
{
    public static IServiceCollection AddAuthOpenIddict(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Quartz for OpenIddict's periodic token-pruning job
        services.AddQuartz(opt =>
        {
            opt.UseSimpleTypeLoader();
            opt.UseInMemoryStore();
        });
        services.AddQuartzHostedService(opt =>
        {
            opt.WaitForJobsToComplete = true;
        });

        services.AddOpenIddict()
            .AddCore(o =>
            {
                o.UseEntityFrameworkCore().UseDbContext<AuthDbContext>();
                o.UseQuartz();
            })
            .AddServer(o =>
            {
                o.SetAuthorizationEndpointUris("/connect/authorize")
                 .SetTokenEndpointUris("/connect/token")
                 .SetUserInfoEndpointUris("/connect/userinfo")
                 .SetIntrospectionEndpointUris("/connect/introspect")
                 .SetRevocationEndpointUris("/connect/revocation")
                 .SetEndSessionEndpointUris("/connect/logout");

                o.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange()
                 .AllowRefreshTokenFlow()
                 .AllowClientCredentialsFlow();

                o.RegisterScopes(
                    OpenIddictConstants.Scopes.OpenId,
                    OpenIddictConstants.Scopes.Profile,
                    OpenIddictConstants.Scopes.Email,
                    OpenIddictConstants.Scopes.OfflineAccess,
                    "api:web",
                    "api:auth");

                // Reference (opaque) tokens — validated via /connect/introspect
                o.UseReferenceAccessTokens();
                o.UseReferenceRefreshTokens();

                o.SetAccessTokenLifetime(TimeSpan.FromSeconds(
                    configuration.GetValue("OpenIddict:AccessTokenLifetimeSeconds", 900)));
                o.SetRefreshTokenLifetime(TimeSpan.FromSeconds(
                    configuration.GetValue("OpenIddict:RefreshTokenLifetimeSeconds", 1209600)));

                string? signingPath = configuration["OpenIddict:SigningCertificatePath"];
                string? encryptionPath = configuration["OpenIddict:EncryptionCertificatePath"];
                if (!string.IsNullOrWhiteSpace(signingPath) && !string.IsNullOrWhiteSpace(encryptionPath))
                {
                    o.AddSigningCertificate(LoadCert(
                        signingPath, configuration["OpenIddict:SigningCertificatePassword"]));
                    o.AddEncryptionCertificate(LoadCert(
                        encryptionPath, configuration["OpenIddict:EncryptionCertificatePassword"]));
                }
                else
                {
                    o.AddDevelopmentSigningCertificate();
                    o.AddDevelopmentEncryptionCertificate();
                }

                o.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough();
            })
            .AddValidation(o =>
            {
                o.UseLocalServer();
                o.UseAspNetCore();
            });

        services.AddHostedService<OpenIddictClientSeedHostedService>();

        return services;
    }

    private static X509Certificate2 LoadCert(string path, string? password) =>
        X509CertificateLoader.LoadPkcs12FromFile(
            path,
            password,
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
}
