using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Validation.AspNetCore;

namespace Infra.Authentication;

public static class IntrospectionAuthenticationExtensions
{
    public static IServiceCollection AddIntrospectionAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection("Auth");
        string issuer = section["Authority"]
            ?? throw new InvalidOperationException("Auth:Authority is required.");
        // Validated up-front so misconfiguration fails fast even though the endpoint is
        // discovered automatically from the issuer's metadata document.
        _ = section["IntrospectionEndpoint"]
            ?? throw new InvalidOperationException("Auth:IntrospectionEndpoint is required.");
        string clientId = section["IntrospectionClientId"]
            ?? throw new InvalidOperationException("Auth:IntrospectionClientId is required.");
        string clientSecret = section["IntrospectionClientSecret"]
            ?? throw new InvalidOperationException("Auth:IntrospectionClientSecret is required.");

        services.AddOpenIddict()
            .AddValidation(o =>
            {
                o.SetIssuer(new Uri(issuer));
                o.AddAudiences("api:web");
                o.UseIntrospection()
                    .SetClientId(clientId)
                    .SetClientSecret(clientSecret);
                o.UseSystemNetHttp()
                    .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(5));
                o.UseAspNetCore();
            });

        services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);

        return services;
    }
}
