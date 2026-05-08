using System.Net;
using System.Net.Http.Json;
using Auth.API.IntegrationTests.Infrastructure;
using Auth.Domain.Permissions;
using Shouldly;

namespace Auth.API.IntegrationTests.Endpoints.Admin;

[Trait("Category", "Integration")]
[Collection(AuthApiCollection.Name)]
public sealed class M2MClientsEndpointTests(AuthWebApplicationFactory factory)
{
    private readonly AuthWebApplicationFactory _factory = factory;

    [Fact]
    public async Task ListClients_WithoutToken_Returns401()
    {
        HttpClient http = _factory.CreateClient();
        HttpResponseMessage response = await http.GetAsync("/admin/m2m-clients/");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListClients_WithoutPermission_Returns403()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "m2m-403")).Id;
        HttpClient http = _factory.CreateAuthorizedClient(tenantId);
        HttpResponseMessage response = await http.GetAsync("/admin/m2m-clients/");
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListClients_WithReadPermission_Returns200()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "m2m-list")).Id;
        HttpClient http = _factory.CreateAuthorizedClient(tenantId, PermissionCodes.M2MClientsRead);
        HttpResponseMessage response = await http.GetAsync("/admin/m2m-clients/");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateClient_WithWritePermission_Returns201_WithSecret()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "m2m-create")).Id;
        HttpClient http = _factory.CreateAuthorizedClient(tenantId, PermissionCodes.M2MClientsWrite);

        HttpResponseMessage response = await http.PostAsJsonAsync(
            "/admin/m2m-clients/",
            new
            {
                clientId = "svc-" + Guid.NewGuid().ToString("N")[..8],
                displayName = "Service Account",
                allowedScopes = new[] { "api:web" },
            });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<CreateResponse>();
        body.ShouldNotBeNull();
        body!.ClientSecret.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreateClient_WithReadOnly_Returns403()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "m2m-create-403")).Id;
        HttpClient http = _factory.CreateAuthorizedClient(tenantId, PermissionCodes.M2MClientsRead);

        HttpResponseMessage response = await http.PostAsJsonAsync(
            "/admin/m2m-clients/",
            new { clientId = "x", displayName = "x", allowedScopes = new[] { "api:web" } });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private sealed record CreateResponse(Guid Id, string ClientId, string ClientSecret);
}
