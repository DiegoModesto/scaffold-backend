namespace Web.Blazor.IntegrationTests.Infrastructure;

/// <summary>
/// xUnit collection that shares a single <see cref="BffWebApplicationFactory"/> across
/// every BFF integration test class. The factory boots a Redis container + WireMock
/// servers and writes WireMock URLs into process-wide environment variables, so parallel
/// fixture instances would race. Serialise on a single fixture per process.
/// </summary>
[CollectionDefinition(Name)]
public sealed class BffCollection : ICollectionFixture<BffWebApplicationFactory>
{
    public const string Name = "Bff";
}
