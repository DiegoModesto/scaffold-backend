using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Auth.API.IntegrationTests.Infrastructure;
using Shouldly;

namespace Auth.API.IntegrationTests.Endpoints;

[Trait("Category", "Integration")]
[Collection(AuthApiCollection.Name)]
public sealed class FailureFlowsTests(AuthWebApplicationFactory factory)
{
    private readonly AuthWebApplicationFactory _factory = factory;

    [Fact]
    public async Task Token_Rejects_InvalidClientSecret()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "ff-bad-secret")).Id;
        const string clientId = "ff-bad-secret-client";
        await _factory.SeedM2MClientAsync(tenantId, clientId, "correct-secret", "api:web");

        HttpClient http = _factory.CreateClient();

        HttpResponseMessage response = await http.PostAsync("/connect/token", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", "WRONG-secret"),
            new KeyValuePair<string, string>("scope", "api:web"),
        }));

        response.IsSuccessStatusCode.ShouldBeFalse(
            $"Expected failure but got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task Token_Rejects_InactiveClient()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "ff-inactive")).Id;
        const string clientId = "ff-inactive-client";
        const string secret = "ff-inactive-secret";
        await _factory.SeedM2MClientAsync(tenantId, clientId, secret, "api:web");
        await _factory.DeactivateM2MClientAsync(clientId);

        HttpClient http = _factory.CreateClient();

        HttpResponseMessage response = await http.PostAsync("/connect/token", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", secret),
            new KeyValuePair<string, string>("scope", "api:web"),
        }));

        response.IsSuccessStatusCode.ShouldBeFalse(
            $"Expected failure but got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task Token_Rejects_UnknownClientId()
    {
        HttpClient http = _factory.CreateClient();

        HttpResponseMessage response = await http.PostAsync("/connect/token", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", "no-such-client"),
            new KeyValuePair<string, string>("client_secret", "irrelevant"),
            new KeyValuePair<string, string>("scope", "api:web"),
        }));

        response.IsSuccessStatusCode.ShouldBeFalse(
            $"Expected failure but got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task Introspect_ReturnsInactive_AfterRevocation()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "ff-revoke")).Id;
        const string clientId = "ff-revoke-client";
        const string secret = "ff-revoke-secret";
        await _factory.SeedM2MClientAsync(tenantId, clientId, secret, "api:web");

        HttpClient http = _factory.CreateClient();

        // Issue a token.
        var tokenResp = await http.PostAsync("/connect/token", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", secret),
            new KeyValuePair<string, string>("scope", "api:web"),
        }));
        tokenResp.IsSuccessStatusCode.ShouldBeTrue(
            $"/connect/token returned {(int)tokenResp.StatusCode}: {await tokenResp.Content.ReadAsStringAsync()}");
        var tokenJson = await tokenResp.Content.ReadFromJsonAsync<TokenJson>();
        string accessToken = tokenJson!.AccessToken!;

        // Revoke the token. Per RFC 7009 the client revoking presents its own credentials.
        using var revokeReq = new HttpRequestMessage(HttpMethod.Post, "/connect/revocation")
        {
            Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("token", accessToken),
                new KeyValuePair<string, string>("token_type_hint", "access_token"),
            }),
        };
        revokeReq.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{secret}")));
        HttpResponseMessage revokeResp = await http.SendAsync(revokeReq);
        revokeResp.IsSuccessStatusCode.ShouldBeTrue(
            $"/connect/revocation returned {(int)revokeResp.StatusCode}: {await revokeResp.Content.ReadAsStringAsync()}");

        // Introspect — should now report inactive.
        using var introReq = new HttpRequestMessage(HttpMethod.Post, "/connect/introspect")
        {
            Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("token", accessToken),
            }),
        };
        introReq.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("web-api:dev-only-web-api-secret-change-me")));
        HttpResponseMessage introResp = await http.SendAsync(introReq);
        introResp.IsSuccessStatusCode.ShouldBeTrue();

        var intro = await introResp.Content.ReadFromJsonAsync<IntrospectJson>();
        intro!.Active.ShouldBeFalse();
    }

    private sealed record TokenJson(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string? AccessToken);

    private sealed record IntrospectJson(
        [property: System.Text.Json.Serialization.JsonPropertyName("active")] bool Active);
}
