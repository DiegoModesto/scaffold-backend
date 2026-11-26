using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Infra.Authentication;

/// <summary>
/// Inserts <see cref="IntrospectionCachingHandler"/> into the HttpClient that OpenIddict's
/// System.Net.Http integration uses for introspection. OpenIddict does not expose a way to add
/// a <see cref="DelegatingHandler"/>, and the name of its IHttpClientFactory client is an
/// implementation detail (OpenIddict 6.x used the bare assembly name; 7.x derives per-registration
/// names from it). Matching on the assembly-name prefix keeps the cache attached across versions.
/// </summary>
internal sealed class IntrospectionCacheHandlerFilter : IHttpMessageHandlerBuilderFilter
{
    internal const string OpenIddictValidationClientNamePrefix = "OpenIddict.Validation.SystemNetHttp";

    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) =>
        builder =>
        {
            next(builder);

            if (builder.Name?.StartsWith(OpenIddictValidationClientNamePrefix, StringComparison.Ordinal) == true)
            {
                builder.AdditionalHandlers.Add(builder.Services.GetRequiredService<IntrospectionCachingHandler>());
            }
        };
}
