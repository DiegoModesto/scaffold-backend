using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
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

        // Optional Redis-backed introspection cache. When Redis is configured we attach a
        // DelegatingHandler to the OpenIddict introspection HttpClient that caches responses
        // by token hash. When Redis is absent (e.g. in-process tests that wire their own
        // mocks), the handler is skipped — OpenIddict still works, just without caching.
        string? redisConn = configuration["Redis:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(redisConn))
        {
            services.AddIntrospectionResponseCache(redisConn, configuration);
        }

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

    /// <summary>
    /// Caches OpenIddict introspection responses in Redis, keyed by the SHA-256 of the token,
    /// for <c>IntrospectionCache:TtlSeconds</c>. Shared by Web.API and the Gateway.
    /// </summary>
    public static IServiceCollection AddIntrospectionResponseCache(
        this IServiceCollection services,
        string redisConnectionString,
        IConfiguration configuration)
    {
        services.AddStackExchangeRedisCache(o => o.Configuration = redisConnectionString);
        services.Configure<IntrospectionCacheOptions>(
            configuration.GetSection(IntrospectionCacheOptions.SectionName));
        services.AddTransient<IntrospectionCachingHandler>();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter, IntrospectionCacheHandlerFilter>();
        return services;
    }
}
