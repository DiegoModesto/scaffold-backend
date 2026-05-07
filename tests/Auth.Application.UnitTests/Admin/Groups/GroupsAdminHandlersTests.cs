using Auth.Application.Admin.Groups.AssignRole;
using Auth.Application.Admin.Groups.Create;
using Auth.Application.Admin.Groups.Delete;
using Auth.Application.Admin.Groups.GetGroup;
using Auth.Application.Admin.Groups.ListGroups;
using Auth.Application.Admin.Groups.RevokeRole;
using Auth.Application.Admin.Groups.Update;
using Auth.Application.UnitTests.Infrastructure;
using Auth.Domain.Groups;
using Auth.Domain.Roles;
using Shouldly;

namespace Auth.Application.UnitTests.Admin.Groups;

public class GroupsAdminHandlersTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static StubTenantContext Tenant() => new(TenantId);

    [Fact]
    public async Task ListGroups_Should_PaginateAndFilter()
    {
        await using var ctx = TestAuthDbContext.Create();
        ctx.Groups.Add(Group.Create(TenantId, "Eng", "Engineering"));
        ctx.Groups.Add(Group.Create(TenantId, "Sales", "Sales team"));
        ctx.Groups.Add(Group.Create(Guid.NewGuid(), "Other", "Other tenant"));
        await ctx.SaveChangesAsync();

        var handler = new ListGroupsQueryHandler(ctx, Tenant());

        var result = await handler.Handle(new ListGroupsQuery(1, 10, null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Total.ShouldBe(2);
    }

    [Fact]
    public async Task GetGroupById_Should_ReturnDetail()
    {
        await using var ctx = TestAuthDbContext.Create();
        var group = Group.Create(TenantId, "Eng", "Engineering");
        var role = Role.Create(TenantId, "Admin", "Admin role");
        group.AssignRole(role.Id);
        ctx.Groups.Add(group);
        ctx.Roles.Add(role);
        await ctx.SaveChangesAsync();

        var handler = new GetGroupByIdQueryHandler(ctx, Tenant());

        var result = await handler.Handle(new GetGroupByIdQuery(group.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AssignedRoles.Single().Name.ShouldBe("Admin");
    }

    [Fact]
    public async Task GetGroupById_Should_FailNotFound()
    {
        await using var ctx = TestAuthDbContext.Create();
        var handler = new GetGroupByIdQueryHandler(ctx, Tenant());

        var result = await handler.Handle(new GetGroupByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Group.NotFound");
    }

    [Fact]
    public async Task CreateGroup_Should_Succeed()
    {
        await using var ctx = TestAuthDbContext.Create();
        var handler = new CreateGroupCommandHandler(ctx, Tenant());

        var result = await handler.Handle(
            new CreateGroupCommand("Eng", "Engineering", null),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var stored = await ctx.Groups.FindAsync(result.Value);
        stored.ShouldNotBeNull();
        stored!.TenantId.ShouldBe(TenantId);
    }

    [Fact]
    public async Task CreateGroup_Should_FailConflict_OnDuplicateName()
    {
        await using var ctx = TestAuthDbContext.Create();
        ctx.Groups.Add(Group.Create(TenantId, "Eng", "Engineering"));
        await ctx.SaveChangesAsync();
        var handler = new CreateGroupCommandHandler(ctx, Tenant());

        var result = await handler.Handle(
            new CreateGroupCommand("Eng", "Other desc", null),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Group.NameAlreadyTaken");
    }

    [Fact]
    public async Task UpdateGroup_Should_UpdateFields()
    {
        await using var ctx = TestAuthDbContext.Create();
        var group = Group.Create(TenantId, "Eng", "Engineering");
        ctx.Groups.Add(group);
        await ctx.SaveChangesAsync();

        var handler = new UpdateGroupCommandHandler(ctx, Tenant());

        var result = await handler.Handle(
            new UpdateGroupCommand(group.Id, "Engineering", "Eng team", null),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        group.Name.ShouldBe("Engineering");
        group.Description.ShouldBe("Eng team");
    }

    [Fact]
    public async Task DeleteGroup_Should_SoftDelete()
    {
        await using var ctx = TestAuthDbContext.Create();
        var group = Group.Create(TenantId, "Eng", "Engineering");
        ctx.Groups.Add(group);
        await ctx.SaveChangesAsync();

        var handler = new DeleteGroupCommandHandler(ctx, Tenant());

        var result = await handler.Handle(new DeleteGroupCommand(group.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        group.IsDeleted.ShouldBeTrue();
    }

    [Fact]
    public async Task AssignRoleToGroup_Should_Succeed()
    {
        await using var ctx = TestAuthDbContext.Create();
        var group = Group.Create(TenantId, "Eng", "Engineering");
        var role = Role.Create(TenantId, "Admin", "Admin role");
        ctx.Groups.Add(group);
        ctx.Roles.Add(role);
        await ctx.SaveChangesAsync();

        var handler = new AssignRoleToGroupCommandHandler(ctx, Tenant());

        var result = await handler.Handle(
            new AssignRoleToGroupCommand(group.Id, role.Id),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        group.RoleIds.ShouldContain(role.Id);
    }

    [Fact]
    public async Task AssignRoleToGroup_Should_FailRoleNotFound()
    {
        await using var ctx = TestAuthDbContext.Create();
        var group = Group.Create(TenantId, "Eng", "Engineering");
        ctx.Groups.Add(group);
        await ctx.SaveChangesAsync();

        var handler = new AssignRoleToGroupCommandHandler(ctx, Tenant());

        var result = await handler.Handle(
            new AssignRoleToGroupCommand(group.Id, Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Role.NotFound");
    }

    [Fact]
    public async Task RevokeRoleFromGroup_Should_Succeed()
    {
        await using var ctx = TestAuthDbContext.Create();
        var group = Group.Create(TenantId, "Eng", "Engineering");
        var roleId = Guid.NewGuid();
        group.AssignRole(roleId);
        ctx.Groups.Add(group);
        await ctx.SaveChangesAsync();

        var handler = new RevokeRoleFromGroupCommandHandler(ctx, Tenant());

        var result = await handler.Handle(
            new RevokeRoleFromGroupCommand(group.Id, roleId),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        group.RoleIds.ShouldNotContain(roleId);
    }
}
