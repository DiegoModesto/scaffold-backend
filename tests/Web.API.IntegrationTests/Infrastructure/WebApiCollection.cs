namespace Web.API.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class WebApiCollection : ICollectionFixture<CustomWebApplicationFactory>
{
    public const string Name = "WebApi";
}
