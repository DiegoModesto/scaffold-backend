using Auth.API.IntegrationTests.Infrastructure;
using Shouldly;

namespace Auth.API.IntegrationTests;

[Trait("Category", "Integration")]
[Collection(AuthApiCollection.Name)]
public sealed class HealthTests(AuthWebApplicationFactory factory)
{
    private readonly AuthWebApplicationFactory _factory = factory;

    [Fact]
    public async Task HealthLive_Returns200()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health/live");

        response.IsSuccessStatusCode.ShouldBeTrue(
            $"Expected 2xx but got {(int)response.StatusCode} {response.StatusCode}");
    }

    [Fact]
    public async Task HealthReady_Returns200_WhenDbAndRedisReachable()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health/ready");

        response.IsSuccessStatusCode.ShouldBeTrue(
            $"Expected 2xx but got {(int)response.StatusCode} {response.StatusCode}");
    }
}
