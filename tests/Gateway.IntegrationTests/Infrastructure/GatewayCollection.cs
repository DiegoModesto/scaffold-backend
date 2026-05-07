namespace Gateway.IntegrationTests.Infrastructure;

/// <summary>
/// xUnit collection that shares a single <see cref="GatewayWebApplicationFactory"/> across
/// every Gateway integration test class. The factory boots a Redis container + two WireMock
/// servers and wires their URLs through process-wide environment variables (so OpenIddict's
/// validation client picks them up at host build time). Parallel fixture instances would
/// trample each other's env vars, so we serialise on a single fixture per process.
/// </summary>
[CollectionDefinition(Name)]
public sealed class GatewayCollection : ICollectionFixture<GatewayWebApplicationFactory>
{
    public const string Name = "Gateway";
}
