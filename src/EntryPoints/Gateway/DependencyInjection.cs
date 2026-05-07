using Gateway.Authentication;
using Microsoft.Extensions.Http;
using OpenIddict.Validation.AspNetCore;

namespace Gateway;

internal static class DependencyInjection
{
    public const string BearerPolicy = "RequireBearer";

    // Name OpenIddict.Validation.SystemNetHttp uses to register its IHttpClientFactory client.
    // Verified against the assembly name convention in OpenIddict 6.4.0
    // (OpenIddictValidationSystemNetHttpHandlers.SendHttpRequest creates the named client by
    // assembly name).
    private const string OpenIddictValidationHttpClientName = "OpenIddict.Validation.SystemNetHttp";

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

        var redisConn = configuration["Redis:ConnectionString"]
            ?? throw new InvalidOperationException("Redis:ConnectionString is required.");
        services.AddStackExchangeRedisCache(o => o.Configuration = redisConn);
        services.Configure<IntrospectionCacheOptions>(
            configuration.GetSection(IntrospectionCacheOptions.SectionName));
        services.AddTransient<IntrospectionCachingHandler>();

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

        // OpenIddict 6.4.0 doesn't expose AddHttpMessageHandler on its fluent builder, so we
        // attach the introspection cache via the HttpClientFactory's named-client pipeline.
        services.PostConfigure<HttpClientFactoryOptions>(
            OpenIddictValidationHttpClientName,
            options =>
            {
                options.HttpMessageHandlerBuilderActions.Add(builder =>
                {
                    var handler = builder.Services.GetRequiredService<IntrospectionCachingHandler>();
                    builder.AdditionalHandlers.Add(handler);
                });
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
