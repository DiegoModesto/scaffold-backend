using Gateway.IntegrationTests.Infrastructure;
using Shouldly;

namespace Gateway.IntegrationTests;

[Collection(GatewayCollection.Name)]
public sealed class HealthCheckTests(GatewayWebApplicationFactory factory)
{
    [Fact]
    public async Task HealthLive_Returns200()
    {
        HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/health/live");
        response.IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task HealthReady_Returns200_WhenAuthApiAndRedisAreUp()
    {
        HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/health/ready");
        response.IsSuccessStatusCode.ShouldBeTrue();
    }
}
