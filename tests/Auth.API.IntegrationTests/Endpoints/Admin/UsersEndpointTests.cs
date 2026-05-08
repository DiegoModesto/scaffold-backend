using System.Net;
using System.Net.Http.Json;
using Auth.API.IntegrationTests.Infrastructure;
using Auth.Domain.Permissions;
using Shouldly;

namespace Auth.API.IntegrationTests.Endpoints.Admin;

[Trait("Category", "Integration")]
[Collection(AuthApiCollection.Name)]
public sealed class UsersEndpointTests(AuthWebApplicationFactory factory)
{
    private readonly AuthWebApplicationFactory _factory = factory;

    [Fact]
    public async Task ListUsers_WithoutToken_Returns401()
    {
        HttpClient http = _factory.CreateClient();
        HttpResponseMessage response = await http.GetAsync("/admin/users/");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListUsers_WithoutPermission_Returns403()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "users-403")).Id;
        HttpClient http = _factory.CreateAuthorizedClient(tenantId /* no permissions */);

        HttpResponseMessage response = await http.GetAsync("/admin/users/");
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListUsers_WithUsersReadPermission_Returns200()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "users-list")).Id;
        await _factory.SeedUserAsync(tenantId, Guid.NewGuid(), "alpha@example.com");

        HttpClient http = _factory.CreateAuthorizedClient(tenantId, PermissionCodes.UsersRead);

        HttpResponseMessage response = await http.GetAsync("/admin/users/");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PreProvisionUser_WithUsersWritePermission_Returns201()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "users-pre")).Id;
        HttpClient http = _factory.CreateAuthorizedClient(tenantId, PermissionCodes.UsersWrite);

        HttpResponseMessage response = await http.PostAsJsonAsync(
            "/admin/users/pre-provision",
            new { email = "new@example.com", displayName = "New User", netSuiteEmail = (string?)null });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task PreProvisionUser_WithUsersReadOnly_Returns403()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "users-pre-403")).Id;
        HttpClient http = _factory.CreateAuthorizedClient(tenantId, PermissionCodes.UsersRead);

        HttpResponseMessage response = await http.PostAsJsonAsync(
            "/admin/users/pre-provision",
            new { email = "x@example.com", displayName = "X", netSuiteEmail = (string?)null });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DisableUser_WithUsersWritePermission_Returns204()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "users-disable")).Id;
        var user = await _factory.SeedUserAsync(tenantId, Guid.NewGuid(), "to-disable@example.com");
        HttpClient http = _factory.CreateAuthorizedClient(tenantId, PermissionCodes.UsersWrite);

        HttpResponseMessage response = await http.PostAsync($"/admin/users/{user.Id}/disable", null);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GetUserById_WhenMissing_Returns404()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "users-missing")).Id;
        HttpClient http = _factory.CreateAuthorizedClient(tenantId, PermissionCodes.UsersRead);

        HttpResponseMessage response = await http.GetAsync($"/admin/users/{Guid.NewGuid()}");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
