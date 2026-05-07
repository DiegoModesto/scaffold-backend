using System.Net;
using System.Net.Http.Headers;
using Gateway.IntegrationTests.Infrastructure;
using Shouldly;

namespace Gateway.IntegrationTests.Authentication;

[Collection(GatewayCollection.Name)]
public sealed class AuthenticationFlowTests(GatewayWebApplicationFactory factory)
{
    [Fact]
    public async Task ProtectedRoute_NoToken_Returns401()
    {
        factory.StubWebApiEcho();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage resp = await client.GetAsync("/api/test/echo");

        resp.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProtectedRoute_InactiveToken_Returns401()
    {
        const string token = "inactive-token-1";
        factory.StubWebApiEcho();
        factory.StubIntrospection(token, active: false);

        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage resp = await client.GetAsync("/api/test/echo");

        resp.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProtectedRoute_ActiveToken_ProxiesToBackend()
    {
        const string token = "active-token-1";
        factory.StubWebApiEcho();
        factory.StubIntrospection(token, active: true);

        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage resp = await client.GetAsync("/api/test/echo");

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Verify the downstream received forwarded headers via WireMock's request log.
        var receivedRequests = factory.WebApi.LogEntries.ToList();
        receivedRequests.ShouldNotBeEmpty();
        var lastRequest = receivedRequests[^1].RequestMessage
            ?? throw new InvalidOperationException("WireMock recorded a null request message.");
        var headers = lastRequest.Headers ?? throw new InvalidOperationException(
            "WireMock recorded no request headers.");
        headers.ShouldContainKey("X-Forwarded-User");
        headers.ShouldContainKey("Authorization");
    }
}
