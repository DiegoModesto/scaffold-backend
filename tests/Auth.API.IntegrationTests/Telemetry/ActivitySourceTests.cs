using System.Diagnostics;
using Auth.API.IntegrationTests.Infrastructure;
using Shouldly;

namespace Auth.API.IntegrationTests.Telemetry;

[Trait("Category", "Integration")]
[Collection(AuthApiCollection.Name)]
public sealed class ActivitySourceTests(AuthWebApplicationFactory factory)
{
    private readonly AuthWebApplicationFactory _factory = factory;

    [Fact]
    public async Task ClientCredentials_RecordsTokenClientCredentialsActivity()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "otel-tenant")).Id;
        const string clientId = "otel-ccg-client";
        const string secret = "otel-ccg-secret";
        await _factory.SeedM2MClientAsync(tenantId, clientId, secret, "api:web");

        var captured = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Auth.API",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a => captured.Add(a),
        };
        ActivitySource.AddActivityListener(listener);

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

        captured.ShouldContain(a => a.OperationName == "TokenClientCredentials");
    }
}
