using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Shouldly;
using Web.API.IntegrationTests.Infrastructure;

namespace Web.API.IntegrationTests.Endpoints;

/// <summary>
/// The OpenAPI document is only mapped in Development, so these tests flip the environment
/// on top of the shared factory. They guard the built-in generator wiring (AddOpenApi +
/// versioned "v1" document + Bearer security scheme).
/// </summary>
[Collection(WebApiCollection.Name)]
public sealed class OpenApiDocumentTests(CustomWebApplicationFactory factory)
{
    private HttpClient CreateDevelopmentClient() =>
        factory.WithWebHostBuilder(b => b.UseEnvironment("Development")).CreateClient();

    [Fact]
    public async Task Should_ServeVersionedDocument_WithVersionSubstitutedInPaths()
    {
        HttpClient client = CreateDevelopmentClient();

        HttpResponseMessage response = await client.GetAsync("/openapi/v1.json");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement paths = doc.RootElement.GetProperty("paths");
        paths.TryGetProperty("/api/v1/sample-entities", out _).ShouldBeTrue(paths.ToString());
    }

    [Fact]
    public async Task Should_DeclareBearerSecurityScheme()
    {
        HttpClient client = CreateDevelopmentClient();

        HttpResponseMessage response = await client.GetAsync("/openapi/v1.json");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement scheme = doc.RootElement
            .GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        scheme.GetProperty("type").GetString().ShouldBe("http");
        scheme.GetProperty("scheme").GetString().ShouldBe("bearer");
        doc.RootElement.GetProperty("security").GetArrayLength().ShouldBeGreaterThan(0);
    }
}
