using System.Net;
using Auth.API.IntegrationTests.Infrastructure;
using Auth.Domain.Permissions;
using Shouldly;

namespace Auth.API.IntegrationTests.Endpoints.Admin;

[Trait("Category", "Integration")]
[Collection(AuthApiCollection.Name)]
public sealed class PermissionsEndpointTests(AuthWebApplicationFactory factory)
{
    private readonly AuthWebApplicationFactory _factory = factory;

    [Fact]
    public async Task ListPermissions_WithoutToken_Returns401()
    {
        HttpClient http = _factory.CreateClient();
        HttpResponseMessage response = await http.GetAsync("/admin/permissions/");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListPermissions_WithoutPermission_Returns403()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "perms-403")).Id;
        HttpClient http = _factory.CreateAuthorizedClient(tenantId);
        HttpResponseMessage response = await http.GetAsync("/admin/permissions/");
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListPermissions_WithRolesReadPermission_Returns200()
    {
        // Listing the permission catalog is gated by roles.read because it is primarily
        // used during role configuration; see PermissionsEndpoints for the documented choice.
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "perms-200")).Id;
        HttpClient http = _factory.CreateAuthorizedClient(tenantId, PermissionCodes.RolesRead);
        HttpResponseMessage response = await http.GetAsync("/admin/permissions/");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
