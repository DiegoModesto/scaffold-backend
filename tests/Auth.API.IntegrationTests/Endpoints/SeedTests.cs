using Auth.API.IntegrationTests.Infrastructure;
using Auth.Domain.Permissions;
using Auth.Infra.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Shouldly;

namespace Auth.API.IntegrationTests.Endpoints;

[Trait("Category", "Integration")]
[Collection(AuthApiCollection.Name)]
public sealed class SeedTests(AuthWebApplicationFactory factory)
{
    private readonly AuthWebApplicationFactory _factory = factory;

    [Fact]
    public async Task PermissionSeed_AllNineSystemPermissionsExist_AfterStartup()
    {
        // Force host startup.
        _ = _factory.CreateClient();

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        string[] codes = await db.Permissions.Select(p => p.Code).ToArrayAsync();

        string[] expected = [.. PermissionCodes.All.Select(p => p.Code)];
        foreach (string code in expected)
        {
            codes.ShouldContain(code, $"expected permission '{code}' to be seeded at startup");
        }
    }

    [Fact]
    public async Task OpenIddictClientSeed_RegistersBffWebApiAndGateway_AfterStartup()
    {
        _ = _factory.CreateClient();

        using IServiceScope scope = _factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        (await manager.FindByClientIdAsync("bff-blazor")).ShouldNotBeNull();
        (await manager.FindByClientIdAsync("web-api")).ShouldNotBeNull();
        (await manager.FindByClientIdAsync("gateway")).ShouldNotBeNull();
    }
}
