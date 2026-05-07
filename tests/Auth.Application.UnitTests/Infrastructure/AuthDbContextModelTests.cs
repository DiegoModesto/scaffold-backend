using Auth.Domain.Groups;
using Auth.Domain.Permissions;
using Auth.Domain.Roles;
using Auth.Domain.Users;
using Auth.Infra.Database;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Auth.Application.UnitTests.Infrastructure;

/// <summary>
/// Verifies that the EF model produced by AuthDbContext expresses many-to-many
/// associations as explicit join tables with composite primary keys, per the
/// SSO/Auth design spec (§6).
/// </summary>
public sealed class AuthDbContextModelTests
{
    private static AuthDbContext NewContext() => TestAuthDbContext.CreateProduction();

    [Theory]
    [InlineData("user_roles", "UserId", "RoleId")]
    [InlineData("user_groups", "UserId", "GroupId")]
    [InlineData("group_roles", "GroupId", "RoleId")]
    [InlineData("role_permissions", "RoleId", "PermissionId")]
    public void JoinTable_ShouldHaveCompositePrimaryKey(string tableName, string left, string right)
    {
        using AuthDbContext db = NewContext();
        var et = db.Model.GetEntityTypes().FirstOrDefault(e => e.GetTableName() == tableName);

        et.ShouldNotBeNull($"Expected join table '{tableName}' to be mapped.");
        var pk = et.FindPrimaryKey();
        pk.ShouldNotBeNull();
        var keyNames = pk!.Properties.Select(p => p.Name).ToHashSet();
        keyNames.ShouldContain(left);
        keyNames.ShouldContain(right);
        keyNames.Count.ShouldBe(2);
    }

    [Fact]
    public void ParentTables_ShouldNotPersistInMemoryIdCollections()
    {
        using AuthDbContext db = NewContext();

        var user = db.Model.FindEntityType(typeof(User))!;
        user.GetProperties().Select(p => p.GetColumnName()).ShouldNotContain("role_ids");
        user.GetProperties().Select(p => p.GetColumnName()).ShouldNotContain("group_ids");

        var role = db.Model.FindEntityType(typeof(Role))!;
        role.GetProperties().Select(p => p.GetColumnName()).ShouldNotContain("permission_ids");

        var group = db.Model.FindEntityType(typeof(Group))!;
        group.GetProperties().Select(p => p.GetColumnName()).ShouldNotContain("role_ids");
    }

    [Fact]
    public async Task Reconciliation_ShouldMaterializeAndRemoveUserRoleJoinRows()
    {
        await using AuthDbContext db = NewContext();
        Guid tenantId = Guid.NewGuid();

        Role roleA = Role.Create(tenantId, "A", "A");
        Role roleB = Role.Create(tenantId, "B", "B");
        db.Roles.AddRange(roleA, roleB);

        User user = User.ProvisionFromEntra(tenantId, Guid.NewGuid(), "a@b.com", "A");
        user.AssignRole(roleA.Id);
        user.AssignRole(roleB.Id);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Verify both rows materialized in the join set.
        var rows = await db.UserRoles.Where(ur => ur.UserId == user.Id).ToListAsync();
        rows.Count.ShouldBe(2);

        // Revoke one role and re-save — the join row for roleA must disappear.
        user.RevokeRole(roleA.Id);
        await db.SaveChangesAsync();

        var afterRows = await db.UserRoles.Where(ur => ur.UserId == user.Id).ToListAsync();
        afterRows.Count.ShouldBe(1);
        afterRows[0].RoleId.ShouldBe(roleB.Id);
    }
}
