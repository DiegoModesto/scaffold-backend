using Auth.Application.Admin.Permissions.ListPermissions;
using Auth.Application.UnitTests.Infrastructure;
using Auth.Domain.Permissions;
using Shouldly;

namespace Auth.Application.UnitTests.Admin.Permissions;

public class ListPermissionsQueryHandlerTests
{
    [Fact]
    public async Task Should_ReturnAllPermissions_OrderedByCode()
    {
        await using var ctx = TestAuthDbContext.Create();
        ctx.Permissions.Add(Permission.Create("users.write", "Write users"));
        ctx.Permissions.Add(Permission.Create("audit.read", "Read audit"));
        ctx.Permissions.Add(Permission.Create("groups.read", "Read groups"));
        await ctx.SaveChangesAsync();

        var handler = new ListPermissionsQueryHandler(ctx);

        var result = await handler.Handle(new ListPermissionsQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(3);
        result.Value.Select(p => p.Code).ShouldBe(["audit.read", "groups.read", "users.write"]);
    }

    [Fact]
    public async Task Should_ReturnEmpty_WhenNoneSeeded()
    {
        await using var ctx = TestAuthDbContext.Create();
        var handler = new ListPermissionsQueryHandler(ctx);

        var result = await handler.Handle(new ListPermissionsQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }
}
