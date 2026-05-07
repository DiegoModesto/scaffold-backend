namespace Auth.API.IntegrationTests.Infrastructure;

/// <summary>
/// xUnit collection that shares a single <see cref="AuthWebApplicationFactory"/> across every
/// integration test class. The factory is heavyweight (boots two Docker containers + WireMock +
/// migrations), and process-wide env vars used to wire connection strings would be trampled by
/// parallel fixture instances. One factory => one container pair => deterministic config.
/// </summary>
[CollectionDefinition(Name)]
public sealed class AuthApiCollection : ICollectionFixture<AuthWebApplicationFactory>
{
    public const string Name = "AuthApi";
}
