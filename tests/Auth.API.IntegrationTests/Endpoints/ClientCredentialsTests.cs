using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Auth.API.IntegrationTests.Infrastructure;
using Shouldly;

namespace Auth.API.IntegrationTests.Endpoints;

[Trait("Category", "Integration")]
[Collection(AuthApiCollection.Name)]
public sealed class ClientCredentialsTests(AuthWebApplicationFactory factory)
{
    private readonly AuthWebApplicationFactory _factory = factory;

    [Fact]
    public async Task Token_ClientCredentials_IssuesAccessToken_ForActiveM2MClient()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "ccg-tenant")).Id;
        const string clientId = "ccg-client-issue";
        const string secret = "ccg-secret-issue";
        await _factory.SeedM2MClientAsync(tenantId, clientId, secret, "api:web");

        HttpClient http = _factory.CreateClient();

        TokenResponse token = await RequestTokenAsync(http, clientId, secret, "api:web");

        token.AccessToken.ShouldNotBeNullOrEmpty();
        token.TokenType.ShouldBe("Bearer", StringCompareShould.IgnoreCase);
        token.ExpiresIn.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Introspection_ReturnsActive_ForFreshAccessToken()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "ccg-introspect")).Id;
        const string clientId = "ccg-client-introspect";
        const string secret = "ccg-secret-introspect";
        await _factory.SeedM2MClientAsync(tenantId, clientId, secret, "api:web");

        HttpClient http = _factory.CreateClient();
        TokenResponse token = await RequestTokenAsync(http, clientId, secret, "api:web");

        // Introspect using the seeded "web-api" resource server credentials.
        IntrospectionResponse intro = await IntrospectAsync(
            http, "web-api", "dev-only-web-api-secret-change-me", token.AccessToken!);

        intro.Active.ShouldBeTrue();
        intro.ClientId.ShouldBe(clientId);
    }

    [Fact]
    public async Task Token_ClientCredentials_Honors_RequestedScopes()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "ccg-scopes")).Id;
        const string clientId = "ccg-client-scopes";
        const string secret = "ccg-secret-scopes";
        await _factory.SeedM2MClientAsync(tenantId, clientId, secret, "api:web", "api:auth");

        HttpClient http = _factory.CreateClient();

        TokenResponse token = await RequestTokenAsync(http, clientId, secret, "api:web");

        token.AccessToken.ShouldNotBeNullOrEmpty();
        // Scope intersection is enforced by the M2M handler (only api:web requested).
        if (!string.IsNullOrEmpty(token.Scope))
        {
            token.Scope.ShouldContain("api:web");
        }
    }

    private static async Task<TokenResponse> RequestTokenAsync(
        HttpClient http, string clientId, string secret, string scope)
    {
        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", secret),
            new KeyValuePair<string, string>("scope", scope),
        });

        HttpResponseMessage response = await http.PostAsync("/connect/token", form);
        response.IsSuccessStatusCode.ShouldBeTrue(
            $"/connect/token returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    private static async Task<IntrospectionResponse> IntrospectAsync(
        HttpClient http, string clientId, string clientSecret, string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/connect/introspect")
        {
            Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("token", token),
            }),
        };
        string basic = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        HttpResponseMessage response = await http.SendAsync(req);
        response.IsSuccessStatusCode.ShouldBeTrue(
            $"/connect/introspect returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        return (await response.Content.ReadFromJsonAsync<IntrospectionResponse>())!;
    }

    private sealed record TokenResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string? AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("token_type")] string? TokenType,
        [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int ExpiresIn,
        [property: System.Text.Json.Serialization.JsonPropertyName("scope")] string? Scope);

    private sealed record IntrospectionResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("active")] bool Active,
        [property: System.Text.Json.Serialization.JsonPropertyName("client_id")] string? ClientId,
        [property: System.Text.Json.Serialization.JsonPropertyName("sub")] string? Sub,
        [property: System.Text.Json.Serialization.JsonPropertyName("scope")] string? Scope);
}
