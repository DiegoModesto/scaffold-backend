using System.Net;
using System.Text;
using Gateway.Authentication;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Shouldly;

namespace Gateway.IntegrationTests.Authentication;

public sealed class IntrospectionCachingHandlerTests
{
    [Fact]
    public async Task CacheMiss_CallsInnerAndStoresResponse()
    {
        var inner = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        inner.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"active":true}""", Encoding.UTF8, "application/json")
            });

        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var options = Options.Create(new IntrospectionCacheOptions { TtlSeconds = 30 });
        var handler = new IntrospectionCachingHandler(cache, options) { InnerHandler = inner.Object };
        var client = new HttpClient(handler);

        var req = new HttpRequestMessage(HttpMethod.Post, "https://auth/connect/introspect")
        {
            Content = new StringContent("token=abc123", Encoding.UTF8, "application/x-www-form-urlencoded")
        };
        var resp = await client.SendAsync(req);

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await resp.Content.ReadAsStringAsync()).ShouldContain("\"active\":true");
        inner.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task CacheHit_DoesNotCallInner()
    {
        var inner = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        inner.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"active":true}""", Encoding.UTF8, "application/json")
            });

        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var options = Options.Create(new IntrospectionCacheOptions { TtlSeconds = 30 });
        var handler = new IntrospectionCachingHandler(cache, options) { InnerHandler = inner.Object };
        var client = new HttpClient(handler);

        HttpRequestMessage MakeRequest() =>
            new(HttpMethod.Post, "https://auth/connect/introspect")
            {
                Content = new StringContent("token=abc123", Encoding.UTF8, "application/x-www-form-urlencoded")
            };

        var first = await client.SendAsync(MakeRequest());
        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        var second = await client.SendAsync(MakeRequest());
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await second.Content.ReadAsStringAsync()).ShouldContain("\"active\":true");

        inner.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task NoTokenInBody_BypassesCache()
    {
        var inner = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        inner.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var options = Options.Create(new IntrospectionCacheOptions { TtlSeconds = 30 });
        var handler = new IntrospectionCachingHandler(cache, options) { InnerHandler = inner.Object };
        var client = new HttpClient(handler);

        var req = new HttpRequestMessage(HttpMethod.Get, "https://auth/connect/introspect");
        var resp = await client.SendAsync(req);
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);

        inner.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }
}
