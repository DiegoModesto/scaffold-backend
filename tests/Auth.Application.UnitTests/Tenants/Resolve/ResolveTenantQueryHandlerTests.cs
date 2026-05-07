using Auth.Application.Tenants.Resolve;
using Auth.Application.UnitTests.Infrastructure;
using Auth.Domain.Tenants;
using Shouldly;

namespace Auth.Application.UnitTests.Tenants.Resolve;

public class ResolveTenantQueryHandlerTests
{
    [Fact]
    public async Task Should_ReturnTenant_WhenEntraTenantIdMatches()
    {
        await using var ctx = TestAuthDbContext.Create();
        var entraTenantId = Guid.NewGuid();
        var tenant = Tenant.Create(entraTenantId, "Acme", "https://acme.example/cb");
        ctx.Tenants.Add(tenant);
        await ctx.SaveChangesAsync();

        var handler = new ResolveTenantQueryHandler(ctx);

        var result = await handler.Handle(new ResolveTenantQuery(entraTenantId), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Id.ShouldBe(tenant.Id);
        result.Value.EntraTenantId.ShouldBe(entraTenantId);
        result.Value.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Should_FailNotRegistered_WhenNoMatch()
    {
        await using var ctx = TestAuthDbContext.Create();
        var handler = new ResolveTenantQueryHandler(ctx);

        var result = await handler.Handle(new ResolveTenantQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Tenant.NotRegistered");
    }

    [Fact]
    public async Task Should_FailInactive_WhenTenantIsDeactivated()
    {
        await using var ctx = TestAuthDbContext.Create();
        var entraTenantId = Guid.NewGuid();
        var tenant = Tenant.Create(entraTenantId, "Acme", "https://acme.example/cb");
        tenant.Deactivate();
        ctx.Tenants.Add(tenant);
        await ctx.SaveChangesAsync();

        var handler = new ResolveTenantQueryHandler(ctx);

        var result = await handler.Handle(new ResolveTenantQuery(entraTenantId), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Tenant.Inactive");
    }
}
