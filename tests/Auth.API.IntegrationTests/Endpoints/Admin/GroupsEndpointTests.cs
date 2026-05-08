using System.Net;
using System.Net.Http.Json;
using Auth.API.IntegrationTests.Infrastructure;
using Auth.Domain.Permissions;
using Shouldly;

namespace Auth.API.IntegrationTests.Endpoints.Admin;

[Trait("Category", "Integration")]
[Collection(AuthApiCollection.Name)]
public sealed class GroupsEndpointTests(AuthWebApplicationFactory factory)
{
    private readonly AuthWebApplicationFactory _factory = factory;

    [Fact]
    public async Task ListGroups_WithoutToken_Returns401()
    {
        HttpClient http = _factory.CreateClient();
        HttpResponseMessage response = await http.GetAsync("/admin/groups/");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListGroups_WithoutPermission_Returns403()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "groups-403")).Id;
        HttpClient http = _factory.CreateAuthorizedClient(tenantId);

        HttpResponseMessage response = await http.GetAsync("/admin/groups/");
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListGroups_WithGroupsReadPermission_Returns200()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "groups-list")).Id;
        HttpClient http = _factory.CreateAuthorizedClient(tenantId, PermissionCodes.GroupsRead);

        HttpResponseMessage response = await http.GetAsync("/admin/groups/");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateGroup_WithGroupsWritePermission_Returns201()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "groups-create")).Id;
        HttpClient http = _factory.CreateAuthorizedClient(tenantId, PermissionCodes.GroupsWrite);

        HttpResponseMessage response = await http.PostAsJsonAsync(
            "/admin/groups/",
            new { name = "Engineers", description = "Eng group", entraGroupId = (Guid?)null });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateGroup_WithReadOnlyPermission_Returns403()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "groups-create-403")).Id;
        HttpClient http = _factory.CreateAuthorizedClient(tenantId, PermissionCodes.GroupsRead);

        HttpResponseMessage response = await http.PostAsJsonAsync(
            "/admin/groups/",
            new { name = "X", description = "X", entraGroupId = (Guid?)null });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
