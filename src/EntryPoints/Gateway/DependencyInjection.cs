using OpenIddict.Validation.AspNetCore;

namespace Gateway;

internal static class DependencyInjection
{
    public const string BearerPolicy = "RequireBearer";

    public static IServiceCollection AddGatewayAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("Auth");
        var issuer = section["Authority"]
            ?? throw new InvalidOperationException("Auth:Authority is required.");
        // Validated at startup so misconfiguration fails fast even though the endpoint is
        // discovered automatically from the issuer's metadata document.
        _ = section["IntrospectionEndpoint"]
            ?? throw new InvalidOperationException("Auth:IntrospectionEndpoint is required.");
        var clientId = section["IntrospectionClientId"]
            ?? throw new InvalidOperationException("Auth:IntrospectionClientId is required.");
        var clientSecret = section["IntrospectionClientSecret"]
            ?? throw new InvalidOperationException("Auth:IntrospectionClientSecret is required.");

        services.AddOpenIddict()
            .AddValidation(o =>
            {
                o.SetIssuer(new Uri(issuer));
                o.AddAudiences("api:web", "api:auth");
                o.UseIntrospection()
                    .SetClientId(clientId)
                    .SetClientSecret(clientSecret);
                o.UseSystemNetHttp()
                    .ConfigureHttpClient(client =>
                    {
                        client.Timeout = TimeSpan.FromSeconds(5);
                    });
                o.UseAspNetCore();
            });

        services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);

        services.AddAuthorization(opt =>
        {
            opt.AddPolicy(BearerPolicy, p =>
            {
                p.AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
                p.RequireAuthenticatedUser();
            });
        });

        return services;
    }
}
