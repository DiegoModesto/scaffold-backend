using Auth.Application.Users.SyncEntra;
using Auth.Application.UnitTests.Infrastructure;
using Auth.Domain.Users;
using Shouldly;

namespace Auth.Application.UnitTests.Users.SyncEntra;

public class SyncEntraUserCommandHandlerTests
{
    [Fact]
    public async Task Should_RecordLoginAndReturnId_WhenUserExistsByEntraOid()
    {
        await using var ctx = TestAuthDbContext.Create();
        var tenantId = Guid.NewGuid();
        var oid = Guid.NewGuid();
        var existing = User.ProvisionFromEntra(tenantId, oid, "user@example.com", "Display");
        ctx.Users.Add(existing);
        await ctx.SaveChangesAsync();

        var handler = new SyncEntraUserCommandHandler(ctx);

        var result = await handler.Handle(
            new SyncEntraUserCommand(tenantId, oid, "user@example.com", "Display"),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(existing.Id);
        existing.LastLoginAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Should_FailDisabled_WhenUserByOidIsInactive()
    {
        await using var ctx = TestAuthDbContext.Create();
        var tenantId = Guid.NewGuid();
        var oid = Guid.NewGuid();
        var existing = User.ProvisionFromEntra(tenantId, oid, "user@example.com", "Display");
        existing.Disable();
        ctx.Users.Add(existing);
        await ctx.SaveChangesAsync();

        var handler = new SyncEntraUserCommandHandler(ctx);

        var result = await handler.Handle(
            new SyncEntraUserCommand(tenantId, oid, "user@example.com", "Display"),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("User.Disabled");
    }

    [Fact]
    public async Task Should_ActivateAndReturnId_WhenPreProvisionedUserMatchesByEmail()
    {
        await using var ctx = TestAuthDbContext.Create();
        var tenantId = Guid.NewGuid();
        var pre = User.PreProvision(tenantId, "user@example.com", "Old Name");
        ctx.Users.Add(pre);
        await ctx.SaveChangesAsync();

        var oid = Guid.NewGuid();
        var handler = new SyncEntraUserCommandHandler(ctx);

        var result = await handler.Handle(
            new SyncEntraUserCommand(tenantId, oid, "user@example.com", "New Name"),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(pre.Id);
        pre.IsActive.ShouldBeTrue();
        pre.IsPreProvisioned.ShouldBeFalse();
        pre.EntraOid.ShouldBe(oid);
        pre.DisplayName.ShouldBe("New Name");
        pre.LastLoginAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Should_FailDisabled_WhenUserByEmailNotPreProvisioned()
    {
        await using var ctx = TestAuthDbContext.Create();
        var tenantId = Guid.NewGuid();
        var existing = User.ProvisionFromEntra(tenantId, Guid.NewGuid(), "user@example.com", "Display");
        existing.Disable();
        ctx.Users.Add(existing);
        await ctx.SaveChangesAsync();

        var handler = new SyncEntraUserCommandHandler(ctx);

        // Different oid -> won't match by oid; will match by email; not pre-provisioned -> Disabled
        var result = await handler.Handle(
            new SyncEntraUserCommand(tenantId, Guid.NewGuid(), "user@example.com", "Display"),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("User.Disabled");
    }

    [Fact]
    public async Task Should_ProvisionAndReturnId_WhenUserNotFound()
    {
        await using var ctx = TestAuthDbContext.Create();
        var tenantId = Guid.NewGuid();
        var oid = Guid.NewGuid();
        var handler = new SyncEntraUserCommandHandler(ctx);

        var result = await handler.Handle(
            new SyncEntraUserCommand(tenantId, oid, "user@example.com", "Display"),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var stored = await ctx.Users.FindAsync(result.Value);
        stored.ShouldNotBeNull();
        stored!.TenantId.ShouldBe(tenantId);
        stored.EntraOid.ShouldBe(oid);
        stored.Email.ShouldBe("user@example.com");
        stored.DisplayName.ShouldBe("Display");
        stored.IsActive.ShouldBeTrue();
        stored.IsPreProvisioned.ShouldBeFalse();
        stored.LastLoginAt.ShouldNotBeNull();
    }
}
