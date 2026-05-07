using Auth.Application.Abstractions.Data;
using Auth.Domain.Permissions;
using Auth.Infra.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Auth.Application.UnitTests.Infrastructure;

public sealed class PermissionSeedHostedServiceTests
{
    [Fact]
    public async Task StartAsync_Should_Insert_All_Permission_Codes_When_Database_Is_Empty()
    {
        await using ServiceProvider provider = BuildProvider();
        var seed = new PermissionSeedHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PermissionSeedHostedService>.Instance);

        await seed.StartAsync(CancellationToken.None);

        using IServiceScope scope = provider.CreateScope();
        IAuthDbContext db = scope.ServiceProvider.GetRequiredService<IAuthDbContext>();
        List<string> codes = await db.Permissions.Select(p => p.Code).ToListAsync();

        codes.Count.ShouldBe(PermissionCodes.All.Count);
        foreach (var entry in PermissionCodes.All)
        {
            codes.ShouldContain(entry.Code);
        }
    }

    [Fact]
    public async Task StartAsync_Should_Be_Idempotent_On_Subsequent_Runs()
    {
        await using ServiceProvider provider = BuildProvider();
        var seed = new PermissionSeedHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PermissionSeedHostedService>.Instance);

        await seed.StartAsync(CancellationToken.None);
        await seed.StartAsync(CancellationToken.None);

        using IServiceScope scope = provider.CreateScope();
        IAuthDbContext db = scope.ServiceProvider.GetRequiredService<IAuthDbContext>();
        int count = await db.Permissions.CountAsync();

        count.ShouldBe(PermissionCodes.All.Count);
    }

    private static ServiceProvider BuildProvider()
    {
        string dbName = $"perm-seed-{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<TestAuthDbContext>(opt => opt.UseInMemoryDatabase(dbName));
        services.AddScoped<IAuthDbContext>(sp => sp.GetRequiredService<TestAuthDbContext>());
        return services.BuildServiceProvider();
    }
}
