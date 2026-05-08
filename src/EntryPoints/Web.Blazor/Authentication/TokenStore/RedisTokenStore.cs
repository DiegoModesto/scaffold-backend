using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace Web.Blazor.Authentication.TokenStore;

public sealed class RedisTokenStore(IDistributedCache cache) : ITokenStore
{
    private const string KeyPrefix = "bff:session:";

    public async Task SaveAsync(string sessionId, SessionTokens tokens, CancellationToken cancellationToken = default)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(tokens);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = tokens.ExpiresAt.AddHours(8)
        };

        await cache.SetAsync(KeyPrefix + sessionId, payload, options, cancellationToken);
    }

    public async Task<SessionTokens?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        byte[]? payload = await cache.GetAsync(KeyPrefix + sessionId, cancellationToken);
        if (payload is null || payload.Length == 0)
        {
            return null;
        }

        return JsonSerializer.Deserialize<SessionTokens>(payload);
    }

    public Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default)
        => cache.RemoveAsync(KeyPrefix + sessionId, cancellationToken);
}
