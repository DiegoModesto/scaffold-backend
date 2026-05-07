using System.Net;
using Auth.API.IntegrationTests.Infrastructure;
using Auth.Domain.Permissions;
using Shouldly;

namespace Auth.API.IntegrationTests.Endpoints.Admin;

[Trait("Category", "Integration")]
[Collection(AuthApiCollection.Name)]
public sealed class AuditEndpointTests(AuthWebApplicationFactory factory)
{
    private readonly AuthWebApplicationFactory _factory = factory;

    [Fact]
    public async Task ListAudit_WithoutToken_Returns401()
    {
        HttpClient http = _factory.CreateClient();
        HttpResponseMessage response = await http.GetAsync("/admin/audit/");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListAudit_WithoutPermission_Returns403()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "audit-403")).Id;
        HttpClient http = _factory.CreateAuthorizedClient(tenantId);
        HttpResponseMessage response = await http.GetAsync("/admin/audit/");
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListAudit_WithAuditReadPermission_Returns200()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "audit-200")).Id;
        HttpClient http = _factory.CreateAuthorizedClient(tenantId, PermissionCodes.AuditRead);
        HttpResponseMessage response = await http.GetAsync("/admin/audit/");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ListAudit_WithUsersReadPermission_Returns403()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "audit-403-other")).Id;
        HttpClient http = _factory.CreateAuthorizedClient(tenantId, PermissionCodes.UsersRead);
        HttpResponseMessage response = await http.GetAsync("/admin/audit/");
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
