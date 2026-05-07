using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Shouldly;
using Web.API.IntegrationTests.Infrastructure;

namespace Web.API.IntegrationTests.Endpoints;

public class SampleEntityEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public SampleEntityEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Create_Should_Return401_WhenNoToken()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/sample-entities",
            new { name = "Foo", description = "bar" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_Should_Return400_WhenPayloadInvalid()
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CustomWebApplicationFactory.CreateBearerToken());

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/sample-entities",
            new { name = "", description = "bar" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_Should_Return201_AndAllow_Retrieval()
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CustomWebApplicationFactory.CreateBearerToken());

        HttpResponseMessage create = await client.PostAsJsonAsync(
            "/api/v1/sample-entities",
            new { name = "Integration Test", description = "desc" });

        create.StatusCode.ShouldBe(HttpStatusCode.Created);

        var body = await create.Content.ReadFromJsonAsync<CreateResponse>();
        body.ShouldNotBeNull();
        body.Id.ShouldNotBe(Guid.Empty);

        HttpResponseMessage get = await client.GetAsync($"/api/v1/sample-entities/{body.Id}");
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_Should_Return401_WhenNoToken()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync($"/api/v1/sample-entities/{Guid.NewGuid()}");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_Should_Return404_WhenNotFound()
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CustomWebApplicationFactory.CreateBearerToken());

        HttpResponseMessage response = await client.GetAsync($"/api/v1/sample-entities/{Guid.NewGuid()}");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Response_Should_Include_SecurityHeaders()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/health/live");

        response.Headers.Contains("X-Content-Type-Options").ShouldBeTrue();
        response.Headers.Contains("X-Frame-Options").ShouldBeTrue();
    }

    private sealed record CreateResponse(Guid Id);
}
