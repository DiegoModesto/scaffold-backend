using System.Net.Http.Headers;
using Gateway.IntegrationTests.Infrastructure;
using Shouldly;

namespace Gateway.IntegrationTests.Authentication;

[Collection(GatewayCollection.Name)]
public sealed class CacheBehaviorTests(GatewayWebApplicationFactory factory)
{
    [Fact]
    public async Task SecondRequestSameToken_DoesNotHitIntrospectionEndpoint()
    {
        const string token = "cache-token";
        factory.StubWebApiEcho();
        factory.StubIntrospection(token, active: true);

        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage first = await client.GetAsync("/api/test/echo");
        first.IsSuccessStatusCode.ShouldBeTrue();

        HttpResponseMessage second = await client.GetAsync("/api/test/echo");
        second.IsSuccessStatusCode.ShouldBeTrue();

        int introspectCalls = factory.AuthApi.LogEntries
            .Count(e => string.Equals(
                e.RequestMessage?.Path,
                "/connect/introspect",
                StringComparison.Ordinal));
        introspectCalls.ShouldBe(1, "Expected only ONE introspection call due to caching");
    }
}
