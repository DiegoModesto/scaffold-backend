using Auth.API.IntegrationTests.Infrastructure;
using Auth.Domain.Audit;
using Auth.Infra.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Auth.API.IntegrationTests.Endpoints;

[Trait("Category", "Integration")]
[Collection(AuthApiCollection.Name)]
public sealed class AuditTests(AuthWebApplicationFactory factory)
{
    private readonly AuthWebApplicationFactory _factory = factory;

    [Fact]
    public async Task ClientCredentials_RecordsM2MTokenIssuedAudit_OnSuccess()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "audit-tenant")).Id;
        const string clientId = "audit-ccg-client";
        const string secret = "audit-ccg-secret";
        await _factory.SeedM2MClientAsync(tenantId, clientId, secret, "api:web");

        HttpClient http = _factory.CreateClient();

        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", secret),
            new KeyValuePair<string, string>("scope", "api:web"),
        });

        HttpResponseMessage response = await http.PostAsync("/connect/token", form);
        response.IsSuccessStatusCode.ShouldBeTrue(
            $"/connect/token returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        AuthAuditEvent[] events = await db.AuditEvents
            .Where(a => a.TenantId == tenantId && a.EventType == AuthAuditEventType.M2MTokenIssued)
            .ToArrayAsync();

        events.Length.ShouldBeGreaterThanOrEqualTo(1);
        events[0].Detail.ShouldContain(clientId);
    }
}
