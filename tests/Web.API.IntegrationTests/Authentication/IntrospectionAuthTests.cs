using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Domain.SampleEntities;
using Infra.Database;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Web.API.IntegrationTests.Infrastructure;

namespace Web.API.IntegrationTests.Authentication;

public sealed class IntrospectionAuthTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public IntrospectionAuthTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Endpoint_NoToken_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync($"/api/v1/sample-entities/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Endpoint_InactiveToken_Returns401()
    {
        string token = $"inactive-{Guid.NewGuid():N}";
        _factory.StubIntrospectionInactive(token);

        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response = await client.GetAsync($"/api/v1/sample-entities/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Endpoint_ActiveTokenWithoutPermission_Returns403()
    {
        // Active token but no `sample.read` permission claim.
        string token = _factory.IssueTestToken(Guid.NewGuid(), "user-1");

        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response = await client.GetAsync($"/api/v1/sample-entities/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Endpoint_ActiveTokenWithPermission_Returns200()
    {
        string token = _factory.IssueTestToken(Guid.NewGuid(), "user-1", "sample.read");

        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response = await client.GetAsync($"/api/v1/sample-entities/{Guid.NewGuid()}");

        // Authorized but the entity doesn't exist => 404 (NOT 401/403).
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TenantClaim_ExposedViaIUserContext_OnHandler()
    {
        Guid tenantId = Guid.NewGuid();
        string token = _factory.IssueTestToken(tenantId, "user-1", "sample.write");

        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage create = await client.PostAsJsonAsync(
            "/api/v1/sample-entities",
            new { name = "Tenant scoped", description = "x" });

        create.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Verify the persisted entity carries the tenant id from the introspection claim,
        // proving IUserContext.TenantId is wired through the request pipeline.
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SampleEntity? entity = db.SampleEntities.OrderByDescending(e => e.CreatedAt).FirstOrDefault();
        entity.ShouldNotBeNull();
        entity.TenantId.ShouldBe(tenantId);
    }
}
