using Auth.Application.Admin.Roles.AssignPermission;
using Auth.Application.Admin.Roles.Create;
using Auth.Application.Admin.Roles.Delete;
using Auth.Application.Admin.Roles.GetRole;
using Auth.Application.Admin.Roles.ListRoles;
using Auth.Application.Admin.Roles.RevokePermission;
using Auth.Application.Admin.Roles.Update;
using Auth.Application.UnitTests.Infrastructure;
using Auth.Domain.Permissions;
using Auth.Domain.Roles;
using Shouldly;

namespace Auth.Application.UnitTests.Admin.Roles;

public class RolesAdminHandlersTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static StubTenantContext Tenant() => new(TenantId);

    [Fact]
    public async Task ListRoles_Should_Paginate()
    {
        await using var ctx = TestAuthDbContext.Create();
        ctx.Roles.Add(Role.Create(TenantId, "Admin", "Admin"));
        ctx.Roles.Add(Role.Create(TenantId, "User", "User"));
        ctx.Roles.Add(Role.Create(Guid.NewGuid(), "Other", "Other"));
        await ctx.SaveChangesAsync();

        var handler = new ListRolesQueryHandler(ctx, Tenant());

        var result = await handler.Handle(new ListRolesQuery(1, 10, null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Total.ShouldBe(2);
    }

    [Fact]
    public async Task GetRoleById_Should_ReturnDetailWithPermissions()
    {
        await using var ctx = TestAuthDbContext.Create();
        var permission = Permission.Create("users.read", "Read users");
        var role = Role.Create(TenantId, "Admin", "Admin role");
        role.AssignPermission(permission.Id);
        ctx.Permissions.Add(permission);
        ctx.Roles.Add(role);
        await ctx.SaveChangesAsync();

        var handler = new GetRoleByIdQueryHandler(ctx, Tenant());

        var result = await handler.Handle(new GetRoleByIdQuery(role.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AssignedPermissions.Single().Code.ShouldBe("users.read");
    }

    [Fact]
    public async Task GetRoleById_Should_FailNotFound()
    {
        await using var ctx = TestAuthDbContext.Create();
        var handler = new GetRoleByIdQueryHandler(ctx, Tenant());

        var result = await handler.Handle(new GetRoleByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Role.NotFound");
    }

    [Fact]
    public async Task CreateRole_Should_Succeed()
    {
        await using var ctx = TestAuthDbContext.Create();
        var handler = new CreateRoleCommandHandler(ctx, Tenant());

        var result = await handler.Handle(new CreateRoleCommand("Admin", "Admin role"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var stored = await ctx.Roles.FindAsync(result.Value);
        stored.ShouldNotBeNull();
    }

    [Fact]
    public async Task CreateRole_Should_FailConflict_OnDuplicate()
    {
        await using var ctx = TestAuthDbContext.Create();
        ctx.Roles.Add(Role.Create(TenantId, "Admin", "Admin role"));
        await ctx.SaveChangesAsync();
        var handler = new CreateRoleCommandHandler(ctx, Tenant());

        var result = await handler.Handle(new CreateRoleCommand("Admin", "Other"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Role.NameAlreadyTaken");
    }

    [Fact]
    public async Task UpdateRole_Should_UpdateFields()
    {
        await using var ctx = TestAuthDbContext.Create();
        var role = Role.Create(TenantId, "Admin", "Admin");
        ctx.Roles.Add(role);
        await ctx.SaveChangesAsync();
        var handler = new UpdateRoleCommandHandler(ctx, Tenant());

        var result = await handler.Handle(
            new UpdateRoleCommand(role.Id, "Administrator", "New desc"),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        role.Name.ShouldBe("Administrator");
    }

    [Fact]
    public async Task DeleteRole_Should_SoftDelete()
    {
        await using var ctx = TestAuthDbContext.Create();
        var role = Role.Create(TenantId, "Admin", "Admin");
        ctx.Roles.Add(role);
        await ctx.SaveChangesAsync();
        var handler = new DeleteRoleCommandHandler(ctx, Tenant());

        var result = await handler.Handle(new DeleteRoleCommand(role.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        role.IsDeleted.ShouldBeTrue();
    }

    [Fact]
    public async Task AssignPermission_Should_Succeed()
    {
        await using var ctx = TestAuthDbContext.Create();
        var role = Role.Create(TenantId, "Admin", "Admin");
        var permission = Permission.Create("users.read", "Read users");
        ctx.Roles.Add(role);
        ctx.Permissions.Add(permission);
        await ctx.SaveChangesAsync();

        var handler = new AssignPermissionToRoleCommandHandler(ctx, Tenant());

        var result = await handler.Handle(
            new AssignPermissionToRoleCommand(role.Id, permission.Id),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        role.PermissionIds.ShouldContain(permission.Id);
    }

    [Fact]
    public async Task AssignPermission_Should_FailPermissionNotFound()
    {
        await using var ctx = TestAuthDbContext.Create();
        var role = Role.Create(TenantId, "Admin", "Admin");
        ctx.Roles.Add(role);
        await ctx.SaveChangesAsync();

        var handler = new AssignPermissionToRoleCommandHandler(ctx, Tenant());

        var result = await handler.Handle(
            new AssignPermissionToRoleCommand(role.Id, Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Permission.NotFound");
    }

    [Fact]
    public async Task RevokePermission_Should_Succeed()
    {
        await using var ctx = TestAuthDbContext.Create();
        var role = Role.Create(TenantId, "Admin", "Admin");
        var permId = Guid.NewGuid();
        role.AssignPermission(permId);
        ctx.Roles.Add(role);
        await ctx.SaveChangesAsync();

        var handler = new RevokePermissionFromRoleCommandHandler(ctx, Tenant());

        var result = await handler.Handle(
            new RevokePermissionFromRoleCommand(role.Id, permId),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        role.PermissionIds.ShouldNotContain(permId);
    }
}
