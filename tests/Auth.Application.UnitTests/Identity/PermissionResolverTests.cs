using Auth.Application.UnitTests.Infrastructure;
using Auth.Domain.Groups;
using Auth.Domain.Permissions;
using Auth.Domain.Roles;
using Auth.Domain.Users;
using Auth.Infra.Identity;
using Shouldly;

namespace Auth.Application.UnitTests.Identity;

public sealed class PermissionResolverTests
{
    [Fact]
    public async Task Should_ReturnPermissionsFromDirectRolesOnly()
    {
        await using TestAuthDbContext db = TestAuthDbContext.Create();
        Guid tenantId = Guid.NewGuid();

        Permission read = Permission.Create("orders.read", "Read orders");
        db.Permissions.Add(read);

        Role role = Role.Create(tenantId, "Reader", "Reader");
        role.AssignPermission(read.Id);
        db.Roles.Add(role);

        User user = User.ProvisionFromEntra(tenantId, Guid.NewGuid(), "a@b.com", "A");
        user.AssignRole(role.Id);
        db.Users.Add(user);

        await db.SaveChangesAsync(CancellationToken.None);

        var resolver = new PermissionResolver(db);

        var permissions = await resolver.ResolveAsync(tenantId, user.Id, CancellationToken.None);

        permissions.ShouldBe(new[] { "orders.read" });
    }

    [Fact]
    public async Task Should_ReturnPermissionsFromGroupRolesOnly()
    {
        await using TestAuthDbContext db = TestAuthDbContext.Create();
        Guid tenantId = Guid.NewGuid();

        Permission write = Permission.Create("orders.write", "Write orders");
        db.Permissions.Add(write);

        Role role = Role.Create(tenantId, "Writer", "Writer");
        role.AssignPermission(write.Id);
        db.Roles.Add(role);

        Group group = Group.Create(tenantId, "Writers", "Writers");
        group.AssignRole(role.Id);
        db.Groups.Add(group);

        User user = User.ProvisionFromEntra(tenantId, Guid.NewGuid(), "a@b.com", "A");
        user.AddToGroup(group.Id);
        db.Users.Add(user);

        await db.SaveChangesAsync(CancellationToken.None);

        var resolver = new PermissionResolver(db);

        var permissions = await resolver.ResolveAsync(tenantId, user.Id, CancellationToken.None);

        permissions.ShouldBe(new[] { "orders.write" });
    }

    [Fact]
    public async Task Should_DeduplicatePermissionsFromBothSources()
    {
        await using TestAuthDbContext db = TestAuthDbContext.Create();
        Guid tenantId = Guid.NewGuid();

        Permission read = Permission.Create("orders.read", "Read orders");
        Permission write = Permission.Create("orders.write", "Write orders");
        db.Permissions.AddRange(read, write);

        Role directRole = Role.Create(tenantId, "Direct", "Direct");
        directRole.AssignPermission(read.Id);
        directRole.AssignPermission(write.Id);

        Role groupRole = Role.Create(tenantId, "ViaGroup", "ViaGroup");
        groupRole.AssignPermission(read.Id);
        db.Roles.AddRange(directRole, groupRole);

        Group group = Group.Create(tenantId, "Group", "Group");
        group.AssignRole(groupRole.Id);
        db.Groups.Add(group);

        User user = User.ProvisionFromEntra(tenantId, Guid.NewGuid(), "a@b.com", "A");
        user.AssignRole(directRole.Id);
        user.AddToGroup(group.Id);
        db.Users.Add(user);

        await db.SaveChangesAsync(CancellationToken.None);

        var resolver = new PermissionResolver(db);

        var permissions = await resolver.ResolveAsync(tenantId, user.Id, CancellationToken.None);

        permissions.Count.ShouldBe(2);
        permissions.ShouldContain("orders.read");
        permissions.ShouldContain("orders.write");
    }
}
