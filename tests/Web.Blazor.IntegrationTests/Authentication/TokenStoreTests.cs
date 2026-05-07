using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Shouldly;
using Web.Blazor.Authentication.TokenStore;

namespace Web.Blazor.IntegrationTests.Authentication;

/// <summary>
/// Unit tests for <see cref="RedisTokenStore"/>. The store is just a thin JSON wrapper
/// over <see cref="IDistributedCache"/>, so we exercise it against an in-memory cache
/// rather than spinning up Redis. The Redis-backed code path is exercised end-to-end
/// by the BFF integration suite.
/// </summary>
public sealed class TokenStoreTests
{
    private static RedisTokenStore CreateStore() =>
        new(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));

    [Fact]
    public async Task SaveAndGet_RoundTrips()
    {
        RedisTokenStore store = CreateStore();
        var tokens = new SessionTokens(
            AccessToken: "access-1",
            RefreshToken: "refresh-1",
            IdToken: "id-1",
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(15));

        await store.SaveAsync("session-A", tokens);
        SessionTokens? loaded = await store.GetAsync("session-A");

        loaded.ShouldNotBeNull();
        loaded!.AccessToken.ShouldBe(tokens.AccessToken);
        loaded.RefreshToken.ShouldBe(tokens.RefreshToken);
        loaded.IdToken.ShouldBe(tokens.IdToken);
        loaded.ExpiresAt.ShouldBe(tokens.ExpiresAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Get_ReturnsNull_ForUnknownSession()
    {
        RedisTokenStore store = CreateStore();

        SessionTokens? loaded = await store.GetAsync("nope");

        loaded.ShouldBeNull();
    }

    [Fact]
    public async Task Remove_ClearsTokens()
    {
        RedisTokenStore store = CreateStore();
        var tokens = new SessionTokens("a", "r", "i", DateTimeOffset.UtcNow.AddMinutes(15));
        await store.SaveAsync("session-B", tokens);

        await store.RemoveAsync("session-B");

        SessionTokens? loaded = await store.GetAsync("session-B");
        loaded.ShouldBeNull();
    }
}
