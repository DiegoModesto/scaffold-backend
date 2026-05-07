using Auth.Application.Admin.Users.AddToGroup;
using Auth.Application.Admin.Users.AssignRole;
using Auth.Application.Admin.Users.Disable;
using Auth.Application.Admin.Users.Enable;
using Auth.Application.Admin.Users.GetUser;
using Auth.Application.Admin.Users.ListUsers;
using Auth.Application.Admin.Users.PreProvision;
using Auth.Application.Admin.Users.RemoveFromGroup;
using Auth.Application.Admin.Users.RevokeRole;
using Auth.Application.Admin.Users.SetNetSuiteEmail;
using Auth.Application.UnitTests.Infrastructure;
using Auth.Domain.Groups;
using Auth.Domain.Roles;
using Auth.Domain.Users;
using Shouldly;

namespace Auth.Application.UnitTests.Admin.Users;

public class UsersAdminHandlersTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();

    private static StubTenantContext Tenant() => new(TenantId);

    [Fact]
    public async Task ListUsers_Should_PaginateAndFilterByTenant()
    {
        await using var ctx = TestAuthDbContext.Create();
        ctx.Users.Add(User.PreProvision(TenantId, "alice@example.com", "Alice"));
        ctx.Users.Add(User.PreProvision(TenantId, "bob@example.com", "Bob"));
        ctx.Users.Add(User.PreProvision(OtherTenantId, "eve@example.com", "Eve"));
        await ctx.SaveChangesAsync();

        var handler = new ListUsersQueryHandler(ctx, Tenant());

        var result = await handler.Handle(new ListUsersQuery(1, 10, null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Total.ShouldBe(2);
        result.Value.Items.Count.ShouldBe(2);
        result.Value.Items.ShouldAllBe(s => s.Email != "eve@example.com");
    }

    [Fact]
    public async Task ListUsers_Should_FilterBySearch()
    {
        await using var ctx = TestAuthDbContext.Create();
        ctx.Users.Add(User.PreProvision(TenantId, "alice@example.com", "Alice"));
        ctx.Users.Add(User.PreProvision(TenantId, "bob@example.com", "Bob"));
        await ctx.SaveChangesAsync();

        var handler = new ListUsersQueryHandler(ctx, Tenant());

        var result = await handler.Handle(new ListUsersQuery(1, 10, "alice"), CancellationToken.None);

        result.Value.Total.ShouldBe(1);
        result.Value.Items.Single().Email.ShouldBe("alice@example.com");
    }

    [Fact]
    public async Task GetUserById_Should_ReturnDetail_WithRolesAndGroups()
    {
        await using var ctx = TestAuthDbContext.Create();
        var user = User.PreProvision(TenantId, "alice@example.com", "Alice");
        var role = Role.Create(TenantId, "Admin", "Admin role");
        var group = Group.Create(TenantId, "Engineers", "Eng team");
        ctx.Users.Add(user);
        ctx.Roles.Add(role);
        ctx.Groups.Add(group);
        user.AssignRole(role.Id);
        user.AddToGroup(group.Id);
        await ctx.SaveChangesAsync();

        var handler = new GetUserByIdQueryHandler(ctx, Tenant());

        var result = await handler.Handle(new GetUserByIdQuery(user.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Id.ShouldBe(user.Id);
        result.Value.AssignedRoles.Single().Name.ShouldBe("Admin");
        result.Value.AssignedGroups.Single().Name.ShouldBe("Engineers");
    }

    [Fact]
    public async Task GetUserById_Should_FailNotFound_WhenMissing()
    {
        await using var ctx = TestAuthDbContext.Create();
        var handler = new GetUserByIdQueryHandler(ctx, Tenant());

        var result = await handler.Handle(new GetUserByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("User.NotFound");
    }

    [Fact]
    public async Task PreProvision_Should_CreateUser()
    {
        await using var ctx = TestAuthDbContext.Create();
        var handler = new PreProvisionUserCommandHandler(ctx, Tenant());

        var result = await handler.Handle(
            new PreProvisionUserCommand("alice@example.com", "Alice", "alice@ns.example.com"),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var stored = await ctx.Users.FindAsync(result.Value);
        stored.ShouldNotBeNull();
        stored!.IsPreProvisioned.ShouldBeTrue();
        stored.NetSuiteEmail.ShouldBe("alice@ns.example.com");
        stored.TenantId.ShouldBe(TenantId);
    }

    [Fact]
    public async Task PreProvision_Should_FailConflict_WhenEmailExists()
    {
        await using var ctx = TestAuthDbContext.Create();
        ctx.Users.Add(User.PreProvision(TenantId, "alice@example.com", "Alice"));
        await ctx.SaveChangesAsync();
        var handler = new PreProvisionUserCommandHandler(ctx, Tenant());

        var result = await handler.Handle(
            new PreProvisionUserCommand("alice@example.com", "Alice", null),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("User.EmailAlreadyTaken");
    }

    [Fact]
    public async Task Disable_Should_DisableUser()
    {
        await using var ctx = TestAuthDbContext.Create();
        var user = User.ProvisionFromEntra(TenantId, Guid.NewGuid(), "alice@example.com", "Alice");
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var handler = new DisableUserCommandHandler(ctx, Tenant());

        var result = await handler.Handle(new DisableUserCommand(user.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        user.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Disable_Should_FailNotFound()
    {
        await using var ctx = TestAuthDbContext.Create();
        var handler = new DisableUserCommandHandler(ctx, Tenant());

        var result = await handler.Handle(new DisableUserCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("User.NotFound");
    }

    [Fact]
    public async Task Enable_Should_EnableUser()
    {
        await using var ctx = TestAuthDbContext.Create();
        var user = User.PreProvision(TenantId, "alice@example.com", "Alice");
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var handler = new EnableUserCommandHandler(ctx, Tenant());

        var result = await handler.Handle(new EnableUserCommand(user.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        user.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task SetNetSuiteEmail_Should_UpdateEmail()
    {
        await using var ctx = TestAuthDbContext.Create();
        var user = User.PreProvision(TenantId, "alice@example.com", "Alice");
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var handler = new SetNetSuiteEmailCommandHandler(ctx, Tenant());

        var result = await handler.Handle(
            new SetNetSuiteEmailCommand(user.Id, "ns@example.com"),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        user.NetSuiteEmail.ShouldBe("ns@example.com");
    }

    [Fact]
    public async Task SetNetSuiteEmail_Should_AcceptNullToClear()
    {
        await using var ctx = TestAuthDbContext.Create();
        var user = User.PreProvision(TenantId, "alice@example.com", "Alice");
        user.SetNetSuiteEmail("old@example.com");
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var handler = new SetNetSuiteEmailCommandHandler(ctx, Tenant());

        var result = await handler.Handle(new SetNetSuiteEmailCommand(user.Id, null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        user.NetSuiteEmail.ShouldBeNull();
    }

    [Fact]
    public async Task AssignRole_Should_Succeed()
    {
        await using var ctx = TestAuthDbContext.Create();
        var user = User.PreProvision(TenantId, "alice@example.com", "Alice");
        var role = Role.Create(TenantId, "Admin", "Admin");
        ctx.Users.Add(user);
        ctx.Roles.Add(role);
        await ctx.SaveChangesAsync();

        var handler = new AssignRoleToUserCommandHandler(ctx, Tenant());

        var result = await handler.Handle(new AssignRoleToUserCommand(user.Id, role.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        user.RoleIds.ShouldContain(role.Id);
    }

    [Fact]
    public async Task AssignRole_Should_FailRoleNotFound()
    {
        await using var ctx = TestAuthDbContext.Create();
        var user = User.PreProvision(TenantId, "alice@example.com", "Alice");
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var handler = new AssignRoleToUserCommandHandler(ctx, Tenant());

        var result = await handler.Handle(
            new AssignRoleToUserCommand(user.Id, Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Role.NotFound");
    }

    [Fact]
    public async Task RevokeRole_Should_Succeed()
    {
        await using var ctx = TestAuthDbContext.Create();
        var user = User.PreProvision(TenantId, "alice@example.com", "Alice");
        var roleId = Guid.NewGuid();
        user.AssignRole(roleId);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var handler = new RevokeRoleFromUserCommandHandler(ctx, Tenant());

        var result = await handler.Handle(
            new RevokeRoleFromUserCommand(user.Id, roleId),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        user.RoleIds.ShouldNotContain(roleId);
    }

    [Fact]
    public async Task AddToGroup_Should_Succeed()
    {
        await using var ctx = TestAuthDbContext.Create();
        var user = User.PreProvision(TenantId, "alice@example.com", "Alice");
        var group = Group.Create(TenantId, "Eng", "Engineering");
        ctx.Users.Add(user);
        ctx.Groups.Add(group);
        await ctx.SaveChangesAsync();

        var handler = new AddUserToGroupCommandHandler(ctx, Tenant());

        var result = await handler.Handle(
            new AddUserToGroupCommand(user.Id, group.Id),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        user.GroupIds.ShouldContain(group.Id);
    }

    [Fact]
    public async Task AddToGroup_Should_FailGroupNotFound()
    {
        await using var ctx = TestAuthDbContext.Create();
        var user = User.PreProvision(TenantId, "alice@example.com", "Alice");
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var handler = new AddUserToGroupCommandHandler(ctx, Tenant());

        var result = await handler.Handle(
            new AddUserToGroupCommand(user.Id, Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Group.NotFound");
    }

    [Fact]
    public async Task RemoveFromGroup_Should_Succeed()
    {
        await using var ctx = TestAuthDbContext.Create();
        var user = User.PreProvision(TenantId, "alice@example.com", "Alice");
        var groupId = Guid.NewGuid();
        user.AddToGroup(groupId);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var handler = new RemoveUserFromGroupCommandHandler(ctx, Tenant());

        var result = await handler.Handle(
            new RemoveUserFromGroupCommand(user.Id, groupId),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        user.GroupIds.ShouldNotContain(groupId);
    }
}
