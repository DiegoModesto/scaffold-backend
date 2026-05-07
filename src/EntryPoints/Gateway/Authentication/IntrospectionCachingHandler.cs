using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Gateway.Authentication;

internal sealed class IntrospectionCachingHandler : DelegatingHandler
{
    private const string CacheKeyPrefix = "gateway:introspect:";

    private readonly IDistributedCache _cache;
    private readonly IntrospectionCacheOptions _options;

    public IntrospectionCachingHandler(
        IDistributedCache cache,
        IOptions<IntrospectionCacheOptions> options)
    {
        _cache = cache;
        _options = options.Value;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await ExtractTokenAsync(request, cancellationToken);
        if (token is null)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var key = CacheKeyPrefix + Hash(token);
        var cached = await _cache.GetAsync(key, cancellationToken);
        if (cached is not null)
        {
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(cached)
                {
                    Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") }
                }
            };
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            await _cache.SetAsync(key, body, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _options.Ttl
            }, cancellationToken);

            response = new HttpResponseMessage(response.StatusCode)
            {
                Content = new ByteArrayContent(body)
                {
                    Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") }
                }
            };
        }

        return response;
    }

    private static async Task<string?> ExtractTokenAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Content is null)
        {
            return null;
        }

        var body = await request.Content.ReadAsStringAsync(ct);

        // Re-set the content so the inner handler still sees the body — reading the stream
        // can consume it for some HttpContent types.
        var mediaType = request.Content.Headers.ContentType?.MediaType ?? "application/x-www-form-urlencoded";
        request.Content = new StringContent(body, Encoding.UTF8, mediaType);

        foreach (var pair in body.Split('&'))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && kv[0] == "token")
            {
                return Uri.UnescapeDataString(kv[1]);
            }
        }
        return null;
    }

    private static string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexStringLower(bytes);
    }
}
