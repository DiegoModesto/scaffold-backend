using System.Net;
using System.Net.Http.Json;
using Auth.API.IntegrationTests.Infrastructure;
using Auth.Domain.Permissions;
using Shouldly;

namespace Auth.API.IntegrationTests.Endpoints.Admin;

[Trait("Category", "Integration")]
[Collection(AuthApiCollection.Name)]
public sealed class RolesEndpointTests(AuthWebApplicationFactory factory)
{
    private readonly AuthWebApplicationFactory _factory = factory;

    [Fact]
    public async Task ListRoles_WithoutToken_Returns401()
    {
        HttpClient http = _factory.CreateClient();
        HttpResponseMessage response = await http.GetAsync("/admin/roles/");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListRoles_WithoutPermission_Returns403()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "roles-403")).Id;
        HttpClient http = _factory.CreateAuthorizedClient(tenantId);
        HttpResponseMessage response = await http.GetAsync("/admin/roles/");
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListRoles_WithRolesReadPermission_Returns200()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "roles-list")).Id;
        HttpClient http = _factory.CreateAuthorizedClient(tenantId, PermissionCodes.RolesRead);
        HttpResponseMessage response = await http.GetAsync("/admin/roles/");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateRole_WithRolesWritePermission_Returns201()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "roles-create")).Id;
        HttpClient http = _factory.CreateAuthorizedClient(tenantId, PermissionCodes.RolesWrite);

        HttpResponseMessage response = await http.PostAsJsonAsync(
            "/admin/roles/",
            new { name = "Admin", description = "Tenant administrator" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateRole_WithReadOnly_Returns403()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "roles-create-403")).Id;
        HttpClient http = _factory.CreateAuthorizedClient(tenantId, PermissionCodes.RolesRead);

        HttpResponseMessage response = await http.PostAsJsonAsync(
            "/admin/roles/",
            new { name = "Admin", description = "x" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
